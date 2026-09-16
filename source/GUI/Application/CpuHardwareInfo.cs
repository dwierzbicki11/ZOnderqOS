using System;
using System.Text;
using Cosmos.Kernel.System.Diagnostics;
#if !ARCH_ARM64
using System.Runtime.Intrinsics.X86;
#endif

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

#if ARCH_ARM64
            // Cosmos 3.0.84 currently brings up one managed CPU on ARM64/QEMU
            // (ARM64PlatformInitializer.GetCpuCount() returns 1). Keep Task Manager
            // honest and architecture-aware instead of falling through to x86 CPUID.
            info.Architecture = "ARM64";
            info.Vendor = "ARM";
            info.Brand = "Cortex-A72 (QEMU virt)";
            info.Features = "AArch64";
            int managedCpuCount = 1;
            try
            {
                managedCpuCount = Math.Max(1, (int)SchedulerInfo.CpuCount);
            }
            catch
            {
                managedCpuCount = 1;
            }
            info.PhysicalCores = managedCpuCount;
            info.LogicalProcessors = managedCpuCount;
            info.ThreadsPerCore = 1;
            info.HypervisorPresent = true;
            return info;
#else
            try
            {
                if (!X86Base.IsSupported)
                    return info;

                info.CpuidAvailable = true;
                info.Architecture = "x86-64";

                int maxBasicSigned, vendorB, vendorC, vendorD;
                (maxBasicSigned, vendorB, vendorC, vendorD) = X86Base.CpuId(0, 0);
                uint maxBasic = (uint)maxBasicSigned;
                info.Vendor = ReadAscii(vendorB, vendorD, vendorC);

                uint maxExtended = 0;
                try
                {
                    int extMax, b, c, d;
                    (extMax, b, c, d) = X86Base.CpuId(unchecked((int)0x80000000u), 0);
                    maxExtended = (uint)extMax;
                }
                catch { }

                if (maxExtended >= 0x80000004u)
                {
                    StringBuilder brand = new StringBuilder(48);
                    for (uint leaf = 0x80000002u; leaf <= 0x80000004u; leaf++)
                    {
                        int a, b, c, d;
                        (a, b, c, d) = X86Base.CpuId(unchecked((int)leaf), 0);
                        AppendRegister(brand, a);
                        AppendRegister(brand, b);
                        AppendRegister(brand, c);
                        AppendRegister(brand, d);
                    }
                    string text = CollapseSpaces(brand.ToString());
                    if (!string.IsNullOrEmpty(text)) info.Brand = text;
                }

                uint featureEcx = 0;
                uint featureEdx = 0;
                if (maxBasic >= 1)
                {
                    int signature, misc, ecx, edx;
                    (signature, misc, ecx, edx) = X86Base.CpuId(1, 0);
                    DecodeSignature((uint)signature, info);
                    info.LogicalProcessors = (int)(((uint)misc >> 16) & 0xFFu);
                    featureEcx = (uint)ecx;
                    featureEdx = (uint)edx;
                    info.HypervisorPresent = (featureEcx & (1u << 31)) != 0;

                    bool svm = false;
                    if (maxExtended >= 0x80000001u)
                    {
                        int a, b, extC, d;
                        (a, b, extC, d) = X86Base.CpuId(unchecked((int)0x80000001u), 0);
                        svm = (((uint)extC) & (1u << 2)) != 0;
                    }
                    info.VirtualizationSupported = (featureEcx & (1u << 5)) != 0 || svm;
                    info.Features = BuildFeatureList(maxBasic, featureEcx, featureEdx, maxExtended);
                }

                DetectExtendedTopology(maxBasic, info);

                if (maxBasic >= 4)
                    DetectDeterministicCaches(info);

                if (info.PhysicalCores <= 0 && maxExtended >= 0x80000008u)
                {
                    int a, b, c, d;
                    (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000008u), 0);
                    info.PhysicalCores = (int)(((uint)c & 0xFFu) + 1u);
                }

                if ((info.L1Bytes == 0 || info.L2Bytes == 0) && maxExtended >= 0x80000006u)
                    DetectLegacyExtendedCaches(info, maxExtended);

                if (info.LogicalProcessors <= 0) info.LogicalProcessors = Math.Max(1, info.PhysicalCores);
                if (info.PhysicalCores <= 0) info.PhysicalCores = Math.Max(1, info.LogicalProcessors);
                if (info.ThreadsPerCore <= 0)
                    info.ThreadsPerCore = Math.Max(1, info.LogicalProcessors / Math.Max(1, info.PhysicalCores));

                if (maxBasic >= 0x16u)
                {
                    int a, b, c, d;
                    (a, b, c, d) = X86Base.CpuId(0x16, 0);
                    info.BaseMHz = a & 0xFFFF;
                    info.MaxMHz = b & 0xFFFF;
                    info.BusMHz = c & 0xFFFF;
                }
            }
            catch
            {
                if (info.LogicalProcessors <= 0) info.LogicalProcessors = 1;
                if (info.PhysicalCores <= 0) info.PhysicalCores = 1;
                if (info.ThreadsPerCore <= 0) info.ThreadsPerCore = 1;
            }

            return info;
