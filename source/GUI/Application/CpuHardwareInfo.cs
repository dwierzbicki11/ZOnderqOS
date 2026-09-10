using System;
using System.Runtime.Intrinsics.X86;
using System.Text;

namespace ZonderqOS.GUI.Apps
{
    public sealed class CpuHardwareInfo
    {
        public string Architecture = "64-bit";
        public string Vendor = "N/A";
        public string Brand = "Processor";
        public string Features = "N/A";
        public int Family;
        public int Model;
        public int Stepping;
        public int PhysicalCores;
        public int LogicalProcessors;
        public int ThreadsPerCore;
        public int BaseMHz;
        public int MaxMHz;
        public int BusMHz;
        public ulong L1Bytes;
        public ulong L2Bytes;
        public ulong L3Bytes;
        public bool HypervisorPresent;
        public bool VirtualizationSupported;
        public bool CpuidAvailable;

        public static CpuHardwareInfo Detect()
        {
            CpuHardwareInfo info = new CpuHardwareInfo();
            try
            {
                if (!X86Base.IsSupported)
                    return info;

                info.CpuidAvailable = true;
                info.Architecture = "x86-64";

                int maxBasicSigned;
                int vendorB;
                int vendorC;
                int vendorD;
                (maxBasicSigned, vendorB, vendorC, vendorD) = X86Base.CpuId(0, 0);
                uint maxBasic = (uint)maxBasicSigned;
                info.Vendor = ReadAscii(vendorB, vendorD, vendorC);

                uint maxExtended = 0;
                try
                {
                    int extMax;
                    int extB;
                    int extC;
                    int extD;
                    (extMax, extB, extC, extD) = X86Base.CpuId(unchecked((int)0x80000000u), 0);
                    maxExtended = (uint)extMax;
                }
                catch
                {
                    maxExtended = 0;
                }

                if (maxExtended >= 0x80000004u)
                {
                    StringBuilder brand = new StringBuilder(48);
                    for (uint leaf = 0x80000002u; leaf <= 0x80000004u; leaf++)
                    {
                        int a;
                        int b;
                        int c;
                        int d;
                        (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), 0);
                        AppendRegister(brand, a);
                        AppendRegister(brand, b);
                        AppendRegister(brand, c);
                        AppendRegister(brand, d);
                    }
                    string detectedBrand = CollapseSpaces(brand.ToString());
                    if (!string.IsNullOrEmpty(detectedBrand))
                        info.Brand = detectedBrand;
                }

                if (maxBasic >= 1)
                {
                    int signature;
                    int misc;
                    int featureC;
                    int featureD;
                    (signature, misc, featureC, featureD) = X86Base.CpuId(1, 0);
                    DecodeSignature((uint)signature, info);
                    info.LogicalProcessors = (int)(((uint)misc >> 16) & 0xFFu);
                    if (info.LogicalProcessors <= 0)
                        info.LogicalProcessors = 1;

                    uint ecx = (uint)featureC;
                    uint edx = (uint)featureD;
                    info.HypervisorPresent = (ecx & (1u << 31)) != 0;
                    bool vmx = (ecx & (1u << 5)) != 0;
                    bool svm = false;
                    if (maxExtended >= 0x80000001u)
                    {
                        int ea;
                        int eb;
                        int ec;
                        int ed;
                        (ea, eb, ec, ed) = X86Base.CpuId(unchecked((int)0x80000001u), 0);
                        svm = (((uint)ec) & (1u << 2)) != 0;
                    }
                    info.VirtualizationSupported = vmx || svm;
                    info.Features = BuildFeatureList(maxBasic, ecx, edx, maxExtended);
                }

                if (maxBasic >= 4)
                    DetectDeterministicCaches(info);

                if (info.PhysicalCores <= 0 && maxExtended >= 0x80000008u)
                {
                    int a;
                    int b;
                    int c;
                    int d;
                    (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000008u), 0);
                    info.PhysicalCores = (int)(((uint)c & 0xFFu) + 1u);
                }

                if ((info.L1Bytes == 0 || info.L2Bytes == 0) && maxExtended >= 0x80000006u)
                    DetectLegacyExtendedCaches(info, maxExtended);

                if (info.PhysicalCores <= 0)
                    info.PhysicalCores = Math.Max(1, info.LogicalProcessors);
                if (info.LogicalProcessors <= 0)
                    info.LogicalProcessors = Math.Max(1, info.PhysicalCores);
                info.ThreadsPerCore = Math.Max(1, info.LogicalProcessors / Math.Max(1, info.PhysicalCores));

                if (maxBasic >= 0x16u)
                {
                    int speedA;
                    int speedB;
                    int speedC;
                    int speedD;
                    (speedA, speedB, speedC, speedD) = X86Base.CpuId(0x16, 0);
                    info.BaseMHz = speedA & 0xFFFF;
                    info.MaxMHz = speedB & 0xFFFF;
                    info.BusMHz = speedC & 0xFFFF;
                }
            }
            catch
            {
            }

