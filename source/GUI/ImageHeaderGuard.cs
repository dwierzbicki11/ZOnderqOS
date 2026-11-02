using System;
using System.IO;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Fail-closed dimension preflight before the Cosmos PNG/BMP decoders
    /// allocate their pixel arrays. The decoder still validates image data;
    /// this guard only limits the maximum allocation requested by a header.
    /// </summary>
    public static class ImageHeaderGuard
    {
        // A decoded ARGB image at the pixel limit already needs 64 MiB.
        // Keep the GUI responsive on machines with limited available RAM.
        public const int MaxDimension = 16384;
        public const long MaxPixels = 16L * 1024 * 1024;

        public static bool IsSafeToDecode(string path, bool png)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                using (Stream stream = File.OpenRead(path))
                {
                    // Read complete PNG IHDR (33 B) and BMP file/DIB prefix (54 B).
                    // The fixed buffer bounds preflight memory usage.
                    byte[] header = new byte[54];
                    int read = 0;
                    while (read < header.Length)
                    {
                        int count = stream.Read(header, read, header.Length - read);
                        if (count <= 0)
                            break;
                        read += count;
                    }
                    return HasSafeDimensions(header, read, png);
                }
            }
            catch (Exception)
            {
                // Removed volumes, broken files and permission failures must
                // never reach the decoder or crash the desktop.
                return false;
            }
        }

        // Pure parser for host C# regression tests. Does not trust length,
        // signature, IHDR type, DIB size or signed BMP dimensions.
        public static bool HasSafeDimensions(byte[] header, int length, bool png)
        {
            if (header == null || length < 0 || length > header.Length)
                return false;

            long width;
            long height;
            if (png)
            {
                if (length < 33 ||
                    header[0] != 137 || header[1] != 80 ||
                    header[2] != 78 || header[3] != 71 ||
                    header[4] != 13 || header[5] != 10 ||
                    header[6] != 26 || header[7] != 10 ||
                    header[8] != 0 || header[9] != 0 ||
                    header[10] != 0 || header[11] != 13 ||
                    header[12] != 73 || header[13] != 72 ||
                    header[14] != 68 || header[15] != 82)
                    return false;

                width = ReadBigEndian32(header, 16);
                height = ReadBigEndian32(header, 20);
                // Validate PNG IHDR metadata before entering Cosmos decoder.
                if (!IsValidPngColorDepth(header[24], header[25]) ||
                    header[26] != 0 || header[27] != 0 || header[28] > 1 ||
                    !HasValidPngIhdrCrc(header))
                    return false;
            }
            else
            {
                if (length < 54 || header[0] != 66 || header[1] != 77)
                    return false;

                uint dibSize = ReadLittleEndian32(header, 14);
                uint pixelOffset = ReadLittleEndian32(header, 10);
                // Cast before addition: a corrupt 32-bit DIB size must not wrap.
                if (dibSize < 40 || (long)pixelOffset < 14L + dibSize ||
                    ReadLittleEndian16(header, 26) != 1 ||
                    ReadLittleEndian16(header, 28) == 0)
                    return false;

                int signedWidth = unchecked((int)ReadLittleEndian32(header, 18));
                int signedHeight = unchecked((int)ReadLittleEndian32(header, 22));
                width = signedWidth;
                height = signedHeight < 0 ? -(long)signedHeight : signedHeight;
            }

            return width > 0 && height > 0 &&
                   width <= MaxDimension && height <= MaxDimension &&
                   width * height <= MaxPixels;
        }

        // CRC-32 of IHDR type + 13 data bytes. The decoder must not be
        // allowed to allocate pixels based on a corrupted width/height.
        // This always processes 17 bytes, independent of the input file size.
        private static bool HasValidPngIhdrCrc(byte[] header)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 12; i < 29; i++)
            {
                crc ^= header[i];
                for (int bit = 0; bit < 8; bit++)
                    crc = (crc & 1u) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
            return ~crc == ReadBigEndian32(header, 29);
        }

        private static bool IsValidPngColorDepth(byte depth, byte colorType)
        {
            switch (colorType)
            {
                case 0: return depth == 1 || depth == 2 || depth == 4 || depth == 8 || depth == 16;
                case 2: return depth == 8 || depth == 16;
                case 3: return depth == 1 || depth == 2 || depth == 4 || depth == 8;
                case 4:
                case 6: return depth == 8 || depth == 16;
                default: return false;
            }
        }

        private static ushort ReadLittleEndian16(byte[] bytes, int offset)
        {
            return (ushort)(bytes[offset] | (bytes[offset + 1] << 8));
        }

        private static uint ReadBigEndian32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) |
                   ((uint)bytes[offset + 1] << 16) |
                   ((uint)bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }

        private static uint ReadLittleEndian32(byte[] bytes, int offset)
        {
            return (uint)bytes[offset] |
                   ((uint)bytes[offset + 1] << 8) |
                   ((uint)bytes[offset + 2] << 16) |
                   ((uint)bytes[offset + 3] << 24);
        }
    }
}