#endif
        }

#if !ARCH_ARM64
        private static void DetectExtendedTopology(uint maxBasic, CpuHardwareInfo info)
        {
            uint leaf = maxBasic >= 0x1Fu ? 0x1Fu : maxBasic >= 0x0Bu ? 0x0Bu : 0u;
            if (leaf == 0) return;

            int smtWidth = 0;
            int packageLogical = 0;
            for (int subleaf = 0; subleaf < 8; subleaf++)
            {
                int a, b, c, d;
                (a, b, c, d) = X86Base.CpuId((int)leaf, subleaf);
                int logicalAtLevel = b & 0xFFFF;
                int levelType = (int)(((uint)c >> 8) & 0xFFu);
                if (logicalAtLevel == 0 || levelType == 0) break;
                if (levelType == 1) smtWidth = logicalAtLevel;
                else if (levelType == 2) packageLogical = logicalAtLevel;
            }

            if (packageLogical > 0) info.LogicalProcessors = packageLogical;
            if (smtWidth > 0) info.ThreadsPerCore = smtWidth;
            if (packageLogical > 0 && smtWidth > 0)
                info.PhysicalCores = Math.Max(1, packageLogical / smtWidth);
        }

        private static void DecodeSignature(uint signature, CpuHardwareInfo info)
        {
            int stepping = (int)(signature & 0xFu);
            int model = (int)((signature >> 4) & 0xFu);
            int family = (int)((signature >> 8) & 0xFu);
            int extendedModel = (int)((signature >> 16) & 0xFu);
            int extendedFamily = (int)((signature >> 20) & 0xFFu);
            if (family == 6 || family == 15) model += extendedModel << 4;
            if (family == 15) family += extendedFamily;
            info.Family = family;
            info.Model = model;
            info.Stepping = stepping;
        }

        private static void DetectDeterministicCaches(CpuHardwareInfo info)
        {
            for (int subleaf = 0; subleaf < 16; subleaf++)
            {
                int eax, ebx, ecx, edx;
                (eax, ebx, ecx, edx) = X86Base.CpuId(4, subleaf);
                uint a = (uint)eax;
                uint b = (uint)ebx;
                uint c = (uint)ecx;
                int cacheType = (int)(a & 0x1Fu);
                if (cacheType == 0) break;

                int level = (int)((a >> 5) & 0x7u);
                ulong lineSize = (b & 0xFFFu) + 1u;
                ulong partitions = ((b >> 12) & 0x3FFu) + 1u;
                ulong ways = ((b >> 22) & 0x3FFu) + 1u;
                ulong sets = (ulong)c + 1UL;
                ulong size = lineSize * partitions * ways * sets;

                int sharing = (int)(((a >> 14) & 0xFFFu) + 1u);
                int logical = Math.Max(1, info.LogicalProcessors);
                size *= (ulong)Math.Max(1, logical / Math.Max(1, sharing));

                if (level == 1) info.L1Bytes += size;
                else if (level == 2) info.L2Bytes += size;
                else if (level == 3) info.L3Bytes += size;

                if (info.PhysicalCores <= 0)
                    info.PhysicalCores = (int)(((a >> 26) & 0x3Fu) + 1u);
            }
        }

        private static void DetectLegacyExtendedCaches(CpuHardwareInfo info, uint maxExtended)
        {
            if (maxExtended >= 0x80000005u && info.L1Bytes == 0)
            {
                int a, b, c, d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000005u), 0);
                info.L1Bytes = ((((uint)c >> 24) & 0xFFu) + (((uint)d >> 24) & 0xFFu)) * 1024UL;
            }
            if (maxExtended >= 0x80000006u)
            {
                int a, b, c, d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000006u), 0);
                if (info.L2Bytes == 0) info.L2Bytes = (((uint)c >> 16) & 0xFFFFu) * 1024UL;
                if (info.L3Bytes == 0) info.L3Bytes = (((uint)d >> 18) & 0x3FFFu) * 512UL * 1024UL;
            }
        }

        private static string BuildFeatureList(uint maxBasic, uint ecx, uint edx, uint maxExtended)
        {
            StringBuilder result = new StringBuilder(96);
            AddFeature(result, (edx & (1u << 25)) != 0, "SSE");
            AddFeature(result, (edx & (1u << 26)) != 0, "SSE2");
            AddFeature(result, (ecx & 1u) != 0, "SSE3");
            AddFeature(result, (ecx & (1u << 9)) != 0, "SSSE3");
            AddFeature(result, (ecx & (1u << 19)) != 0, "SSE4.1");
            AddFeature(result, (ecx & (1u << 20)) != 0, "SSE4.2");
            AddFeature(result, (ecx & (1u << 25)) != 0, "AES");
            AddFeature(result, (ecx & (1u << 28)) != 0, "AVX");
            if (maxBasic >= 7)
            {
                int a, b, c, d;
                (a, b, c, d) = X86Base.CpuId(7, 0);
                uint flags = (uint)b;
                AddFeature(result, (flags & (1u << 3)) != 0, "BMI1");
                AddFeature(result, (flags & (1u << 5)) != 0, "AVX2");
                AddFeature(result, (flags & (1u << 8)) != 0, "BMI2");
                AddFeature(result, (flags & (1u << 29)) != 0, "SHA");
            }
            if (maxExtended >= 0x80000001u)
            {
                int a, b, c, d;
                (a, b, c, d) = X86Base.CpuId(unchecked((int)0x80000001u), 0);
                AddFeature(result, (((uint)d) & (1u << 20)) != 0, "NX");
            }
            return result.Length == 0 ? "N/A" : result.ToString();
        }

        private static void AddFeature(StringBuilder builder, bool available, string name)
        {
            if (!available) return;
            if (builder.Length > 0) builder.Append(' ');
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
                if (ch == '\0') return;
                builder.Append(ch >= 32 && ch <= 126 ? ch : ' ');
            }
        }

        private static string CollapseSpaces(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            StringBuilder result = new StringBuilder(value.Length);
            bool lastSpace = true;
            for (int i = 0; i < value.Length; i++)
            {
                char ch = value[i];
                bool space = ch == ' ' || ch == '\t' || ch == '\r' || ch == '\n';
                if (space)
                {
                    if (!lastSpace) { result.Append(' '); lastSpace = true; }
                }
                else if (ch >= 32 && ch <= 126)
                {
                    result.Append(ch);
                    lastSpace = false;
                }
            }
            return result.ToString().Trim();
        }
#endif
    }
}
