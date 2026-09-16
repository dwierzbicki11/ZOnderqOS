using System;
using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// ARM64/QEMU virt CPU discovery. This file is compiled only for the ARM64
    /// Cosmos target. The GUI consumes the same CpuHardwareInfo contract on both
    /// architectures and never needs to reference x86 intrinsics or ARCH_* symbols.
    /// </summary>
    public sealed class CpuHardwareInfo
    {
        public string Architecture = "ARM64";
        public string Vendor = "ARM";
        public string Brand = "Cortex-A72 (QEMU virt)";
        public string Features = "AArch64";
        public int Family;
        public int Model;
        public int Stepping;
        public int PhysicalCores;
        public int LogicalProcessors;
        public int ThreadsPerCore = 1;
        public int BaseMHz;
        public int MaxMHz;
        public int BusMHz;
        public ulong L1Bytes;
        public ulong L2Bytes;
        public ulong L3Bytes;
        public bool HypervisorPresent = true;
        public bool VirtualizationSupported;
        public bool CpuidAvailable;

        public static CpuHardwareInfo Detect()
        {
            CpuHardwareInfo info = new CpuHardwareInfo();
            int managedCpuCount = 1;

            try
            {
                managedCpuCount = Math.Max(1, (int)SchedulerInfo.CpuCount);
            }
            catch
            {
                managedCpuCount = 1;
            }

            // Cosmos ARM64 currently exposes the scheduler-managed CPU count.
            // Do not pretend QEMU vCPUs are online until the HAL actually brings
            // secondary processors up and registers them with the scheduler.
            info.PhysicalCores = managedCpuCount;
            info.LogicalProcessors = managedCpuCount;
            info.ThreadsPerCore = 1;
            return info;
        }
    }
}