            return info;
        }

        private static void DecodeSignature(uint signature, CpuHardwareInfo info)
        {
            int stepping = (int)(signature & 0xFu);
            int model = (int)((signature >> 4) & 0xFu);
            int family = (int)((signature >> 8) & 0xFu);
            int extendedModel = (int)((signature >> 16) & 0xFu);
            int extendedFamily = (int)((signature >> 20) & 0xFFu);

            if (family == 6 || family == 15)
                model += extendedModel << 4;
            if (family == 15)
                family += extendedFamily;

            info.Family = family;
            info.Model = model;
            info.Stepping = stepping;
        }

        private static void DetectDeterministicCaches(CpuHardwareInfo info)
        {
            for (int subleaf = 0; subleaf < 16; subleaf++)
            {
                int eax;
                int ebx;
                int ecx;
                int edx;
                (eax, ebx, ecx, edx) = X86Base.CpuId(4, subleaf);
                uint a = (uint)eax;
                uint b = (uint)ebx;
                uint c = (uint)ecx;
                int cacheType = (int)(a & 0x1Fu);
                if (cacheType == 0)
                    break;

                int level = (int)((a >> 5) & 0x7u);
                ulong lineSize = (b & 0xFFFu) + 1u;
                ulong partitions = ((b >> 12) & 0x3FFu) + 1u;
                ulong ways = ((b >> 22) & 0x3FFu) + 1u;
                ulong sets = (ulong)c + 1UL;
                ulong size = lineSize * partitions * ways * sets;

                int sharing = (int)(((a >> 14) & 0xFFFu) + 1u);
                int logical = Math.Max(1, info.LogicalProcessors);
                int instances = Math.Max(1, logical / Math.Max(1, sharing));
                size *= (ulong)instances;

                if (level == 1)
                    info.L1Bytes += size;
                else if (level == 2)
                    info.L2Bytes += size;
                else if (level == 3)
                    info.L3Bytes += size;

                if (info.PhysicalCores <= 0)
                    info.PhysicalCores = (int)(((a >> 26) & 0x3Fu) + 1u);
            }
        }

        private static void DetectLegacyExtendedCaches(CpuHardwareInfo info, uint maxExtended)
        {
            if (maxExtended >= 0x80000005u && info.L1Bytes == 0)
            {
                int a;
                int b;
                int c;
                int d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000005u), 0);
                ulong l1DataKb = ((uint)c >> 24) & 0xFFu;
                ulong l1InstructionKb = ((uint)d >> 24) & 0xFFu;
                info.L1Bytes = (l1DataKb + l1InstructionKb) * 1024UL;
            }

            if (maxExtended >= 0x80000006u)
            {
                int a;
                int b;
                int c;
                int d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000006u), 0);
                if (info.L2Bytes == 0)
                    info.L2Bytes = (((uint)c >> 16) & 0xFFFFu) * 1024UL;
                if (info.L3Bytes == 0)
                {
                    ulong units = ((uint)d >> 18) & 0x3FFFu;
                    info.L3Bytes = units * 512UL * 1024UL;
                }
            }
        }

        private static string BuildFeatureList(uint maxBasic, uint ecx, uint edx, uint maxExtended)
        {
            StringBuilder result = new StringBuilder(96);
            AddFeature(result, (edx & (1u << 25)) != 0, "SSE");
            AddFeature(result, (edx & (1u << 26)) != 0, "SSE2");
            AddFeature(result, (ecx & (1u << 0)) != 0, "SSE3");
            AddFeature(result, (ecx & (1u << 9)) != 0, "SSSE3");
            AddFeature(result, (ecx & (1u << 19)) != 0, "SSE4.1");
            AddFeature(result, (ecx & (1u << 20)) != 0, "SSE4.2");
            AddFeature(result, (ecx & (1u << 25)) != 0, "AES");
            AddFeature(result, (ecx & (1u << 28)) != 0, "AVX");

            if (maxBasic >= 7)
            {
                int a;
                int b;
                int c;
                int d;
                (a, b, c, d) = X86Base.CpuId(7, 0);
                uint ebx = (uint)b;
                AddFeature(result, (ebx & (1u << 3)) != 0, "BMI1");
                AddFeature(result, (ebx & (1u << 5)) != 0, "AVX2");
                AddFeature(result, (ebx & (1u << 8)) != 0, "BMI2");
                AddFeature(result, (ebx & (1u << 29)) != 0, "SHA");
            }

            if (maxExtended >= 0x80000001u)
            {
                int a;
                int b;
                int c;
                int d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000001u), 0);
                AddFeature(result, (((uint)d) & (1u << 20)) != 0, "NX");
            }

            return result.Length == 0 ? "N/A" : result.ToString();
        }

        private static void AddFeature(StringBuilder builder, bool available, string name)
        {
            if (!available)
                return;
            if (builder.Length > 0)
                builder.Append(' ');
            builder.Append(name);
        }

        private static string ReadAscii(int first, int second, int third)
        {
            StringBuilder builder = new StringBuilder(12);
            AppendRegister(builder, first);
            AppendRegister(builder, second);
            AppendRegister(builder, third);
            return CollapseSpaces(builder.ToString());
        }

        private static void AppendRegister(StringBuilder builder, int value)
        {
            uint data = (uint)value;
            for (int i = 0; i < 4; i++)
            {
                char ch = (char)((data >> (i * 8)) & 0xFFu);
                if (ch == '\0')
                    return;
                builder.Append(ch >= 32 && ch <= 126 ? ch : ' ');
            }
        }

        private static string CollapseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";

            StringBuilder result = new StringBuilder(value.Length);
            bool lastSpace = true;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool space = ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
                if (space)
                {
                    if (!lastSpace)
                    {
                        result.Append(' ');
                        lastSpace = true;
                    }
                }
                else if (ch >= 32 && ch <= 126)
                {
                    result.Append(ch);
                    lastSpace = false;
                }
            }
            return result.ToString().Trim();
        }
    }
}
