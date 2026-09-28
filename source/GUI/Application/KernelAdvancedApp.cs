using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using ZonderqOS.Platform;

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

        private int pageMode; // 0 kernel, 1 memory/gc, 2 hardware

        public KernelAdvancedApp(int x, int y)
            : base("Panel kernela", "Zaawansowany kernel - ZOnderqOS", x, y, 960, 620)
        {
            cpuInfo = CpuHardwareInfo.Detect();
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            string pageName = pageMode == 0 ? "KERNEL" : pageMode == 1 ? "PAMIEC / GC" : "HARDWARE";
            DrawRow(canvas, 0, IconType.Settings,
                "WIDOK", "Kliknij aby przelaczyc kernel / pamiec / hardware",
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
                "SCHEDULER", "Aktywny scheduler Cosmos Gen3", schedulerName, Text);
            DrawNumericRow(canvas, 2, IconType.Settings,
                "WATKI", "Liczba watkow schedulera", (ulong)System.Math.Max(0, threadCount), "");
            DrawNumericRow(canvas, 3, IconType.Settings,
                "ONLINE CPU", "CPU obslugiwane obecnie przez scheduler", cpuCount, "");
            DrawRow(canvas, 4, IconType.Settings,
                "PROCESOR", cpuInfo.Vendor, cpuInfo.Brand, Text);
            DrawNumericRow(canvas, 5, IconType.Settings,
                "BUSY CPU TIME", "Skumulowany czas pracy watkow nie-idle", busyCpuNs / 1000000UL, " MS");
            DrawNumericRow(canvas, 6, IconType.Refresh,
                "UPTIME", "Monotoniczny czas od startu", uptimeSeconds, " S");
        }

        private void RenderMemory(Canvas canvas)
        {
            DrawNumericRow(canvas, 1, IconType.Settings,
                "RAM CALKOWITY", "PageAllocator", totalRamMb, " MB");
            DrawNumericRow(canvas, 2, IconType.Settings,
                "RAM WOLNY", "Wolne strony fizyczne", freeRamMb, " MB");
            DrawNumericRow(canvas, 3, IconType.Settings,
                "GC HEAP / COMMITTED", "Heap OrionGC; committed pokazane w opisie statusu", gcHeapMb, " MB");
            DrawNumericRow(canvas, 4, IconType.Settings,
                "FRAGMENTACJA GC", "Wolne/fragmentowane bloki wewnatrz sterty", gcFragmentedKb, " KB");
            DrawNumericRow(canvas, 5, IconType.Settings,
                "PINNED OBJECTS", "Obiekty przypiete raportowane przez OrionGC", gcPinned, "");
            DrawNumericRow(canvas, 6, IconType.Refresh,
                "KOLEKCJE GC", "Automatyczne kolekcje wykonane przez runtime", gcCollections, "");
        }


        private void RenderHardware(Canvas canvas)
        {
            HardwareSnapshot info = hardwareSnapshot ?? HardwareSnapshot.Capture();
            DrawRow(canvas, 1, IconType.Settings,
                "ARCHITEKTURA", "Backend platformy", info.Architecture, Text);
            DrawRow(canvas, 2, IconType.Settings,
                "CPU", info.CpuVendor, info.CpuBrand, Text);
            DrawRow(canvas, 3, IconType.Settings,
                "TOPOLOGIA", "Rdzenie / logiczne / watki na rdzen",
                info.PhysicalCores + " / " + info.LogicalProcessors + " / " + info.ThreadsPerCore, Text);
            DrawRow(canvas, 4, IconType.Settings,
                "CACHE", "L1 / L2 / L3",
                FormatHardwareBytes(info.L1Bytes) + " / " +
                FormatHardwareBytes(info.L2Bytes) + " / " +
                FormatHardwareBytes(info.L3Bytes), Text);
            DrawRow(canvas, 5, IconType.Settings,
                "WIRTUALIZACJA",
                info.HypervisorPresent ? "Wykryto hypervisor" : "Brak wykrytego hypervisora",
                info.VirtualizationSupported ? "SUPPORTED" : "N/A", Text);
            DrawRow(canvas, 6, IconType.Settings,
                "FEATURES", "Flagi CPU raportowane przez backend platformy",
                info.CpuFeatures, Text);
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
                totalRamMb = PageAllocator.TotalPageCount * PageAllocator.PageSize / 1024UL / 1024UL;
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

            hardwareSnapshot = HardwareSnapshot.Capture();

            if (pageMode == 0)
                SetStatus("KERNEL: ODCZYT TYLKO - BRAK RYZYKOWNYCH ZMIAN SCHEDULERA", Good);
            else if (pageMode == 1)
                SetStatus("ORIONGC: COMMITTED " + gcCommittedMb + " MB; RECZNE COLLECT ZABLOKOWANE", Warning);
            else
                SetStatus("HARDWARE: SNAPSHOT TYLKO DO ODCZYTU", Good);
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
                SetStatus("ODSWIEZONO SNAPSHOT KERNELA", Good);
            }
        }
    }
}
