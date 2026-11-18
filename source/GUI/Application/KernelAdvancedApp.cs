using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using ZonderqOS.Platform;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public sealed class KernelAdvancedApp : SettingsToolWindow
    {
        private readonly CpuHardwareInfo cpuInfo;
        private string schedulerName = "N/A";
        private int threadCount;
        private uint cpuCount;
        private ulong busyCpuNs;
        private ulong totalRamMb;
        private ulong freeRamMb;
        private ulong gcHeapMb;
        private ulong gcCommittedMb;
        private ulong gcFragmentedKb;
        private ulong gcPinned;
        private ulong gcCollections;
        private ulong uptimeSeconds;
        private HardwareSnapshot hardwareSnapshot;
        private string cachedTopologyText = "0 / 0 / 0";
        private string cachedCacheText = "0 B / 0 B / 0 B";
        private string cachedMemoryStatus = "ORIONGC: COMMITTED 0 MB; MANUAL COLLECTION DISABLED";
        private ulong lastStatusGcCommittedMb = ulong.MaxValue;

        private int pageMode; // 0 kernel, 1 memory/gc, 2 hardware

        public KernelAdvancedApp(int x, int y)
            : base("Panel kernela", "Advanced Kernel - ZOnderqOS", x, y, 960, 620)
        {
            cpuInfo = CpuHardwareInfo.Detect();
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            string pageName = pageMode == 0 ? "KERNEL" : pageMode == 1 ? "MEMORY / GC" : "HARDWARE";
            DrawRow(canvas, 0, IconType.Settings,
                "VIEW", "Click to switch kernel / memory / hardware",
                pageName, Accent);

            if (pageMode == 0)
                RenderKernel(canvas);
            else if (pageMode == 1)
                RenderMemory(canvas);
            else
                RenderHardware(canvas);
        }

        private void RenderKernel(Canvas canvas)
        {
            DrawRow(canvas, 1, IconType.Settings,
                "SCHEDULER", "Cosmos Gen3 scheduler active", schedulerName, Text);
            DrawNumericRow(canvas, 2, IconType.Settings,
                "WATKI", "Scheduler thread count", (ulong)System.Math.Max(0, threadCount), "");
            DrawNumericRow(canvas, 3, IconType.Settings,
                "ONLINE CPU", "CPUs currently managed by the scheduler", cpuCount, "");
            DrawRow(canvas, 4, IconType.Settings,
                "PROCESSOR", cpuInfo.Vendor, cpuInfo.Brand, Text);
            DrawNumericRow(canvas, 5, IconType.Settings,
                "BUSY CPU TIME", "Total non-idle thread execution time", busyCpuNs / 1000000UL, " MS");
            DrawNumericRow(canvas, 6, IconType.Refresh,
                "UPTIME", "Monotonic uptime", uptimeSeconds, " S");
        }

        private void RenderMemory(Canvas canvas)
        {
            DrawNumericRow(canvas, 1, IconType.Settings,
                "RAM CALKOWITY", "Physical RAM (Limine / BIOS)", totalRamMb, " MB");
            DrawNumericRow(canvas, 2, IconType.Settings,
                "FREE RAM", "Free physical pages", freeRamMb, " MB");
            DrawNumericRow(canvas, 3, IconType.Settings,
                "GC HEAP / COMMITTED", "OrionGC heap; committed memory shown in the status description", gcHeapMb, " MB");
            DrawNumericRow(canvas, 4, IconType.Settings,
                "FRAGMENTACJA GC", "Free/fragmented blocks in heap", gcFragmentedKb, " KB");
            DrawNumericRow(canvas, 5, IconType.Settings,
                "PINNED OBJECTS", "Pinned objects reported by OrionGC", gcPinned, "");
            DrawNumericRow(canvas, 6, IconType.Refresh,
                "GC COLLECTIONS", "Automatic collections performed by runtime", gcCollections, "");
        }


        private void RenderHardware(Canvas canvas)
        {
            HardwareSnapshot info = hardwareSnapshot;
            if (info == null)
            {
                info = HardwareSnapshot.Capture();
                hardwareSnapshot = info;
                UpdateHardwareStrings(info);
            }
            DrawRow(canvas, 1, IconType.Settings,
                "ARCHITECTURE", "Platform backend", info.Architecture, Text);
            DrawRow(canvas, 2, IconType.Settings,
                "CPU", info.CpuVendor, info.CpuBrand, Text);
            DrawRow(canvas, 3, IconType.Settings,
                "TOPOLOGIA", "Cores / logical CPUs / threads per core",
                cachedTopologyText, Text);
            DrawRow(canvas, 4, IconType.Settings,
                "CACHE", "L1 / L2 / L3",
                cachedCacheText, Text);
            DrawRow(canvas, 5, IconType.Settings,
                "WIRTUALIZACJA",
                info.HypervisorPresent ? "Hypervisor detected" : "No hypervisor detected",
                info.VirtualizationSupported ? "SUPPORTED" : "N/A", Text);
            DrawRow(canvas, 6, IconType.Settings,
                "FEATURES", "CPU flags reported by platform backend",
                info.CpuFeatures, Text);
        }

        private void UpdateHardwareStrings(HardwareSnapshot info)
        {
            if (info == null)
                return;

            cachedTopologyText = info.PhysicalCores + " / " + info.LogicalProcessors + " / " + info.ThreadsPerCore;
            cachedCacheText = FormatHardwareBytes(info.L1Bytes) + " / " +
                              FormatHardwareBytes(info.L2Bytes) + " / " +
                              FormatHardwareBytes(info.L3Bytes);
        }

        private static string FormatHardwareBytes(ulong bytes)
        {
            const ulong KiB = 1024UL;
            const ulong MiB = 1024UL * KiB;
            if (bytes >= MiB)
                return (bytes / MiB) + " MB";
            if (bytes >= KiB)
                return (bytes / KiB) + " KB";
            return bytes + " B";
        }

        protected override void RefreshData()
        {
            try
            {
                threadCount = SchedulerManager.ThreadCount;
                cpuCount = SchedulerManager.CpuCount;
                schedulerName = SchedulerManager.Current != null && !string.IsNullOrEmpty(SchedulerManager.Current.Name)
                    ? SchedulerManager.Current.Name : "N/A";
                busyCpuNs = SchedulerManager.GetBusyCpuTimeNs();
            }
            catch
            {
                threadCount = 0;
                cpuCount = 0;
                schedulerName = "N/A";
                busyCpuNs = 0;
            }

            try
            {
                ulong physBytes = ZonderqOS.Hardware.PhysicalMemoryDetector.TotalPhysicalBytes;
                totalRamMb = physBytes > 0
                    ? physBytes / 1024UL / 1024UL
                    : PageAllocator.TotalPageCount * PageAllocator.PageSize / 1024UL / 1024UL;
                freeRamMb = PageAllocator.FreePageCount * PageAllocator.PageSize / 1024UL / 1024UL;
            }
            catch
            {
                totalRamMb = 0;
                freeRamMb = 0;
            }

            try
            {
                if (CosmosGc.IsEnabled)
                {
                    gcHeapMb = CosmosGc.GetHeapSizeBytes() / 1024UL / 1024UL;
                    gcCommittedMb = CosmosGc.GetTotalCommittedBytes() / 1024UL / 1024UL;
                    gcFragmentedKb = CosmosGc.GetFragmentedBytes() / 1024UL;
                    gcPinned = (ulong)System.Math.Max(0, CosmosGc.GetPinnedObjectsCount());
                    gcCollections = (ulong)System.Math.Max(0, CosmosGc.GetCollectionIndex());
                }
                else
                {
                    gcHeapMb = gcCommittedMb = gcFragmentedKb = gcPinned = gcCollections = 0;
                }
            }
            catch
            {
                gcHeapMb = gcCommittedMb = gcFragmentedKb = gcPinned = gcCollections = 0;
            }

            try
            {
                long frequency = Stopwatch.Frequency;
                long now = Stopwatch.GetTimestamp();
                uptimeSeconds = frequency > 0 && now > 0 ? (ulong)now / (ulong)frequency : 0;
            }
            catch
            {
                uptimeSeconds = 0;
            }

            if (hardwareSnapshot == null)
            {
                hardwareSnapshot = HardwareSnapshot.Capture();
                UpdateHardwareStrings(hardwareSnapshot);
            }

            if (pageMode == 0)
                SetStatus("KERNEL: READ-ONLY - NO SCHEDULER CHANGES", Good);
            else if (pageMode == 1)
            {
                if (lastStatusGcCommittedMb != gcCommittedMb)
                {
                    lastStatusGcCommittedMb = gcCommittedMb;
                    cachedMemoryStatus = "ORIONGC: COMMITTED " + gcCommittedMb + " MB; MANUAL COLLECTION DISABLED";
                }
                SetStatus(cachedMemoryStatus, Warning);
            }
            else
                SetStatus("HARDWARE: READ-ONLY SNAPSHOT", Good);
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 0)
            {
                pageMode = (pageMode + 1) % 3;
                RefreshData();
                return;
            }

            if (row >= 1 && row <= 6)
            {
                RefreshData();
                SetStatus("KERNEL SNAPSHOT REFRESHED", Good);
            }
        }
    }
}
