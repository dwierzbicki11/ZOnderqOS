using System;
using System.Diagnostics;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

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

        private int pageMode; // 0 kernel, 1 memory/gc

        public KernelAdvancedApp(int x, int y)
            : base("Panel kernela", "Zaawansowany kernel - ZOnderqOS", x, y, 960, 620)
        {
            cpuInfo = CpuHardwareInfo.Detect();
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            DrawRow(canvas, 0, IconType.Settings,
                "WIDOK", "Kliknij aby przelaczyc kernel / pamiec i OrionGC",
                pageMode == 0 ? "KERNEL" : "PAMIEC / GC", Accent);

            if (pageMode == 0)
                RenderKernel(canvas);
            else
                RenderMemory(canvas);
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
                    gcCollections = CosmosGc.GetCollectionIndex();
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

            SetStatus(pageMode == 0
                ? "KERNEL: ODCZYT TYLKO - BRAK RYZYKOWNYCH ZMIAN SCHEDULERA"
                : "ORIONGC: COMMITTED " + gcCommittedMb + " MB; RECZNE COLLECT ZABLOKOWANE",
                pageMode == 0 ? Good : Warning);
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            int row = HitRow(mouseX, mouseY, 7);
            if (row == 0)
            {
                pageMode = pageMode == 0 ? 1 : 0;
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
