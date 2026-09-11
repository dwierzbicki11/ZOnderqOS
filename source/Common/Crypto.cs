using System;
using System.Diagnostics;
using Cosmos.Kernel.HAL.X64.Devices.Clock;

namespace ZonderqOS
{
    public static class Crypto
    {
        // Deliberately moderate for Cosmos/QEMU. Authentication is infrequent and also
        // protected by AuthenticationGuard, while 4096 rounds are a major improvement
        // over the legacy one-pass FNV format. The iteration count is stored in shadow so
        // it can be raised later without invalidating existing accounts.
        public const int PasswordHashIterations = 4096;

        private static readonly uint[] Sha256K =
        {
            0x428A2F98U, 0x71374491U, 0xB5C0FBCFU, 0xE9B5DBA5U,
            0x3956C25BU, 0x59F111F1U, 0x923F82A4U, 0xAB1C5ED5U,
            0xD807AA98U, 0x12835B01U, 0x243185BEU, 0x550C7DC3U,
            0x72BE5D74U, 0x80DEB1FEU, 0x9BDC06A7U, 0xC19BF174U,
            0xE49B69C1U, 0xEFBE4786U, 0x0FC19DC6U, 0x240CA1CCU,
            0x2DE92C6FU, 0x4A7484AAU, 0x5CB0A9DCU, 0x76F988DAU,
            0x983E5152U, 0xA831C66DU, 0xB00327C8U, 0xBF597FC7U,
            0xC6E00BF3U, 0xD5A79147U, 0x06CA6351U, 0x14292967U,
            0x27B70A85U, 0x2E1B2138U, 0x4D2C6DFCU, 0x53380D13U,
            0x650A7354U, 0x766A0ABBU, 0x81C2C92EU, 0x92722C85U,
            0xA2BFE8A1U, 0xA81A664BU, 0xC24B8B70U, 0xC76C51A3U,
            0xD192E819U, 0xD6990624U, 0xF40E3585U, 0x106AA070U,
            0x19A4C116U, 0x1E376C08U, 0x2748774CU, 0x34B0BCB5U,
            0x391C0CB3U, 0x4ED8AA4AU, 0x5B9CCA4FU, 0x682E6FF3U,
            0x748F82EEU, 0x78A5636FU, 0x84C87814U, 0x8CC70208U,
            0x90BEFFFAU, 0xA4506CEBU, 0xBEF9A3F7U, 0xC67178F2U
        };

        private static ulong saltCounter;

        public static string GenerateSalt()
        {
            // Cosmos Gen3 does not currently expose a CSPRNG through this project. The salt
            // is not a secret; uniqueness is what matters here. Mix RTC, monotonic time and
            // a per-boot counter so credentials created in the same RTC second still differ.
            ulong rtcValue = 0;
            try
            {
                var (y, m, d, h, min, s) = RTC.ReadTime();
                rtcValue = (ulong)y * 31536000UL + (ulong)m * 2592000UL +
                           (ulong)d * 86400UL + (ulong)h * 3600UL +
                           (ulong)min * 60UL + (ulong)s;
            }
            catch
            {
                rtcValue = 0xA9F2BUL;
            }

            ulong monotonic = 0;
            try
            {
                long timestamp = Stopwatch.GetTimestamp();
                monotonic = timestamp > 0 ? (ulong)timestamp : 0UL;
            }
            catch
            {
                monotonic = 0UL;
            }

            saltCounter++;
            ulong mixed = rtcValue ^ RotateLeft64(monotonic, 17) ^
                          (saltCounter * 0x9E3779B97F4A7C15UL);
            mixed = Mix64(mixed);
            ulong second = Mix64(mixed ^ monotonic ^ 0xD6E8FEB86659FD93UL);
            return mixed.ToString("X16") + second.ToString("X16");
        }

        /// <summary>
        /// Legacy one-pass FNV representation kept only so existing installations can log
        /// in once and be migrated to the versioned PBKDF2 format.
        /// </summary>
        public static string HashPassword(string password, string salt)
        {
            string combined = (password ?? string.Empty) + (salt ?? string.Empty);
            ulong hash = 14695981039346656037UL;
            for (int i = 0; i < combined.Length; i++)
            {
                hash ^= combined[i];
                hash *= 1099511628211UL;
            }

            return hash.ToString("X");
        }

