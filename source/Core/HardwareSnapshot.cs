using System;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.Platform;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Architecture-neutral, read-only hardware/system snapshot used by shell
    /// and GUI diagnostics. Platform-specific CPU discovery stays in the
    /// existing CpuHardwareInfo backend.
    /// </summary>
    public sealed class HardwareSnapshot
    {
        public string Architecture { get; private set; } = "N/A";
        public string CpuVendor { get; private set; } = "N/A";
        public string CpuBrand { get; private set; } = "Processor";
        public string CpuFeatures { get; private set; } = "N/A";
        public int PhysicalCores { get; private set; }
        public int LogicalProcessors { get; private set; }
        public int ThreadsPerCore { get; private set; }
        public int BaseMHz { get; private set; }
        public int MaxMHz { get; private set; }
        public int BusMHz { get; private set; }
        public ulong L1Bytes { get; private set; }
        public ulong L2Bytes { get; private set; }
        public ulong L3Bytes { get; private set; }
        public bool HypervisorPresent { get; private set; }
        public bool VirtualizationSupported { get; private set; }

        public uint OnlineCpuCount { get; private set; }
        public string SchedulerName { get; private set; } = "N/A";
        public int SchedulerThreadSlots { get; private set; }
        public int StorageDeviceCount { get; private set; }
        public int StoragePartitionCount { get; private set; }

        public ulong TotalMemoryBytes { get; private set; }
        public ulong FreeMemoryBytes { get; private set; }
        public ulong UsedMemoryBytes => TotalMemoryBytes >= FreeMemoryBytes
            ? TotalMemoryBytes - FreeMemoryBytes
            : 0;

        public static HardwareSnapshot Capture()
        {
            HardwareSnapshot snapshot = new HardwareSnapshot();

            try
            {
                CpuHardwareInfo cpu = CpuHardwareInfo.Detect();
                snapshot.Architecture = Safe(cpu.Architecture, "N/A");
                snapshot.CpuVendor = Safe(cpu.Vendor, "N/A");
                snapshot.CpuBrand = Safe(cpu.Brand, "Processor");
                snapshot.CpuFeatures = Safe(cpu.Features, "N/A");
                snapshot.PhysicalCores = Math.Max(1, cpu.PhysicalCores);
                snapshot.LogicalProcessors = Math.Max(1, cpu.LogicalProcessors);
                snapshot.ThreadsPerCore = Math.Max(1, cpu.ThreadsPerCore);
                snapshot.BaseMHz = Math.Max(0, cpu.BaseMHz);
                snapshot.MaxMHz = Math.Max(0, cpu.MaxMHz);
                snapshot.BusMHz = Math.Max(0, cpu.BusMHz);
                snapshot.L1Bytes = cpu.L1Bytes;
                snapshot.L2Bytes = cpu.L2Bytes;
                snapshot.L3Bytes = cpu.L3Bytes;
                snapshot.HypervisorPresent = cpu.HypervisorPresent;
                snapshot.VirtualizationSupported = cpu.VirtualizationSupported;
            }
            catch
            {
                snapshot.PhysicalCores = 1;
                snapshot.LogicalProcessors = 1;
                snapshot.ThreadsPerCore = 1;
            }

            try
            {
                snapshot.OnlineCpuCount = SchedulerInfo.CpuCount;
                snapshot.SchedulerName = Safe(SchedulerInfo.SchedulerName, "N/A");
                snapshot.SchedulerThreadSlots = Math.Max(0, SchedulerInfo.ThreadSlotCount);
            }
            catch
            {
                snapshot.OnlineCpuCount = 0;
                snapshot.SchedulerThreadSlots = 0;
            }

            try
            {
                snapshot.StorageDeviceCount = Math.Max(0, StorageManager.DeviceCount);
                snapshot.StoragePartitionCount = StorageManager.Partitions == null
                    ? 0
                    : Math.Max(0, StorageManager.Partitions.Count);
            }
            catch
            {
                snapshot.StorageDeviceCount = 0;
                snapshot.StoragePartitionCount = 0;
            }

            try
            {
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = Math.Min(PageAllocator.FreePageCount, totalPages);
                ulong pageSize = PageAllocator.PageSize;

                snapshot.TotalMemoryBytes = SaturatingMultiply(totalPages, pageSize);
                snapshot.FreeMemoryBytes = SaturatingMultiply(freePages, pageSize);
            }
            catch
            {
                snapshot.TotalMemoryBytes = 0;
                snapshot.FreeMemoryBytes = 0;
            }

            return snapshot;
        }

        private static string Safe(string value, string fallback) =>
            string.IsNullOrWhiteSpace(value) ? fallback : value;

        private static ulong SaturatingMultiply(ulong left, ulong right)
        {
            if (left == 0 || right == 0)
                return 0;
            if (left > ulong.MaxValue / right)
                return ulong.MaxValue;
            return left * right;
        }
    }
}