        /// <summary>
        /// PBKDF2-HMAC-SHA256 implemented locally so credential hardening does not depend on
        /// System.Security.Cryptography support in the current Cosmos Gen3 runtime.
        /// Inputs are printable ASCII by PasswordPolicy. Scratch buffers are allocated once
        /// per authentication, not once per iteration, preventing thousands of GC objects.
        /// </summary>
        public static string HashPasswordV2(string password, string salt, int iterations)
        {
            if (password == null || salt == null || iterations < 1 || iterations > 1000000)
                return null;

            byte[] key = ToAscii(password);
            byte[] saltBytes = ToAscii(salt);
            byte[] keyBlock = new byte[64];
            byte[] keyHash = new byte[32];
            byte[] ipad = new byte[64];
            byte[] opad = new byte[64];
            byte[] data = new byte[System.Math.Max(36, saltBytes.Length + 4)];
            byte[] u = new byte[32];
            byte[] nextU = new byte[32];
            byte[] result = new byte[32];
            byte[] innerHash = new byte[32];
            byte[] innerBuffer = new byte[64 + data.Length];
            byte[] outerBuffer = new byte[96];
            uint[] schedule = new uint[64];

            if (key.Length > 64)
            {
                Sha256Into(key, key.Length, keyHash, schedule);
                CopyBytes(keyHash, 0, keyBlock, 0, keyHash.Length);
            }
            else
            {
                CopyBytes(key, 0, keyBlock, 0, key.Length);
            }

            for (int i = 0; i < 64; i++)
            {
                ipad[i] = (byte)(keyBlock[i] ^ 0x36);
                opad[i] = (byte)(keyBlock[i] ^ 0x5C);
                innerBuffer[i] = ipad[i];
                outerBuffer[i] = opad[i];
            }

            CopyBytes(saltBytes, 0, data, 0, saltBytes.Length);
            int firstLength = saltBytes.Length + 4;
            data[saltBytes.Length] = 0;
            data[saltBytes.Length + 1] = 0;
            data[saltBytes.Length + 2] = 0;
            data[saltBytes.Length + 3] = 1;

            HmacSha256Into(data, firstLength, u, innerHash, innerBuffer, outerBuffer, schedule);
            CopyBytes(u, 0, result, 0, 32);

            for (int round = 1; round < iterations; round++)
            {
                HmacSha256Into(u, 32, nextU, innerHash, innerBuffer, outerBuffer, schedule);
                for (int i = 0; i < 32; i++)
                {
                    u[i] = nextU[i];
                    result[i] ^= u[i];
                }
            }

            string hex = ToHex(result);
            Zero(key);
            Zero(saltBytes);
            Zero(keyBlock);
            Zero(keyHash);
            Zero(ipad);
            Zero(opad);
            Zero(data);
            Zero(u);
            Zero(nextU);
            Zero(result);
            Zero(innerHash);
            Zero(innerBuffer);
            Zero(outerBuffer);
            for (int i = 0; i < schedule.Length; i++)
                schedule[i] = 0;
            return hex;
        }

        public static bool FixedTimeEquals(string left, string right)
        {
            if (left == null || right == null)
                return false;

            int max = System.Math.Max(left.Length, right.Length);
            int difference = left.Length ^ right.Length;

            for (int i = 0; i < max; i++)
            {
                char a = i < left.Length ? left[i] : '\0';
                char b = i < right.Length ? right[i] : '\0';
                difference |= a ^ b;
            }

            return difference == 0;
        }

        private static void HmacSha256Into(byte[] data, int dataLength, byte[] output,
            byte[] innerHash, byte[] innerBuffer, byte[] outerBuffer, uint[] schedule)
        {
            CopyBytes(data, 0, innerBuffer, 64, dataLength);
            Sha256Into(innerBuffer, 64 + dataLength, innerHash, schedule);
            CopyBytes(innerHash, 0, outerBuffer, 64, 32);
            Sha256Into(outerBuffer, 96, output, schedule);
        }

        private static void Sha256Into(byte[] input, int length, byte[] output, uint[] schedule)
        {
            uint h0 = 0x6A09E667U;
            uint h1 = 0xBB67AE85U;
            uint h2 = 0x3C6EF372U;
            uint h3 = 0xA54FF53AU;
            uint h4 = 0x510E527FU;
            uint h5 = 0x9B05688CU;
            uint h6 = 0x1F83D9ABU;
            uint h7 = 0x5BE0CD19U;

            int totalLength = ((length + 9 + 63) / 64) * 64;
            ulong bitLength = (ulong)length * 8UL;

            for (int blockStart = 0; blockStart < totalLength; blockStart += 64)
            {
                for (int i = 0; i < 16; i++)
                {
                    int pos = blockStart + i * 4;
                    schedule[i] = ((uint)GetPaddedByte(input, length, totalLength, bitLength, pos) << 24) |
                                  ((uint)GetPaddedByte(input, length, totalLength, bitLength, pos + 1) << 16) |
                                  ((uint)GetPaddedByte(input, length, totalLength, bitLength, pos + 2) << 8) |
                                  GetPaddedByte(input, length, totalLength, bitLength, pos + 3);
                }

                for (int i = 16; i < 64; i++)
                {
                    uint s0 = RotateRight(schedule[i - 15], 7) ^
                              RotateRight(schedule[i - 15], 18) ^
                              (schedule[i - 15] >> 3);
                    uint s1 = RotateRight(schedule[i - 2], 17) ^
                              RotateRight(schedule[i - 2], 19) ^
                              (schedule[i - 2] >> 10);
                    schedule[i] = unchecked(schedule[i - 16] + s0 + schedule[i - 7] + s1);
                }

                uint a = h0;
                uint b = h1;
                uint c = h2;
                uint d = h3;
                uint e = h4;
                uint f = h5;
                uint g = h6;
                uint h = h7;

                for (int i = 0; i < 64; i++)
                {
                    uint s1 = RotateRight(e, 6) ^ RotateRight(e, 11) ^ RotateRight(e, 25);
                    uint choose = (e & f) ^ ((~e) & g);
                    uint temp1 = unchecked(h + s1 + choose + Sha256K[i] + schedule[i]);
                    uint s0 = RotateRight(a, 2) ^ RotateRight(a, 13) ^ RotateRight(a, 22);
                    uint majority = (a & b) ^ (a & c) ^ (b & c);
                    uint temp2 = unchecked(s0 + majority);

                    h = g;
                    g = f;
                    f = e;
                    e = unchecked(d + temp1);
                    d = c;
                    c = b;
                    b = a;
                    a = unchecked(temp1 + temp2);
                }

                h0 = unchecked(h0 + a);
                h1 = unchecked(h1 + b);
                h2 = unchecked(h2 + c);
                h3 = unchecked(h3 + d);
                h4 = unchecked(h4 + e);
                h5 = unchecked(h5 + f);
                h6 = unchecked(h6 + g);
                h7 = unchecked(h7 + h);
            }

            WriteUInt32BigEndian(output, 0, h0);
            WriteUInt32BigEndian(output, 4, h1);
            WriteUInt32BigEndian(output, 8, h2);
            WriteUInt32BigEndian(output, 12, h3);
            WriteUInt32BigEndian(output, 16, h4);
            WriteUInt32BigEndian(output, 20, h5);
            WriteUInt32BigEndian(output, 24, h6);
            WriteUInt32BigEndian(output, 28, h7);
        }

        private static byte GetPaddedByte(byte[] input, int length, int totalLength,
            ulong bitLength, int position)
        {
            if (position < length)
                return input[position];
            if (position == length)
                return 0x80;

            int lengthStart = totalLength - 8;
            if (position >= lengthStart)
            {
                int shift = (totalLength - 1 - position) * 8;
                return (byte)((bitLength >> shift) & 0xFFUL);
            }

            return 0;
        }

        private static void WriteUInt32BigEndian(byte[] output, int offset, uint value)
        {
            output[offset] = (byte)(value >> 24);
            output[offset + 1] = (byte)(value >> 16);
            output[offset + 2] = (byte)(value >> 8);
            output[offset + 3] = (byte)value;
        }

        private static uint RotateRight(uint value, int shift)
        {
            return (value >> shift) | (value << (32 - shift));
        }

        private static byte[] ToAscii(string value)
        {
            byte[] data = new byte[value.Length];
            for (int i = 0; i < value.Length; i++)
                data[i] = (byte)value[i];
            return data;
        }

        private static string ToHex(byte[] data)
        {
            const string digits = "0123456789ABCDEF";
            char[] chars = new char[data.Length * 2];
            for (int i = 0; i < data.Length; i++)
            {
                chars[i * 2] = digits[data[i] >> 4];
                chars[i * 2 + 1] = digits[data[i] & 0x0F];
            }
            return new string(chars);
        }

        private static void CopyBytes(byte[] source, int sourceOffset, byte[] destination,
            int destinationOffset, int count)
        {
            for (int i = 0; i < count; i++)
                destination[destinationOffset + i] = source[sourceOffset + i];
        }

        private static void Zero(byte[] data)
        {
            for (int i = 0; i < data.Length; i++)
                data[i] = 0;
        }

        private static ulong Mix64(ulong value)
        {
            value ^= value >> 30;
            value *= 0xBF58476D1CE4E5B9UL;
            value ^= value >> 27;
            value *= 0x94D049BB133111EBUL;
            value ^= value >> 31;
            return value;
        }

        private static ulong RotateLeft64(ulong value, int shift)
        {
            return (value << shift) | (value >> (64 - shift));
        }
    }
}
