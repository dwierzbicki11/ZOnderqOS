using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using ZonderqOS.Platform;
using ZonderqOS.Hardware;

namespace ZonderqOS.GUI.Apps
{
    public sealed class TaskManagerModernApp : Application
    {
        internal const int PageProcesses = 0;
        internal const int PagePerformance = 1;
        internal const int PageDetails = 2;
        internal const int PageServices = 3;

        internal const int PerfCpu = 0;
        internal const int PerfMemory = 1;
        internal const int PerfSystem = 2;

        internal const int CpuGraphTotal = 0;
        internal const int CpuGraphLogical = 1;

        private const int HistoryLength = 120;
        private const int LogicalPerPage = 16;
        private const int InitialRowPool = 12;
        private const int MaxRecycledRows = 64;

        private sealed class ReusableScratch
        {
            public readonly int[] CpuHistory = new int[HistoryLength];
            public readonly int[] MemoryHistory = new int[HistoryLength];
            public readonly List<ModernTaskRow> RecycledRows = new List<ModernTaskRow>(InitialRowPool);
            public readonly TaskManagerMemoryCache MemoryCache = new TaskManagerMemoryCache();
            public int[][] LogicalHistory = new int[0][];
            public int[] LogicalHistoryCount = new int[0];
            public int[] LogicalHistoryWrite = new int[0];
            public int[] LogicalUsage = new int[0];
            public ulong[] LogicalBusyDelta = new ulong[0];
            public int LogicalCapacity;
            public uint[] PreviousThreadIds = new uint[0];
            public ulong[] PreviousThreadRuntime = new ulong[0];
            public bool[] PreviousThreadValid = new bool[0];
            public int ThreadSlotCapacity;
        }

        private static ReusableScratch sharedScratch;
        private static bool sharedScratchInUse;
        private static CpuHardwareInfo cachedCpuStringsFor;
        private static string cachedStaticBaseSpeedText;
        private static string cachedStaticMaxSpeedText;
        private static string cachedStaticBaseMaxSpeedText;
        private static string cachedStaticTopologyText;
        private static string cachedStaticCacheText;
        private static string cachedStaticSignatureText;

        private readonly ApplicationManager applicationManager;
        private readonly Action closeCallback;
        private readonly TaskManagerModernView view;
        private readonly CpuHardwareInfo cpuInfo;
        private readonly bool ownsSharedScratch;
        private bool releasedSharedScratch;

        private readonly List<KernelProcess> processSnapshot = new List<KernelProcess>(16);
        private readonly List<ModernTaskRow> rowPool = new List<ModernTaskRow>(InitialRowPool);
        private readonly List<ModernTaskRow> allRows = new List<ModernTaskRow>(InitialRowPool);
        private readonly List<ModernTaskRow> visibleRows = new List<ModernTaskRow>(InitialRowPool);

        private readonly int[] cpuHistory;
        private readonly int[] memoryHistory;
        private int cpuHistoryCount;
        private int cpuHistoryWrite;
        private int memoryHistoryCount;
        private int memoryHistoryWrite;

        private int[][] logicalHistory = new int[0][];
        private int[] logicalHistoryCount = new int[0];
        private int[] logicalHistoryWrite = new int[0];
        private int[] logicalUsage = new int[0];
        private ulong[] logicalBusyDelta = new ulong[0];
        private int logicalCapacity;

        private uint[] previousThreadIds = new uint[0];
        private ulong[] previousThreadRuntime = new ulong[0];
        private bool[] previousThreadValid = new bool[0];
        private int threadSlotCapacity;
        private bool hadThreadSlotScan;

        private int activePage = PageProcesses;
        private int performanceResource = PerfCpu;
        private int cpuGraphMode = CpuGraphTotal;
        private int logicalPage;
        private int selectedIndex = -1;
        private int scrollIndex;
        private int previousVisibleRows = -1;
        private long lastRefreshTicks = -1;
        private bool samplingPaused;
        private string status = "Gotowy";

        private int guiAppCount;
        private int kernelProcessCount;
        private int cpuUsagePercent;
        private ulong totalPages;
        private ulong freePages;
        private ulong usedMemoryPercent;
        private ulong gcHeapBytes;
        private ulong gcCommittedBytes;
        private ulong physicalInstalledBytes;
        private ulong physicalUsableBytes;
        private ulong previousUsedPagesSnapshot;
        private bool hasPreviousMemorySnapshot;
        private string cachedMemoryDetailText = "0.00 MB (0.00%)";
        private string cachedGcCommittedText = "N/A";
        private string cachedUsableRamText = "0 B";
        private int lastLayoutW = -1;
        private int lastLayoutH = -1;
        private int lastLayoutX = int.MinValue;
        private int lastLayoutY = int.MinValue;
        private int lastLayoutTab = -1;
        private int storageDeviceCount;
        private int storagePartitionCount;
        private bool networkReady;

        private uint onlineCpuCount;
        private int schedulerThreadCount;
        private int runningThreadCount;
        private int readyThreadCount;
        private int blockedThreadCount;
        private int sleepingThreadCount;
        private string schedulerName = "N/A";
        private ulong schedulerTickNs;

        private long lastCpuTimestamp;
        private ulong lastBusyCpuNs;
        private readonly long managerOpenedAt;

        private long clockSecond = -1;
        private long lastFormattedClockSecond = -1;
        private int lastClockDay = -1;
        private int clockHour;
        private int clockMinute;
        private int clockSecondInt;
        private string clockText = "--:--:--";
        private string dateText = "--.--.----";

        private ulong rawGcHeapPages;
        private ulong rawRatHeapPages;
        private ulong rawSysPages;
        private ulong rawGfxBufferBytes;
        private long rawDeltaPages;
        private ulong rawGcFragmentedBytes;
        private int rawGcCollectionIndex;
        private int rawGcObjectsFreed;
        private bool rawGcEnabled;

        private readonly TaskManagerMemoryCache memoryCache;
        private MemoryOptimizerSnapshot optimizerSnapshot;
        private readonly string baseMaxSpeedText;

        private readonly string baseSpeedText;
        private readonly string maxSpeedText;
        private readonly string topologyText;
        private readonly string cacheText;
        private readonly string signatureText;

        public TaskManagerModernApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Manager zadan")
        {
            applicationManager = manager;
            closeCallback = onClose;
            cpuInfo = CpuHardwareInfo.Detect();
            managerOpenedAt = Stopwatch.GetTimestamp();

            if (!sharedScratchInUse)
            {
                if (sharedScratch == null)
                    sharedScratch = new ReusableScratch();
                sharedScratchInUse = true;
                ownsSharedScratch = true;
                cpuHistory = sharedScratch.CpuHistory;
                memoryHistory = sharedScratch.MemoryHistory;
                Array.Clear(cpuHistory, 0, cpuHistory.Length);
                Array.Clear(memoryHistory, 0, memoryHistory.Length);
                logicalHistory = sharedScratch.LogicalHistory;
                logicalHistoryCount = sharedScratch.LogicalHistoryCount;
                logicalHistoryWrite = sharedScratch.LogicalHistoryWrite;
                logicalUsage = sharedScratch.LogicalUsage;
                logicalBusyDelta = sharedScratch.LogicalBusyDelta;
                logicalCapacity = sharedScratch.LogicalCapacity;
                Array.Clear(logicalHistoryCount, 0, logicalHistoryCount.Length);
                Array.Clear(logicalHistoryWrite, 0, logicalHistoryWrite.Length);
                Array.Clear(logicalUsage, 0, logicalUsage.Length);
                previousThreadIds = sharedScratch.PreviousThreadIds;
                previousThreadRuntime = sharedScratch.PreviousThreadRuntime;
                previousThreadValid = sharedScratch.PreviousThreadValid;
                threadSlotCapacity = sharedScratch.ThreadSlotCapacity;
                Array.Clear(previousThreadValid, 0, previousThreadValid.Length);
                memoryCache = sharedScratch.MemoryCache;
            }
            else
            {
                cpuHistory = new int[HistoryLength];
                memoryHistory = new int[HistoryLength];
                memoryCache = new TaskManagerMemoryCache();
            }

            if (object.ReferenceEquals(cpuInfo, cachedCpuStringsFor))
            {
                baseSpeedText = cachedStaticBaseSpeedText;
                maxSpeedText = cachedStaticMaxSpeedText;
                baseMaxSpeedText = cachedStaticBaseMaxSpeedText;
                topologyText = cachedStaticTopologyText;
                cacheText = cachedStaticCacheText;
                signatureText = cachedStaticSignatureText;
            }
            else
            {
                baseSpeedText = FormatSpeed(cpuInfo.BaseMHz);
                maxSpeedText = FormatSpeed(cpuInfo.MaxMHz);
                baseMaxSpeedText = baseSpeedText + " / " + maxSpeedText;
                topologyText = Math.Max(1, cpuInfo.PhysicalCores) + " / " +
                               Math.Max(1, cpuInfo.LogicalProcessors) + " / " +
                               Math.Max(1, cpuInfo.ThreadsPerCore);
                cacheText = FormatBytes(cpuInfo.L1Bytes) + " / " + FormatBytes(cpuInfo.L2Bytes) + " / " + FormatBytes(cpuInfo.L3Bytes);
                signatureText = cpuInfo.Family + " / " + cpuInfo.Model + " / " + cpuInfo.Stepping;

                cachedCpuStringsFor = cpuInfo;
                cachedStaticBaseSpeedText = baseSpeedText;
                cachedStaticMaxSpeedText = maxSpeedText;
                cachedStaticBaseMaxSpeedText = baseMaxSpeedText;
                cachedStaticTopologyText = topologyText;
                cachedStaticCacheText = cacheText;
                cachedStaticSignatureText = signatureText;
            }

            if (ownsSharedScratch && sharedScratch.RecycledRows.Count > 0)
            {
                int take = Math.Min(InitialRowPool, sharedScratch.RecycledRows.Count);
                for (int i = 0; i < take; i++)
                {
                    int last = sharedScratch.RecycledRows.Count - 1;
                    rowPool.Add(sharedScratch.RecycledRows[last]);
                    sharedScratch.RecycledRows.RemoveAt(last);
                }
            }
            while (rowPool.Count < InitialRowPool)
                rowPool.Add(new ModernTaskRow());

            EnsureLogicalCapacity(Math.Max(1, cpuInfo.LogicalProcessors));
            UpdateClock();

            Window = new Window(x, y, 1040, 690, "Manager zadan");
            Window.CloseAction = Close;
            view = new TaskManagerModernView(10, 40, 1020, 635, this);
            Window.AddChild(view);

            UpdateLayout();
            RefreshSnapshot(forceFullMemory: true);
        }

        public override void Update()
        {
            // ApplicationManager updates minimized apps too. Never poll hidden
            // telemetry. On the first hidden update invalidate the CPU delta
            // baseline so a long pause cannot dilute the next utilization sample.
            if (!Window.Visible || Window.IsMinimized)
            {
                if (!samplingPaused)
                {
                    lastCpuTimestamp = 0;
                    Array.Clear(previousThreadValid, 0, previousThreadValid.Length);
                    samplingPaused = true;
                }
                lastRefreshTicks = -1;
                return;
            }

            samplingPaused = false;
            UpdateLayout();
            UpdateClock();
            // The old 30-frame interval varied from 0.3s to 15s depending
            // on GUI FPS. Snapshot cadence now uses monotonic elapsed time.
            if (TaskManagerRefreshGate.ShouldRefresh(
                Stopwatch.GetTimestamp(), lastRefreshTicks, Stopwatch.Frequency))
                RefreshSnapshot();
        }

        private void UpdateLayout()
        {
            if (Window.X == lastLayoutX && Window.Y == lastLayoutY &&
                Window.Width == lastLayoutW && Window.Height == lastLayoutH &&
                activePage == lastLayoutTab)
                return;

            lastLayoutX = Window.X;
            lastLayoutY = Window.Y;
            lastLayoutW = Window.Width;
            lastLayoutH = Window.Height;
            lastLayoutTab = activePage;

            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(1, Window.Width - 20);
            view.Height = Math.Max(1, Window.Height - 50);
            int rows = view.VisibleRows;
            // Preserve a selected row that was actually on screen before
            // shrinking the window. Do not snap back to selections that the
            // user intentionally scrolled away from.
            bool selectionWasVisible = TaskManagerSelectionPolicy.IsVisible(
                selectedIndex, scrollIndex, previousVisibleRows, visibleRows.Count);
            if (rows > 0 && view.HasPaintedProcessRows &&
                rows != previousVisibleRows && selectionWasVisible)
                EnsureSelectionVisible();
            else
                ClampScroll();
            previousVisibleRows = rows;
            ClampLogicalPage();
        }

        private static readonly string[] Digits2 = PrecomputeDigits2();
        private static string[] PrecomputeDigits2()
        {
            var arr = new string[100];
            for (int i = 0; i < 100; i++)
                arr[i] = i < 10 ? "0" + i : i.ToString();
            return arr;
        }

        private static string D2(int value)
        {
            if ((uint)value < 100)
                return Digits2[value];
            return value.ToString();
        }

        private static string D4(int value)
        {
            value = Math.Max(0, value);
            if (value >= 2000 && value < 2100)
                return "20" + D2(value - 2000);
            if (value < 10) return "000" + D2(value);
            if (value < 100) return "00" + D2(value);
            if (value < 1000) return "0" + value;
            return value.ToString();
        }

        private void UpdateClock()
        {
            try
            {
                DateTime now = DateTime.Now;
                long key = now.Ticks / TimeSpan.TicksPerSecond;
                if (key == clockSecond)
                    return;
                clockSecond = key;
                clockHour = now.Hour;
                clockMinute = now.Minute;
                clockSecondInt = now.Second;
                if (now.Day != lastClockDay)
                {
                    lastClockDay = now.Day;
                    dateText = D2(now.Day) + "." + D2(now.Month) + "." + D4(now.Year);
                }
            }
            catch
            {
                clockHour = 0;
                clockMinute = 0;
                clockSecondInt = 0;
                dateText = "--.--.----";
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            if (!Window.Visible || Window.IsMinimized || !IsRunning)
                return;
            // A move, resize or display-mode change may occur in the same
            // input frame, before Update(). Hit-test against the new client
            // coordinates rather than the previous frame's row positions.
            if (view.X != Window.X + 10 || view.Y != Window.Y + 40 ||
                view.Width != Math.Max(1, Window.Width - 20) ||
                view.Height != Math.Max(1, Window.Height - 50))
                UpdateLayout();
            if (view.HandleScrollMouse(mouseX, mouseY, left, oldLeft))
                return;
            if (!left || oldLeft)
                return;

            int x = mouseX - view.X;
            int y = mouseY - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)
                return;

            int page = view.NavAt(x, y);
            if (page >= 0)
            {
                SetPage(page);
                return;
            }

            int toolbar = view.ToolbarActionAt(x, y);
            if (toolbar == 1)
            {
                RefreshSnapshot();
                status = "Odswiezono";
                return;
            }
            if (toolbar == 2)
            {
                EndSelectedTask();
                return;
            }

            if (activePage == PagePerformance)
            {
                int resource = view.PerformanceResourceAt(x, y);
                if (resource >= 0)
                {
                    if (performanceResource != resource)
                    {
                        performanceResource = resource;
                        OnTelemetrySelectionChanged();
                    }
                    return;
                }

                if (performanceResource == PerfCpu)
                {
                    int graph = view.CpuGraphModeAt(x, y);
                    if (graph >= 0)
                    {
                        if (cpuGraphMode != graph)
                        {
                            cpuGraphMode = graph;
                            OnTelemetrySelectionChanged();
                        }
                        status = graph == CpuGraphTotal ? "CPU: lacznie" : "CPU: logiczne procesory";
                        return;
                    }

                    int delta = view.CpuLogicalPageActionAt(x, y);
                    if (delta != 0)
                    {
                        logicalPage += delta;
                        ClampLogicalPage();
                        return;
                    }
                }
                return;
            }

            int row = view.RowAt(x, y);
            if (row >= 0 && row < visibleRows.Count)
            {
                selectedIndex = row;
                EnsureSelectionVisible();
                status = visibleRows[row].Name;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            // Keyboard can arrive before Update after a resolution switch.
            // Refresh the window and visible-row geometry before PageUp/Down,
            // selection and the potentially destructive Delete action.
            Window.EnsureDesktopBounds();
            UpdateLayout();
            // A modified key belongs to the desktop/global shortcut layer.
            // In particular Ctrl+Alt+Delete must never end the selected task.
            if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
                return;
            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }
            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshSnapshot();
                status = "Odswiezono";
                return;
            }
            // The left/right arrows switch Task Manager pages without a mouse.
            // Only plain arrows are accepted; modified keys stay reserved for
            // global desktop shortcuts and never trigger process actions.
            if (key.Modifiers == 0 && key.Key == ConsoleKeyEx.LeftArrow)
            {
                SetPage(activePage == PageProcesses ? PageServices : activePage - 1);
                return;
            }
            if (key.Modifiers == 0 && key.Key == ConsoleKeyEx.RightArrow)
            {
                SetPage(activePage == PageServices ? PageProcesses : activePage + 1);
                return;
            }
            if (activePage == PagePerformance)
            {
                // The performance dashboard used to be mouse-only. Keep
                // global Ctrl/Alt and modified keys out of this navigation.
                if (key.Modifiers != 0)
                    return;

                if (key.Key == ConsoleKeyEx.UpArrow || key.Key == ConsoleKeyEx.DownArrow)
                {
                    int nextResource = TaskManagerPerformanceNavigation.StepResource(
                        performanceResource, key.Key == ConsoleKeyEx.UpArrow ? -1 : 1);
                    if (nextResource != performanceResource)
                    {
                        performanceResource = nextResource;
                        OnTelemetrySelectionChanged();
                    }
                    status = performanceResource == PerfCpu ? "CPU" :
                        performanceResource == PerfMemory ? "Pamiec" : "System";
                    return;
                }

                if (performanceResource != PerfCpu)
                    return;

                // G switches the total/logical CPU graphs. PageUp/PageDown
                // and Home/End navigate logical CPU pages without a mouse.
                if (key.KeyChar == 'g' || key.KeyChar == 'G')
                {
                    cpuGraphMode = cpuGraphMode == CpuGraphTotal ? CpuGraphLogical : CpuGraphTotal;
                    OnTelemetrySelectionChanged();
                    status = cpuGraphMode == CpuGraphTotal ? "CPU: lacznie" : "CPU: logiczne procesory";
                    return;
                }

                if (cpuGraphMode != CpuGraphLogical)
                    return;
                if (key.Key == ConsoleKeyEx.PageUp)
                    logicalPage = TaskManagerPerformanceNavigation.ClampLogicalPage(
                        logicalPage - 1, LogicalPageCount);
                else if (key.Key == ConsoleKeyEx.PageDown)
                    logicalPage = TaskManagerPerformanceNavigation.ClampLogicalPage(
                        logicalPage + 1, LogicalPageCount);
                else if (key.Key == ConsoleKeyEx.Home)
                    logicalPage = 0;
                else if (key.Key == ConsoleKeyEx.End)
                    logicalPage = TaskManagerPerformanceNavigation.ClampLogicalPage(
                        int.MaxValue, LogicalPageCount);
                return;
            }
            if (key.Key == ConsoleKeyEx.UpArrow) Select(-1);
            else if (key.Key == ConsoleKeyEx.DownArrow) Select(1);
            else if (key.Key == ConsoleKeyEx.PageUp) Select(-view.VisibleRows);
            else if (key.Key == ConsoleKeyEx.PageDown) Select(view.VisibleRows);
            else if (key.Key == ConsoleKeyEx.Home) SelectAbsolute(0);
            else if (key.Key == ConsoleKeyEx.End) SelectAbsolute(visibleRows.Count - 1);
            else if (key.Key == ConsoleKeyEx.Delete && key.Modifiers == 0) EndSelectedTask();
        }

        private void SetPage(int page)
        {
            if (page < PageProcesses || page > PageServices || page == activePage)
                return;
            activePage = page;
            selectedIndex = -1;
            scrollIndex = 0;
            if (activePage != PagePerformance)
            {
                BuildRows();
            }
            RebuildVisibleRows(null, -1, false);
            OnTelemetrySelectionChanged();
            status = page == PageProcesses ? "Procesy" : page == PagePerformance ? "Wydajnosc" : page == PageDetails ? "Szczegoly" : "Uslugi";
        }

        private void OnTelemetrySelectionChanged()
        {
            if (activePage != PagePerformance)
                return;
            UpdateMemorySnapshot(performanceResource == PerfMemory);
            if (performanceResource == PerfMemory)
                optimizerSnapshot = MemoryOptimizer.GetSnapshot();
            if (performanceResource == PerfSystem || (performanceResource == PerfCpu && cpuGraphMode == CpuGraphLogical))
                UpdateCpuSnapshot();
            if (performanceResource == PerfSystem)
                UpdateSystemSnapshot();
        }

        private void Select(int delta)
        {
            if (visibleRows.Count == 0)
                return;
            SelectAbsolute(selectedIndex < 0
                ? (delta >= 0 ? 0 : visibleRows.Count - 1)
                : selectedIndex + delta);
        }

        private void SelectAbsolute(int index)
        {
            if (visibleRows.Count == 0 || !view.HasPaintedProcessRows)
                return;
            selectedIndex = Math.Max(0, Math.Min(visibleRows.Count - 1, index));
            EnsureSelectionVisible();
            status = visibleRows[selectedIndex].Name;
        }

        // A selected task may be scrolled out of view. Never terminate an
        // invisible row: the user must see what the Delete/END TASK action targets.
        internal bool CanEndSelectedTask => activePage != PagePerformance &&
            view.HasPaintedProcessRows &&
            TaskManagerSelectionPolicy.IsVisible(selectedIndex, scrollIndex,
                view.VisibleRows, visibleRows.Count) &&
            visibleRows[selectedIndex].CanEnd && visibleRows[selectedIndex].GuiApplication != this;

        private void EndSelectedTask()
        {
            if (activePage == PagePerformance || !view.HasPaintedProcessRows ||
                !TaskManagerSelectionPolicy.IsVisible(selectedIndex, scrollIndex,
                    view.VisibleRows, visibleRows.Count))
            {
                status = "Wybierz widoczne zadanie";
                return;
            }

            ModernTaskRow row = visibleRows[selectedIndex];
            if (!row.CanEnd)
            {
                status = "Zadanie chronione";
                return;
            }

            if (row.GuiApplication != null)
            {
                if (row.GuiApplication == this)
                {
                    status = "Manager zamknij przyciskiem X";
                    return;
                }
                if (row.GuiApplication.Window?.CloseAction != null)
                    row.GuiApplication.Window.CloseAction();
                else if (applicationManager != null)
                    applicationManager.Close(row.GuiApplication);
                else
                    row.GuiApplication.Close();
                status = "Zakonczono aplikacje";
                RefreshSnapshot();
                return;
            }

            if (row.KernelPid > 0)
            {
                bool ok = ProcessManager.Kill(row.KernelPid);
                status = ok ? "Wyslano zatrzymanie" : "Nie mozna zatrzymac";
                RefreshSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            RefreshSnapshot(false);
        }

        private void RefreshSnapshot(bool forceFullMemory)
        {
            if (activePage != PagePerformance)
            {
                // A user may scroll away from the selected task while the 500ms
                // telemetry timer keeps refreshing. Do not jump back to that old
                // selection on every sample; keep it visible only if it was visible
                // immediately before the refresh.
                bool selectedWasVisible = view.HasPaintedProcessRows &&
                    TaskManagerSelectionPolicy.IsVisible(selectedIndex, scrollIndex,
                        view.VisibleRows, visibleRows.Count);
                Application selectedApp = null;
                int selectedPid = -1;
                if (selectedIndex >= 0 && selectedIndex < visibleRows.Count)
                {
                    selectedApp = visibleRows[selectedIndex].GuiApplication;
                    selectedPid = visibleRows[selectedIndex].KernelPid;
                }

                BuildRows();
                RebuildVisibleRows(selectedApp, selectedPid, selectedWasVisible);
            }

            UpdateMemorySnapshot(forceFullMemory);
            UpdateCpuSnapshot();
            if (activePage == PagePerformance && performanceResource == PerfSystem)
                UpdateSystemSnapshot();
            if (forceFullMemory || (activePage == PagePerformance && performanceResource == PerfMemory))
                optimizerSnapshot = MemoryOptimizer.GetSnapshot();
            // Start the interval after expensive sampling completes, including
            // F5 and toolbar refreshes, rather than immediately sampling again.
            lastRefreshTicks = Stopwatch.GetTimestamp();
        }

        private ModernTaskRow AcquireRow(int index)
        {
            while (index >= rowPool.Count)
                rowPool.Add(new ModernTaskRow());
            return rowPool[index];
        }

        private void BuildRows()
        {
            allRows.Clear();
            guiAppCount = 0;
            int slot = 0;
            Application active = applicationManager != null ? applicationManager.ActiveApplication : null;

            if (applicationManager != null)
            {
                List<Application> apps = applicationManager.Applications;
                for (int i = 0; i < apps.Count; i++)
                {
                    Application app = apps[i];
                    if (app == null || !app.IsRunning || app.Window == null)
                        continue;
                    ModernTaskRow row = AcquireRow(slot++);
                    string state = app.Window.IsMinimized ? "MINIMIZED" : active == app ? "ACTIVE" : "RUNNING";
                    row.SetApplication(app, state, app != this);
                    allRows.Add(row);
                    guiAppCount++;
                }
            }

            kernelProcessCount = ProcessManager.FillActiveProcesses(processSnapshot);
            for (int i = 0; i < processSnapshot.Count; i++)
            {
                KernelProcess process = processSnapshot[i];
                if (process == null)
                    continue;
                bool protectedProcess = string.Equals(process.Name, "sys_guardian", StringComparison.OrdinalIgnoreCase);
                ModernTaskRow row = AcquireRow(slot++);
                row.SetKernel(process, process.IsRunning ? "RUNNING" : "STOPPED", protectedProcess ? "SYSTEM PROTECTED" : "KERNEL PROCESS", !protectedProcess);
                allRows.Add(row);
            }
        }

        private void RebuildVisibleRows(Application selectedApp, int selectedPid, bool keepSelectedVisible)
        {
            visibleRows.Clear();
            if (activePage != PagePerformance)
            {
                for (int i = 0; i < allRows.Count; i++)
                {
                    ModernTaskRow row = allRows[i];
                    if (activePage == PageServices && !row.IsKernel)
                        continue;
                    visibleRows.Add(row);
                }
            }

            selectedIndex = -1;
            for (int i = 0; i < visibleRows.Count; i++)
            {
                ModernTaskRow row = visibleRows[i];
                if ((selectedApp != null && row.GuiApplication == selectedApp) || (selectedPid > 0 && row.KernelPid == selectedPid))
                {
                    selectedIndex = i;
                    break;
                }
            }
            ClampScroll();
            if (keepSelectedVisible)
                EnsureSelectionVisible();
        }

        private void UpdateMemorySnapshot()
        {
            UpdateMemorySnapshot(false);
        }

        private void UpdateMemorySnapshot(bool forceFullMemory)
        {
            bool includeDetail = forceFullMemory || activePage == PagePerformance;
            bool includeFullPoolLabels = forceFullMemory ||
                (activePage == PagePerformance && performanceResource == PerfMemory);
            try
            {
                totalPages = PageAllocator.TotalPageCount;
                freePages = Math.Min(PageAllocator.FreePageCount, totalPages);
                ulong usedPages = totalPages - freePages;
                usedMemoryPercent = totalPages == 0 ? 0 : usedPages * 100UL / totalPages;
                usedMemoryPercent = Math.Min(100UL, usedMemoryPercent);

                long deltaPages = hasPreviousMemorySnapshot ? (long)usedPages - (long)previousUsedPagesSnapshot : 0;
                previousUsedPagesSnapshot = usedPages;
                hasPreviousMemorySnapshot = true;
                rawDeltaPages = deltaPages;

                if (includeFullPoolLabels)
                {
                    gcHeapBytes = 0;
                    gcCommittedBytes = 0;
                    ulong gcFragmentedBytes = 0;
                    int gcCollectionIndex = 0;
                    int gcObjectsFreed = 0;
                    bool gcEnabled = CosmosGc.IsEnabled;
                    if (gcEnabled)
                    {
                        gcHeapBytes = CosmosGc.GetHeapSizeBytes();
                        gcCommittedBytes = CosmosGc.GetTotalCommittedBytes();
                        gcFragmentedBytes = CosmosGc.GetFragmentedBytes();
                        gcCollectionIndex = CosmosGc.GetCollectionIndex();
                        gcObjectsFreed = CosmosGc.GetTotalObjectsFreed();
                    }

                    ulong pEmpty = 0, pGcHeap = 0, pSmall = 0, pMed = 0, pLarge = 0;
                    ulong pUnmanaged = 0, pPageDir = 0, pPageAlloc = 0, pSmt = 0, pExt = 0, pUnknown = 0;
                    try
                    {
                        PageAllocator.GetPageCountsByType(
                            out pEmpty, out pGcHeap, out pSmall, out pMed, out pLarge,
                            out pUnmanaged, out pPageDir, out pPageAlloc, out pSmt, out pExt, out pUnknown);
                    }
                    catch { }
                    rawGcHeapPages = pGcHeap;
                    rawRatHeapPages = pSmall + pMed + pLarge;
                    rawSysPages = pUnmanaged + pPageDir + pPageAlloc + pSmt + pExt + pUnknown;
                    rawGfxBufferBytes = GuiManager.Instance != null ? GuiManager.Instance.EstimatedGraphicsBufferBytes : 0;
                    rawGcFragmentedBytes = gcFragmentedBytes;
                    rawGcCollectionIndex = gcCollectionIndex;
                    rawGcObjectsFreed = gcObjectsFreed;
                    rawGcEnabled = gcEnabled;
                }

                if (physicalInstalledBytes == 0)
                    physicalInstalledBytes = PhysicalMemoryDetector.InstalledBytes;
                if (physicalUsableBytes == 0)
                    physicalUsableBytes = PhysicalMemoryDetector.UsableBytes;

                memoryCache.Update(usedPages, freePages, totalPages,
                                   gcHeapBytes, gcCommittedBytes,
                                   physicalInstalledBytes, physicalUsableBytes,
                                   includeDetail, includeFullPoolLabels);
                cachedMemoryDetailText = memoryCache.MemoryDetailText;
                cachedGcCommittedText = memoryCache.GcCommittedText;
                cachedUsableRamText = memoryCache.UsableRamText;
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
                usedMemoryPercent = 0;
                gcHeapBytes = 0;
                gcCommittedBytes = 0;
                physicalInstalledBytes = 0;
                physicalUsableBytes = 0;
                rawGcHeapPages = 0;
                rawRatHeapPages = 0;
                rawSysPages = 0;
                rawGfxBufferBytes = 0;
                rawDeltaPages = 0;
                rawGcFragmentedBytes = 0;
                rawGcCollectionIndex = 0;
                rawGcObjectsFreed = 0;
                rawGcEnabled = false;
            }
            RecordHistory(memoryHistory, ref memoryHistoryCount, ref memoryHistoryWrite, (int)usedMemoryPercent);
        }

        private void UpdateCpuSnapshot()
        {
            uint cpuCount = 0;
            int slots = 0;
            int totalThreads = 0;
            ulong busyTotal = 0;
            try
            {
                cpuCount = SchedulerInfo.CpuCount;
                slots = SchedulerInfo.ThreadSlotCount;
                totalThreads = SchedulerInfo.ThreadCount;
                busyTotal = SchedulerInfo.BusyCpuTimeNs;
                if (schedulerName == null || schedulerName == "N/A")
                {
                    string sName = SchedulerInfo.SchedulerName;
                    schedulerName = string.IsNullOrEmpty(sName) ? "N/A" : sName;
                }
                schedulerTickNs = SchedulerInfo.TickPeriodNs;
            }
            catch
            {
                schedulerName = "N/A";
                schedulerTickNs = 0;
            }

            onlineCpuCount = cpuCount;
            bool needThreadSlots = activePage == PagePerformance &&
                (performanceResource == PerfSystem ||
                 (performanceResource == PerfCpu && cpuGraphMode == CpuGraphLogical));
            if (needThreadSlots)
                EnsureLogicalCapacity(Math.Max(1, Math.Max(cpuInfo.LogicalProcessors, (int)cpuCount)));

            long now = Stopwatch.GetTimestamp();
            long elapsedTicks = lastCpuTimestamp == 0 ? 0 : now - lastCpuTimestamp;
            ulong elapsedNs = elapsedTicks > 0 && Stopwatch.Frequency > 0
                ? TicksToNanoseconds((ulong)elapsedTicks, (ulong)Stopwatch.Frequency)
                : 0;
            bool validWindow = lastCpuTimestamp != 0 && elapsedNs >= 1_000_000UL;

            if (needThreadSlots)
            {
                EnsureThreadSlotCapacity(Math.Max(0, slots));
                schedulerThreadCount = 0;
                runningThreadCount = 0;
                readyThreadCount = 0;
                blockedThreadCount = 0;
                sleepingThreadCount = 0;
                Array.Clear(logicalBusyDelta, 0, logicalBusyDelta.Length);

                bool threadWindow = validWindow && hadThreadSlotScan;
                for (int slot = 0; slot < slots; slot++)
                {
                    KernelThreadInfo info;
                    bool present;
                    try { present = SchedulerInfo.TryGetThreadInSlot(slot, out info); }
                    catch { info = default; present = false; }

                    if (!present)
                    {
                        if (slot < previousThreadValid.Length) previousThreadValid[slot] = false;
                        continue;
                    }

                    schedulerThreadCount++;
                    if (info.State == KernelThreadState.Running) runningThreadCount++;
                    else if (info.State == KernelThreadState.Ready) readyThreadCount++;
                    else if (info.State == KernelThreadState.Blocked) blockedThreadCount++;
                    else if (info.State == KernelThreadState.Sleeping) sleepingThreadCount++;

                    ulong delta = 0;
                    if (threadWindow && previousThreadValid[slot] && previousThreadIds[slot] == info.Id && info.TotalRuntimeNs >= previousThreadRuntime[slot])
                        delta = info.TotalRuntimeNs - previousThreadRuntime[slot];

                    if (!info.IsIdle && info.CpuId < (uint)logicalBusyDelta.Length)
                        logicalBusyDelta[info.CpuId] += delta;

                    previousThreadIds[slot] = info.Id;
                    previousThreadRuntime[slot] = info.TotalRuntimeNs;
                    previousThreadValid[slot] = true;
                }
                hadThreadSlotScan = true;
            }
            else
            {
                schedulerThreadCount = totalThreads;
                if (hadThreadSlotScan)
                {
                    Array.Clear(previousThreadValid, 0, previousThreadValid.Length);
                    hadThreadSlotScan = false;
                }
            }

            if (!validWindow)
            {
                cpuUsagePercent = 0;
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, 0);
                if (needThreadSlots)
                {
                    for (int cpu = 0; cpu < logicalCapacity; cpu++)
                    {
                        logicalUsage[cpu] = 0;
                        RecordHistory(logicalHistory[cpu], ref logicalHistoryCount[cpu], ref logicalHistoryWrite[cpu], 0);
                    }
                }
            }
            else
            {
                ulong capacity = elapsedNs * Math.Max(1UL, (ulong)cpuCount);
                ulong busyDelta = busyTotal >= lastBusyCpuNs ? busyTotal - lastBusyCpuNs : 0;
                cpuUsagePercent = capacity == 0 ? 0 : (int)Math.Min(100UL, busyDelta * 100UL / capacity);
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, cpuUsagePercent);

                if (needThreadSlots)
                {
                    for (int cpu = 0; cpu < logicalCapacity; cpu++)
                    {
                        int usage = (uint)cpu < cpuCount && elapsedNs > 0
                            ? (int)Math.Min(100UL, logicalBusyDelta[cpu] * 100UL / elapsedNs)
                            : 0;
                        logicalUsage[cpu] = usage;
                        RecordHistory(logicalHistory[cpu], ref logicalHistoryCount[cpu], ref logicalHistoryWrite[cpu], usage);
                    }
                }
            }

            lastCpuTimestamp = now;
            lastBusyCpuNs = busyTotal;
            ClampLogicalPage();
        }

        private void EnsureLogicalCapacity(int required)
        {
            required = Math.Max(1, Math.Min(256, required));
            if (required <= logicalCapacity)
                return;
            int old = logicalCapacity;
            int size = Math.Min(256, Math.Max(required, Math.Max(4, old * 2)));
            int[][] history = new int[size][];
            int[] counts = new int[size];
            int[] writes = new int[size];
            int[] usage = new int[size];
            ulong[] busy = new ulong[size];
            for (int i = 0; i < size; i++)
            {
                history[i] = i < old ? logicalHistory[i] : new int[HistoryLength];
                if (i < old)
                {
                    counts[i] = logicalHistoryCount[i];
                    writes[i] = logicalHistoryWrite[i];
                    usage[i] = logicalUsage[i];
                }
            }
            logicalHistory = history;
            logicalHistoryCount = counts;
            logicalHistoryWrite = writes;
            logicalUsage = usage;
            logicalBusyDelta = busy;
            logicalCapacity = size;
        }

        private void EnsureThreadSlotCapacity(int required)
        {
            if (required <= threadSlotCapacity)
                return;
            int size = Math.Max(required, Math.Max(32, threadSlotCapacity * 2));
            Array.Resize(ref previousThreadIds, size);
            Array.Resize(ref previousThreadRuntime, size);
            Array.Resize(ref previousThreadValid, size);
            threadSlotCapacity = size;
        }

        private void UpdateSystemSnapshot()
        {
            networkReady = global::ZonderqOS.Network.IsReady;
            try
            {
                storageDeviceCount = StorageManager.DeviceCount;
                storagePartitionCount = StorageManager.Partitions.Count;
            }
            catch
            {
                storageDeviceCount = 0;
                storagePartitionCount = 0;
            }
        }

        private static ulong TicksToNanoseconds(ulong ticks, ulong frequency)
        {
            if (frequency == 0) return 0;
            return ticks / frequency * 1_000_000_000UL + ticks % frequency * 1_000_000_000UL / frequency;
        }

        private static void RecordHistory(int[] history, ref int count, ref int write, int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            history[write] = value;
            write = (write + 1) % history.Length;
            if (count < history.Length) count++;
        }

        private static int ReadHistory(int[] history, int count, int write, int index)
        {
            if (index < 0 || index >= count) return 0;
            int start = count < history.Length ? 0 : write;
            return history[(start + index) % history.Length];
        }

        private void EnsureSelectionVisible()
        {
            if (selectedIndex < 0 || activePage == PagePerformance) return;
            int visible = Math.Max(1, view.VisibleRows);
            if (selectedIndex < scrollIndex) scrollIndex = selectedIndex;
            else if (selectedIndex >= scrollIndex + visible) scrollIndex = selectedIndex - visible + 1;
            ClampScroll();
        }

        private void ClampScroll()
        {
            if (activePage == PagePerformance) { scrollIndex = 0; return; }
            int max = Math.Max(0, visibleRows.Count - Math.Max(1, view.VisibleRows));
            scrollIndex = Math.Max(0, Math.Min(scrollIndex, max));
        }

        internal void SetScrollIndex(int value)
        {
            int max = Math.Max(0, visibleRows.Count - Math.Max(1, view.VisibleRows));
            scrollIndex = Math.Max(0, Math.Min(value, max));
        }

        private void ClampLogicalPage()
        {
            logicalPage = Math.Max(0, Math.Min(logicalPage, LogicalPageCount - 1));
        }

        private static string FormatSpeed(int mhz)
        {
            if (mhz <= 0) return "N/A";
            if (mhz >= 1000) return (mhz / 1000) + "." + ((mhz % 1000) / 100) + " GHZ";
            return mhz + " MHZ";
        }

        private static string FormatBytesSmart(ulong bytes)
        {
            if (bytes == 0) return "0 B";
            const ulong KiB = 1024UL;
            const ulong MiB = 1024UL * 1024UL;
            const ulong GiB = 1024UL * 1024UL * 1024UL;

            if (bytes >= GiB)
            {
                ulong whole = bytes / GiB;
                ulong hundredths = (bytes % GiB) * 100UL / GiB;
                return whole + "." + D2((int)hundredths) + " GB";
            }
            if (bytes >= MiB)
            {
                ulong whole = bytes / MiB;
                ulong hundredths = (bytes % MiB) * 100UL / MiB;
                return whole + "." + D2((int)hundredths) + " MB";
            }
            if (bytes >= KiB)
            {
                ulong whole = bytes / KiB;
                ulong tenths = (bytes % KiB) * 10UL / KiB;
                return whole + "." + tenths + " KB";
            }
            return bytes + " B";
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0) return "N/A";
            if (bytes >= 1024UL * 1024UL) return (bytes / (1024UL * 1024UL)) + " MB";
            if (bytes >= 1024UL) return (bytes / 1024UL) + " KB";
            return bytes + " B";
        }

        private static string FormatMiBPrecise(ulong bytes)
        {
            const ulong MiB = 1024UL * 1024UL;
            ulong whole = bytes / MiB;
            ulong hundredths = (bytes % MiB) * 100UL / MiB;
            return whole + "." + D2((int)hundredths) + " MB";
        }

        private static string FormatPercentPrecise(ulong used, ulong total)
        {
            if (total == 0) return "0.00%";
            // Floating-point ratio avoids overflowing used * 10000 on large capacities.
            // Saturate first to keep the displayed percentage in [0, 100].
            if (used >= total) return "100.00%";
            // Double rounds adjacent ulong values together near ulong.MaxValue.
            // Keep a strictly partial usage below 100% even after rounding.
            ulong scaled = Math.Min(9999UL, (ulong)((double)used / total * 10000.0));
            return (scaled / 100UL) + "." + D2((int)(scaled % 100UL)) + "%";
        }

        public override void Close()
        {
            base.Close();
            if (ownsSharedScratch && !releasedSharedScratch)
            {
                releasedSharedScratch = true;
                sharedScratch.LogicalHistory = logicalHistory;
                sharedScratch.LogicalHistoryCount = logicalHistoryCount;
                sharedScratch.LogicalHistoryWrite = logicalHistoryWrite;
                sharedScratch.LogicalUsage = logicalUsage;
                sharedScratch.LogicalBusyDelta = logicalBusyDelta;
                sharedScratch.LogicalCapacity = logicalCapacity;
                sharedScratch.PreviousThreadIds = previousThreadIds;
                sharedScratch.PreviousThreadRuntime = previousThreadRuntime;
                sharedScratch.PreviousThreadValid = previousThreadValid;
                sharedScratch.ThreadSlotCapacity = threadSlotCapacity;

                for (int i = 0; i < rowPool.Count; i++)
                {
                    ModernTaskRow row = rowPool[i];
                    if (row == null)
                        continue;
                    row.Reset();
                    if (sharedScratch.RecycledRows.Count < MaxRecycledRows)
                        sharedScratch.RecycledRows.Add(row);
                }

                Array.Clear(cpuHistory, 0, cpuHistory.Length);
                Array.Clear(memoryHistory, 0, memoryHistory.Length);
                Array.Clear(logicalHistoryCount, 0, logicalHistoryCount.Length);
                Array.Clear(logicalHistoryWrite, 0, logicalHistoryWrite.Length);
                Array.Clear(logicalUsage, 0, logicalUsage.Length);
                Array.Clear(previousThreadValid, 0, previousThreadValid.Length);
                sharedScratchInUse = false;
            }
            else
            {
                for (int i = 0; i < rowPool.Count; i++)
                    rowPool[i]?.Reset();
            }
            rowPool.Clear();
            allRows.Clear();
            visibleRows.Clear();
            processSnapshot.Clear();
            closeCallback?.Invoke();
        }

        internal List<ModernTaskRow> Rows => visibleRows;
        internal int ActivePage => activePage;
        internal int PerformanceResource => performanceResource;
        internal int CpuGraphMode => cpuGraphMode;
        internal int SelectedIndex => selectedIndex;
        internal int ScrollIndex => scrollIndex;
        internal string Status => status;
        internal int ClockHour => clockHour;
        internal int ClockMinute => clockMinute;
        internal int ClockSecondInt => clockSecondInt;
        internal string ClockText
        {
            get
            {
                if (clockText == "--:--:--" || clockSecond != lastFormattedClockSecond)
                {
                    lastFormattedClockSecond = clockSecond;
                    clockText = D2(clockHour) + ":" + D2(clockMinute) + ":" + D2(clockSecondInt);
                }
                return clockText;
            }
        }
        internal string DateText => dateText;
        internal int GuiAppCount => guiAppCount;
        internal int KernelProcessCount => kernelProcessCount;
        internal int CpuUsagePercent => cpuUsagePercent;
        internal ulong UsedMemoryPercent => usedMemoryPercent;
        internal ulong TotalPages => totalPages;
        internal ulong FreePages => freePages;
        internal ulong UsedPages => totalPages - Math.Min(totalPages, freePages);
        internal ulong UsedMemoryMb => UsedPages * PageAllocator.PageSize / (1024UL * 1024UL);
        internal ulong TotalMemoryMb => totalPages * PageAllocator.PageSize / (1024UL * 1024UL);
        internal ulong GcHeapMb => gcHeapBytes / (1024UL * 1024UL);
        internal ulong GcCommittedMb => gcCommittedBytes / (1024UL * 1024UL);
        internal string UsedMemoryText => memoryCache.UsedMemoryText;
        internal string FreeMemoryText => memoryCache.FreeMemoryText;
        internal string TotalMemoryText => memoryCache.TotalMemoryText;
        internal string MemoryUsageText => memoryCache.MemoryUsageText;
        internal string GcHeapText => memoryCache.GcHeapText;
        internal string GcCommittedText => memoryCache.GcCommittedText;
        internal string GcFragmentationText => memoryCache.GcFragmentationText;
        internal string GcCollectionsText => memoryCache.GcCollectionsText;
        internal string PageBreakdownText => memoryCache.PageBreakdownText;
        internal string GraphicsBuffersText => memoryCache.GraphicsBuffersText;
        internal ulong OptimizerSamples => (ulong)Math.Max(0L, optimizerSnapshot.Samples);
        internal ulong OptimizerRequests => (ulong)Math.Max(0L, optimizerSnapshot.Requests);
        internal ulong OptimizerSweeps => (ulong)Math.Max(0L, optimizerSnapshot.Sweeps);
        internal ulong OptimizerDetachedBuffers => (ulong)Math.Max(0L, optimizerSnapshot.DetachedBuffers);
        internal ulong OptimizerCandidateKiB => optimizerSnapshot.CandidateBytes / 1024UL;
        internal string MemoryDeltaText => memoryCache.MemoryDeltaText;
        internal string InstalledRamText => memoryCache.InstalledRamText;
        internal string UsableRamText => memoryCache.UsableRamText;
        internal string HeapPoolText => memoryCache.HeapPoolText;
        internal string MemoryDetailText => memoryCache.MemoryDetailText;
        internal string MemoryPagesText => memoryCache.MemoryPagesText;
        internal string HardwareRamText => memoryCache.HardwareRamText;
        internal string BaseMaxSpeedText => baseMaxSpeedText;
        internal int StorageDeviceCount => storageDeviceCount;
        internal int StoragePartitionCount => storagePartitionCount;
        internal bool NetworkReady => networkReady;
        internal uint OnlineCpuCount => onlineCpuCount;
        internal int SchedulerThreadCount => schedulerThreadCount;
        internal int RunningThreadCount => runningThreadCount;
        internal int ReadyThreadCount => readyThreadCount;
        internal int BlockedThreadCount => blockedThreadCount;
        internal int SleepingThreadCount => sleepingThreadCount;
        internal string SchedulerName => schedulerName;
        internal ulong SchedulerTickUs => schedulerTickNs / 1000UL;
        internal CpuHardwareInfo CpuInfo => cpuInfo;
        internal string BaseSpeedText => baseSpeedText;
        internal string MaxSpeedText => maxSpeedText;
        internal string TopologyText => topologyText;
        internal string CacheText => cacheText;
        internal string SignatureText => signatureText;
        internal int CpuHistoryCount => cpuHistoryCount;
        internal int MemoryHistoryCount => memoryHistoryCount;
        internal int HistoryCapacity => HistoryLength;
        internal int GetCpuHistory(int index) => ReadHistory(cpuHistory, cpuHistoryCount, cpuHistoryWrite, index);
        internal int GetMemoryHistory(int index) => ReadHistory(memoryHistory, memoryHistoryCount, memoryHistoryWrite, index);
        internal int GetLogicalHistory(int cpu, int index) => cpu >= 0 && cpu < logicalCapacity ? ReadHistory(logicalHistory[cpu], logicalHistoryCount[cpu], logicalHistoryWrite[cpu], index) : 0;
        internal int GetLogicalHistoryCount(int cpu) => cpu >= 0 && cpu < logicalCapacity ? logicalHistoryCount[cpu] : 0;
        internal int GetLogicalUsage(int cpu) => cpu >= 0 && cpu < logicalCapacity ? logicalUsage[cpu] : 0;
        internal int LogicalDisplayCount => Math.Min(logicalCapacity, Math.Max(Math.Max(1, cpuInfo.LogicalProcessors), Math.Max(1, (int)onlineCpuCount)));
        internal int LogicalPageCount => Math.Max(1, (LogicalDisplayCount + LogicalPerPage - 1) / LogicalPerPage);
        internal int LogicalPageStart => logicalPage * LogicalPerPage;
        internal int LogicalPageIndex => logicalPage;
        internal int LogicalPerPageCount => LogicalPerPage;
        internal ulong RawGcHeapPages => rawGcHeapPages;
        internal ulong RawRatHeapPages => rawRatHeapPages;
        internal ulong RawSysPages => rawSysPages;
        internal ulong RawGfxBufferBytes => rawGfxBufferBytes;
        internal long RawDeltaPages => rawDeltaPages;
        internal ulong RawGcFragmentedBytes => rawGcFragmentedBytes;
        internal int RawGcCollectionIndex => rawGcCollectionIndex;
        internal int RawGcObjectsFreed => rawGcObjectsFreed;
        internal bool RawGcEnabled => rawGcEnabled;
        internal ulong UptimeSeconds => Stopwatch.Frequency > 0 && Stopwatch.GetTimestamp() > 0 ? (ulong)Stopwatch.GetTimestamp() / (ulong)Stopwatch.Frequency : 0;
        internal ulong ManagerOpenSeconds => Stopwatch.Frequency > 0 ? (ulong)Math.Max(0L, Stopwatch.GetTimestamp() - managerOpenedAt) / (ulong)Stopwatch.Frequency : 0;
    }

    internal sealed class ModernTaskRow
    {
        public string Name = "";
        public string State = "";
        public string Detail = "";
        public Application GuiApplication;
        public int KernelPid;
        public bool CanEnd;
        public bool IsKernel;
        public int WindowWidth;
        public int WindowHeight;

        public void Reset()
        {
            GuiApplication = null;
            KernelPid = -1;
            IsKernel = false;
            CanEnd = false;
            Name = "";
            State = "";
            Detail = "";
            WindowWidth = 0;
            WindowHeight = 0;
        }

        public void SetApplication(Application app, string state, bool canEnd)
        {
            GuiApplication = app;
            KernelPid = -1;
            IsKernel = false;
            CanEnd = canEnd;
            Name = app == null || string.IsNullOrEmpty(app.Name) ? "Application" : app.Name;
            State = state;
            Detail = "GUI WINDOW";
            WindowWidth = app != null && app.Window != null ? app.Window.Width : 0;
            WindowHeight = app != null && app.Window != null ? app.Window.Height : 0;
        }

        public void SetKernel(KernelProcess process, string state, string detail, bool canEnd)
        {
            GuiApplication = null;
            KernelPid = process == null ? -1 : process.PID;
            IsKernel = true;
            CanEnd = canEnd;
            Name = process == null || string.IsNullOrEmpty(process.Name) ? "kernel" : process.Name;
            State = state;
            Detail = detail;
            WindowWidth = 0;
            WindowHeight = 0;
        }
    }

    internal sealed class TaskManagerModernView : Widget
    {
        private const int SidebarWidth = 170;
        private const int HeaderHeight = 54;
        private const int SummaryTop = 60;
        private const int SummaryHeight = 48;
        private const int TableHeaderTop = 116;
        private const int TableHeaderHeight = 30;
        private const int ListTop = 146;
        private const int FooterHeight = 28;
        private const int RowHeight = 36;
        private const int ScrollReserve = 16;
        private const int NavTop = 60;
        private const int NavHeight = 42;
        private const int PerfCardHeight = 72;
        private const int PerfCardGap = 10;

        private static readonly Color Chrome = Color.FromArgb(29, 34, 40);
        private static readonly Color Sidebar = Color.FromArgb(24, 29, 35);
        private static readonly Color Border = Color.FromArgb(64, 76, 88);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color Text = Color.FromArgb(228, 234, 239);
        private static readonly Color Muted = Color.FromArgb(139, 154, 168);
        private static readonly Color Good = Color.FromArgb(111, 194, 145);

        private readonly TaskManagerModernApp app;
        private readonly ScrollBar scrollBar;

        public TaskManagerModernView(int x, int y, int width, int height, TaskManagerModernApp owner) : base(x, y, width, height)
        {
            app = owner;
            scrollBar = new ScrollBar(0, 0, 10, 100);
            scrollBar.ValueChanged = value => app.SetScrollIndex(value);
        }

        // A wide but short window needs the same compact layout as a narrow one.
        // Otherwise the 146px table origin can overlap the footer or leave the client.
        private bool CompactProcessLayout => Width < 550 ||
            Height < ListTop + FooterHeight + RowHeight + 6;
        // The 16-logical-CPU grid needs four 72px rows and three 8px gaps.
        // Its top is 144px from the client origin; keep an 18px gap above
        // the 28px footer (144 + 4 * 72 + 3 * 8 + 18 + 28 = 502).
        // A full layout at 460-501px otherwise draws CPU cards over the footer.
        private bool CompactPerformanceLayout => Width < 900 || Height < 502;
        private int ListTopForView => CompactProcessLayout ? 64 : ListTop;
        // No synthetic row may overlap the footer on a short firmware mode.
        // A zero-row view is intentionally noninteractive, including Delete.
        private int ListHeightForView => Math.Max(0, Height - ListTopForView - FooterHeight - 6);
        private int TableRowWidthForView => Width - SidebarWidth - 12 - ScrollReserve;
        public int VisibleRows => ListHeightForView / RowHeight;
        // A complete row needs enough width for the Details page's
        // 52px PID/name offset plus at least one clipped name glyph.
        // Below 60px the old renderer painted outside the row while its
        // invisible selection remained actionable by Delete.
        internal bool HasPaintedProcessRows => VisibleRows > 0 &&
            TableRowWidthForView >= 60;

        public int NavAt(int x, int y)
        {
            if (Width < SidebarWidth || x < 0 || x >= Width || y < 0 || y >= Height ||
                x < 6 || x >= SidebarWidth - 6 || y < NavTop) return -1;
            if (Height < 300)
            {
                // Same 2x2 navigation layout as DrawNav on short screens.
                int relativeX = x - 6;
                int relativeY = y - NavTop;
                int column = relativeX / 80;
                int row = relativeY / 36;
                if (column > 1 || row > 1 || relativeX % 80 >= 76 ||
                    relativeY % 36 >= 32 || NavTop + row * 36 + 32 > Height)
                    return -1;
                return row * 2 + column;
            }
            int relative = y - NavTop;
            int page = relative / NavHeight;
            return page >= 0 && page <= 3 && relative < NavHeight * 4 ? page : -1;
        }

        public int ToolbarActionAt(int x, int y)
        {
            // Match the painted 30px header buttons exactly (Y+10..Y+39).
            // The old 8..41 hitbox could end a task on invisible pixels;
            // a short client must not accept clicks when RenderHeader skips.
            if (Width < SidebarWidth + 64 || Height < HeaderHeight + 2 ||
                x < SidebarWidth || x >= Width || y < 10 || y >= 40) return 0;
            if (Width < 550)
                return x >= Width - 42 && x < Width - 10 ? 1 : 0;
            int refresh = Width - 210;
            int end = Width - 114;
            if (x >= refresh && x < refresh + 88) return 1;
            if (app.CanEndSelectedTask &&
                x >= end && x < end + 100) return 2;
            return 0;
        }

        public int PerformanceResourceAt(int x, int y)
        {
            if (app.ActivePage != TaskManagerModernApp.PagePerformance ||
                Width < SidebarWidth + 96 || Height < 134) return -1;
            if (CompactPerformanceLayout)
            {
                // Compact mode renders three horizontal resource buttons.
                // Use exactly the same positions for mouse hit-testing.
                int left = SidebarWidth + 8;
                int available = Math.Max(1, Width - SidebarWidth - 16);
                int buttonWidth = Math.Max(1, (available - 8) / 3);
                if (y < 64 || y >= 106 || x < left) return -1;
                int relative = x - left;
                int index = relative / (buttonWidth + 4);
                return index >= 0 && index < 3 &&
                    relative % (buttonWidth + 4) < buttonWidth ? index : -1;
            }

            int cardLeft = SidebarWidth + 10;
            int top = 64;
            int relativeY = y - top;
            int stride = PerfCardHeight + PerfCardGap;
            if (x < cardLeft || x >= cardLeft + 154 || relativeY < 0) return -1;
            int card = relativeY / stride;
            return card >= 0 && card <= 2 && relativeY % stride < PerfCardHeight ? card : -1;
        }

        public int CpuGraphModeAt(int x, int y)
        {
            if (CompactPerformanceLayout || app.ActivePage != TaskManagerModernApp.PagePerformance || app.PerformanceResource != TaskManagerModernApp.PerfCpu) return -1;
            int panelX = SidebarWidth + 180;
            int top = 86;
            if (y < top || y >= top + 26) return -1;
            if (x >= panelX && x < panelX + 92) return TaskManagerModernApp.CpuGraphTotal;
            if (x >= panelX + 98 && x < panelX + 212) return TaskManagerModernApp.CpuGraphLogical;
            return -1;
        }

        public int CpuLogicalPageActionAt(int x, int y)
        {
            if (app.ActivePage != TaskManagerModernApp.PagePerformance ||
                app.PerformanceResource != TaskManagerModernApp.PerfCpu ||
                CompactPerformanceLayout || app.CpuGraphMode != TaskManagerModernApp.CpuGraphLogical ||
                app.LogicalPageCount <= 1) return 0;
            // RenderCpu starts at y=64; RenderLogicalCpu at +58 and DrawArrow at -8.
            // The arrow's actual top is 114, not the graph heading's 122.
            int top = 114;
            // RenderPerformance uses a 10px right inset for the graph panel.
            int right = Width - 10;
            if (y < top || y >= top + 26) return 0;
            if (x >= right - 60 && x < right - 34) return -1;
            if (x >= right - 28 && x < right - 2) return 1;
            return 0;
        }

        public int RowAt(int x, int y)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance || !HasPaintedProcessRows) return -1;
            int contentX = SidebarWidth + 8;
            int listTop = ListTopForView;
            int listH = ListHeightForView;
            // Do not select a process in the 4px gap between painted rows and the scrollbar.
            if (x < contentX || x >= contentX + TableRowWidthForView ||
                y < listTop || y >= listTop + listH) return -1;
            int rowOffset = (y - listTop) / RowHeight;
            // RenderTable paints only VisibleRows complete rows. A click in
            // the unused bottom strip must not select an invisible process.
            if (rowOffset >= VisibleRows)
                return -1;
            int index = app.ScrollIndex + rowOffset;
            return index >= 0 && index < app.Rows.Count ? index : -1;
        }

        public bool HandleScrollMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance || !HasPaintedProcessRows) return false;
            UpdateScrollBar();
            return scrollBar.HandleMouse(mouseX, mouseY, left, oldLeft);
        }

        private void UpdateScrollBar()
        {
            int listH = ListHeightForView;
            scrollBar.X = X + Width - 15;
            scrollBar.Y = Y + ListTopForView + 2;
            scrollBar.Width = 10;
            scrollBar.Height = Math.Max(24, listH - 4);
            scrollBar.SetRange(app.Rows.Count, VisibleRows);
            scrollBar.Value = app.ScrollIndex;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;
            canvas.DrawFilledRectangle(Chrome, X, Y, Width, Height);
            canvas.DrawRectangle(Border, X, Y, Width, Height);
            RenderSidebar(canvas);
            RenderHeader(canvas);
            if (app.ActivePage == TaskManagerModernApp.PagePerformance) RenderPerformance(canvas);
            else RenderProcessPage(canvas);
            RenderFooter(canvas);
        }

        private void RenderSidebar(Canvas canvas)
        {
            if (Width < 2 || Height < 3)
                return;
            int paintedWidth = Math.Min(SidebarWidth, Width - 1);
            canvas.DrawFilledRectangle(Sidebar, X + 1, Y + 1, paintedWidth - 1, Height - 2);
            canvas.DrawLine(Color.FromArgb(52, 62, 72), X + paintedWidth, Y + 1, X + paintedWidth, Y + Height - 2);
            if (Width < SidebarWidth || Height < 36)
                return;
            IconManager.DrawScaled(canvas, IconType.Settings, X + 16, Y + 16, 20, 20);
            SmallTextRenderer.Draw(canvas, "TASK MANAGER", X + 46, Y + 23, Text);
            DrawNav(canvas, 0, "Procesy", IconType.FileManager);
            DrawNav(canvas, 1, "Wydajnosc", IconType.Settings);
            DrawNav(canvas, 2, "Szczegoly", IconType.File);
            DrawNav(canvas, 3, "Uslugi", IconType.Settings);
            if (Height < 300)
                return; // The clock must not overlap compact 2x2 navigation.
            int bottom = Y + Height - 62;
            canvas.DrawLine(Color.FromArgb(48, 58, 68), X + 10, bottom - 8, X + SidebarWidth - 10, bottom - 8);
            SmallTextRenderer.Draw(canvas, "SYSTEM CLOCK", X + 16, bottom + 2, Muted);
            SmallTextRenderer.DrawTime(canvas, app.ClockHour, app.ClockMinute, app.ClockSecondInt, X + 16, bottom + 20, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, app.DateText, X + 82, bottom + 20, Muted);
        }

        private void DrawNav(Canvas canvas, int page, string label, IconType icon)
        {
            if (Height < 300)
            {
                if (NavTop + (page / 2) * 36 + 32 > Height)
                    return;
                int compactX = X + 6 + (page % 2) * 80;
                int compactY = Y + NavTop + (page / 2) * 36;
                bool compactActive = app.ActivePage == page;
                canvas.DrawFilledRectangle(compactActive ? Color.FromArgb(38, 55, 70) :
                    Sidebar, compactX, compactY, 76, 32);
                if (compactActive)
                    canvas.DrawFilledRectangle(Accent, compactX, compactY + 4, 3, 24);
                // Icons alone are ambiguous in the short, wide layout
                // (especially at Full HD). Keep a short readable label inside
                // the same 76x32 hitbox without spilling into the next tile.
                IconManager.DrawScaled(canvas, icon, compactX + 7, compactY + 7, 18, 18);
                string compactLabel = page == 0 ? "PROC" : page == 1 ? "CPU" :
                    page == 2 ? "DET" : "USL";
                SmallTextRenderer.DrawClipped(canvas, compactLabel,
                    compactX + 29, compactY + 12, 42,
                    compactActive ? Color.WhiteSmoke : Color.FromArgb(196, 205, 213));
                return;
            }
            int y = Y + NavTop + page * NavHeight;
            bool active = app.ActivePage == page;
            if (active)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(38, 55, 70), X + 8, y + 2, SidebarWidth - 16, NavHeight - 4);
                canvas.DrawFilledRectangle(Accent, X + 8, y + 7, 3, NavHeight - 14);
            }
            IconManager.DrawScaled(canvas, icon, X + 21, y + 13, 18, 18);
            SmallTextRenderer.Draw(canvas, label, X + 52, y + 18, active ? Color.WhiteSmoke : Color.FromArgb(196, 205, 213));
        }

        private void RenderHeader(Canvas canvas)
        {
            if (Width < SidebarWidth + 64 || Height < HeaderHeight + 2)
                return;
            int contentX = X + SidebarWidth + 8;
            string title = app.ActivePage == 0 ? "Procesy" : app.ActivePage == 1 ? "Wydajnosc" : app.ActivePage == 2 ? "Szczegoly" : "Uslugi";
            if (Width < 550)
            {
                // Do not draw REFRESH, END TASK or the clock over the sidebar.
                // The compact header retains a safe refresh-only button.
                SmallTextRenderer.DrawClipped(canvas, title, contentX + 4, Y + 21,
                    Math.Max(1, Width - SidebarWidth - 64), Color.WhiteSmoke);
                DrawButton(canvas, X + Width - 42, Y + 10, 32, "", IconType.Refresh, false, true);
                canvas.DrawLine(Color.FromArgb(52, 63, 74), contentX,
                    Y + HeaderHeight, X + Width - 4, Y + HeaderHeight);
                return;
            }
            SmallTextRenderer.Draw(canvas, title, contentX + 4, Y + 21, Color.WhiteSmoke);
            int refresh = X + Width - 210;
            int end = X + Width - 114;
            int clock = Math.Max(contentX + 120, refresh - 154);
            // At 550-579px the fixed clock position overlaps REFRESH.
            // Hide only the clock until a complete 72px slot fits before it.
            if (refresh - clock >= 72)
            {
                SmallTextRenderer.DrawTime(canvas, app.ClockHour, app.ClockMinute, app.ClockSecondInt, clock, Y + 13, Color.WhiteSmoke);
                SmallTextRenderer.Draw(canvas, app.DateText, clock, Y + 31, Muted);
            }
            DrawButton(canvas, refresh, Y + 10, 88, "REFRESH", IconType.Refresh, false, true);
            DrawButton(canvas, end, Y + 10, 100, "END TASK", IconType.Close, true, app.CanEndSelectedTask);
            canvas.DrawLine(Color.FromArgb(52, 63, 74), contentX, Y + HeaderHeight, X + Width - 4, Y + HeaderHeight);
        }

        private void DrawButton(Canvas canvas, int x, int y, int width, string label, IconType icon, bool danger, bool enabled)
        {
            Color bg = !enabled ? Color.FromArgb(35, 40, 46) : danger ? Color.FromArgb(63, 42, 47) : Color.FromArgb(47, 55, 64);
            Color border = !enabled ? Color.FromArgb(54, 62, 70) : danger ? Color.FromArgb(111, 65, 73) : Color.FromArgb(80, 94, 108);
            canvas.DrawFilledRectangle(bg, x, y, width, 30);
            canvas.DrawRectangle(border, x, y, width, 30);
            if (width < 40)
            {
                // Compact refresh-only header: avoid a negative text width.
                IconManager.DrawScaled(canvas, icon, x + (width - 16) / 2, y + 7, 16, 16);
                return;
            }
            IconManager.DrawScaled(canvas, icon, x + 7, y + 7, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 29, y + 12, width - 34, enabled ? Color.WhiteSmoke : Muted);
        }

        private void RenderProcessPage(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int width = Width - SidebarWidth - 12;
            if (CompactProcessLayout)
            {
                // Summary cards also overlap the footer in short, wide windows.
                // Use the available area for a compact, scrollable task list.
                RenderTable(canvas, x, width);
                return;
            }
            int gap = 8;
            int cardW = Math.Max(86, (width - gap * 3) / 4);
            DrawSummary(canvas, x, cardW, "APPS", (ulong)app.GuiAppCount, "APPLICATIONS", false);
            DrawSummary(canvas, x + cardW + gap, cardW, "THREADS", (ulong)Math.Max(0, app.SchedulerThreadCount), "SCHEDULER", false);
            DrawSummary(canvas, x + (cardW + gap) * 2, cardW, "CPU", (ulong)app.CpuUsagePercent, "TOTAL USAGE", true);
            DrawSummaryText(canvas, x + (cardW + gap) * 3, cardW, "MEMORY", app.MemoryUsageText, "COSMOS HEAP");
            RenderTable(canvas, x, width);
        }

        private void DrawSummary(Canvas canvas, int x, int width, string title, ulong value, string detail, bool percent)
        {
            int y = Y + SummaryTop;
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, SummaryHeight);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, SummaryHeight);
            SmallTextRenderer.Draw(canvas, title, x + 10, y + 12, Muted);
            SmallTextRenderer.DrawUInt(canvas, value, x + 10, y + 27, Text);
            if (percent) SmallTextRenderer.Draw(canvas, "%", x + 12 + SmallTextRenderer.WidthUInt(value), y + 27, Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 66, y + 27, Math.Max(20, width - 74), Muted);
        }

        private void DrawSummaryText(Canvas canvas, int x, int width, string title, string value, string detail)
        {
            int y = Y + SummaryTop;
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, SummaryHeight);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, SummaryHeight);
            SmallTextRenderer.Draw(canvas, title, x + 10, y + 12, Muted);
            SmallTextRenderer.DrawClipped(canvas, value, x + 10, y + 27, 56, Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 72, y + 27, Math.Max(20, width - 80), Muted);
        }

        private void RenderTable(Canvas canvas, int x, int width)
        {
            // The sidebar can occupy nearly the whole view in tiny modes.
            // Never send negative table/scrollbar dimensions to Canvas.
            if (!HasPaintedProcessRows)
                return;
            int headerY = Y + TableHeaderTop;
            if (!CompactProcessLayout)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(33, 39, 46), x, headerY, width, TableHeaderHeight);
            }
            if (CompactProcessLayout)
            {
                // No header row in compact mode; the list starts at y=64.
            }
            else if (width < 520)
            {
                // Narrow windows cannot fit four columns. The compact rows
                // show name and state without drawing beyond the client area.
                SmallTextRenderer.Draw(canvas, app.ActivePage == 2 ? "PID / NAME" : "NAME",
                    x + 12, headerY + 11, Muted);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, app.ActivePage == 2 ? "PID" : "NAME", x + 12, headerY + 11, Muted);
                SmallTextRenderer.Draw(canvas, app.ActivePage == 2 ? "NAME" : "TYPE", x + 90, headerY + 11, Muted);
                SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(370, width - 240), headerY + 11, Muted);
                SmallTextRenderer.Draw(canvas, "DETAIL", x + Math.Max(470, width - 120), headerY + 11, Muted);
            }

            int listY = Y + ListTopForView;
            int listH = ListHeightForView;
            int rowW = TableRowWidthForView;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x, listY, width, listH);
            canvas.DrawRectangle(Color.FromArgb(50, 60, 70), x, listY, width, listH);

            for (int rowIndex = 0; rowIndex < VisibleRows; rowIndex++)
            {
                int index = app.ScrollIndex + rowIndex;
                if (index >= app.Rows.Count) break;
                ModernTaskRow row = app.Rows[index];
                int y = listY + rowIndex * RowHeight;
                bool selected = index == app.SelectedIndex;
                if (selected) canvas.DrawFilledRectangle(Color.FromArgb(38, 62, 82), x + 2, y + 1, rowW - 2, RowHeight - 2);
                else if ((rowIndex & 1) != 0) canvas.DrawFilledRectangle(Color.FromArgb(27, 33, 39), x + 2, y + 1, rowW - 2, RowHeight - 2);
                DrawRow(canvas, row, x, rowW, y, selected);
            }

            UpdateScrollBar();
            scrollBar.Render(canvas);
        }

        private void DrawRow(Canvas canvas, ModernTaskRow row, int x, int width, int y, bool selected)
        {
            if (width < 520)
            {
                int nameOffset;
                if (app.ActivePage == 2)
                {
                    if (row.KernelPid > 0)
                        SmallTextRenderer.DrawUInt(canvas, (ulong)row.KernelPid, x + 8, y + 12, Muted);
                    else
                        SmallTextRenderer.Draw(canvas, "GUI", x + 8, y + 12, Muted);
                    nameOffset = 52;
                }
                else
                {
                    IconManager.DrawScaled(canvas, row.IsKernel ? IconType.Settings : IconType.File,
                        x + 8, y + 9, 18, 18);
                    nameOffset = 34;
                }

                int textWidth = Math.Max(1, width - nameOffset - 4);
                SmallTextRenderer.DrawClipped(canvas, row.Name, x + nameOffset, y + 10,
                    textWidth, selected ? Color.WhiteSmoke : Text);
                SmallTextRenderer.DrawClipped(canvas, row.State, x + nameOffset, y + 23,
                    textWidth, row.State == "ACTIVE" ? Good : Muted);
                return;
            }

            if (app.ActivePage == 2)
            {
                if (row.KernelPid > 0) SmallTextRenderer.DrawUInt(canvas, (ulong)row.KernelPid, x + 12, y + 14, Muted);
                else SmallTextRenderer.Draw(canvas, "GUI", x + 12, y + 14, Muted);
                SmallTextRenderer.DrawClipped(canvas, row.Name, x + 90, y + 14, Math.Max(80, width - 380), selected ? Color.WhiteSmoke : Text);
            }
            else
            {
                IconManager.DrawScaled(canvas, row.IsKernel ? IconType.Settings : IconType.File, x + 12, y + 9, 18, 18);
                SmallTextRenderer.DrawClipped(canvas, row.Name, x + 40, y + 14, Math.Max(80, width - 410), selected ? Color.WhiteSmoke : Text);
                SmallTextRenderer.Draw(canvas, row.IsKernel ? "KERNEL" : "APP", x + 90, y + 25, Muted);
            }
            int statusX = x + Math.Max(370, width - 240);
            int detailX = x + Math.Max(470, width - 120);
            SmallTextRenderer.DrawClipped(canvas, row.State, statusX, y + 14, 82, row.State == "ACTIVE" ? Good : Muted);
            SmallTextRenderer.DrawClipped(canvas, row.Detail, detailX, y + 14, Math.Max(30, width - (detailX - x) - 6), Muted);
        }

        private void RenderPerformance(Canvas canvas)
        {
            // At very short resolutions the selectors would overlap the footer.
            if (Width < SidebarWidth + 96 || Height < 134)
                return;
            if (CompactPerformanceLayout)
            {
                // The normal graph needs both a wide and a tall client:
                // 360px sidebar/resource offset + 540px = 900px view.
                // Below that, use compact cards so 800x600 stays in bounds.
                // Keep all three selectors usable, showing the selected
                // telemetry without drawing an off-screen graph.
                int left = X + SidebarWidth + 8;
                int available = Math.Max(1, Width - SidebarWidth - 16);
                int buttonWidth = Math.Max(1, (available - 8) / 3);
                int top = Y + 64;
                DrawCompactResource(canvas, left, top, buttonWidth, 0, "CPU", (ulong)app.CpuUsagePercent, "%");
                DrawCompactResource(canvas, left + buttonWidth + 4, top, buttonWidth, 1, "MEM", app.MemoryUsageText);
                DrawCompactResource(canvas, left + (buttonWidth + 4) * 2, top, buttonWidth, 2, "SYS", (ulong)Math.Max(0, app.SchedulerThreadCount), null);

                // Leave the 28px footer unobstructed on 320x240 desktops.
                if (Height >= 180)
                {
                    if (app.PerformanceResource == 0)
                    {
                        SmallTextRenderer.Draw(canvas, "CPU ", left, Y + 122, Text);
                        SmallTextRenderer.DrawUInt(canvas, (ulong)app.CpuUsagePercent, left + 24, Y + 122, Text);
                        SmallTextRenderer.Draw(canvas, "%", left + 24 + SmallTextRenderer.WidthUInt((ulong)app.CpuUsagePercent), Y + 122, Text);
                    }
                    else if (app.PerformanceResource == 1)
                    {
                        SmallTextRenderer.Draw(canvas, "RAM ", left, Y + 122, Text);
                        SmallTextRenderer.DrawClipped(canvas, app.MemoryUsageText, left + 24, Y + 122, Math.Max(1, available - 24), Text);
                    }
                    else
                    {
                        SmallTextRenderer.Draw(canvas, "Threads ", left, Y + 122, Text);
                        SmallTextRenderer.DrawUInt(canvas, (ulong)Math.Max(0, app.SchedulerThreadCount), left + 48, Y + 122, Text);
                    }
                }
                return;
            }

            int resourceX = X + SidebarWidth + 10;
            int resourceY = Y + 64;
            DrawResource(canvas, resourceX, resourceY, 0, "CPU", (ulong)app.CpuUsagePercent, "%");
            DrawResourceText(canvas, resourceX, resourceY + 82, 1, "MEMORY", app.MemoryDetailText);
            DrawResource(canvas, resourceX, resourceY + 164, 2, "SYSTEM", (ulong)Math.Max(0, app.SchedulerThreadCount), "");

            int x = resourceX + 170;
            int y = resourceY;
            int width = Math.Max(360, Width - (x - X) - 10);
            if (app.PerformanceResource == 0) RenderCpu(canvas, x, y, width);
            else if (app.PerformanceResource == 1) RenderMemory(canvas, x, y, width);
            else RenderSystem(canvas, x, y, width);
        }

        private void DrawCompactResource(Canvas canvas, int x, int y, int width,
            int resource, string title, string value)
        {
            bool active = app.PerformanceResource == resource;
            canvas.DrawFilledRectangle(active ? Color.FromArgb(35, 55, 71) :
                Color.FromArgb(26, 32, 38), x, y, width, 42);
            canvas.DrawRectangle(active ? Color.FromArgb(65, 126, 169) :
                Color.FromArgb(52, 63, 74), x, y, width, 42);
            SmallTextRenderer.DrawClipped(canvas, title, x + 4, y + 10,
                Math.Max(1, width - 8), active ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawClipped(canvas, value, x + 4, y + 25,
                Math.Max(1, width - 8), active ? Color.FromArgb(126, 194, 238) : Muted);
        }

        private void DrawCompactResource(Canvas canvas, int x, int y, int width,
            int resource, string title, ulong value, string suffix)
        {
            bool active = app.PerformanceResource == resource;
            canvas.DrawFilledRectangle(active ? Color.FromArgb(35, 55, 71) :
                Color.FromArgb(26, 32, 38), x, y, width, 42);
            canvas.DrawRectangle(active ? Color.FromArgb(65, 126, 169) :
                Color.FromArgb(52, 63, 74), x, y, width, 42);
            SmallTextRenderer.DrawClipped(canvas, title, x + 4, y + 10,
                Math.Max(1, width - 8), active ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawUInt(canvas, value, x + 4, y + 25,
                active ? Color.FromArgb(126, 194, 238) : Muted);
            if (!string.IsNullOrEmpty(suffix))
            {
                SmallTextRenderer.Draw(canvas, suffix, x + 4 + SmallTextRenderer.WidthUInt(value), y + 25,
                    active ? Color.FromArgb(126, 194, 238) : Muted);
            }
        }

        private void DrawResource(Canvas canvas, int x, int y, int resource, string title, ulong value, string suffix)
        {
            bool active = app.PerformanceResource == resource;
            canvas.DrawFilledRectangle(active ? Color.FromArgb(35, 55, 71) : Color.FromArgb(26, 32, 38), x, y, 154, 72);
            canvas.DrawRectangle(active ? Color.FromArgb(65, 126, 169) : Color.FromArgb(52, 63, 74), x, y, 154, 72);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 13, active ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawUInt(canvas, value, x + 12, y + 32, active ? Color.FromArgb(126, 194, 238) : Text);
            if (!string.IsNullOrEmpty(suffix)) SmallTextRenderer.Draw(canvas, suffix, x + 14 + SmallTextRenderer.WidthUInt(value), y + 32, Text);
        }

        private void DrawResourceText(Canvas canvas, int x, int y, int resource, string title, string value)
        {
            bool active = app.PerformanceResource == resource;
            canvas.DrawFilledRectangle(active ? Color.FromArgb(35, 55, 71) : Color.FromArgb(26, 32, 38), x, y, 154, 72);
            canvas.DrawRectangle(active ? Color.FromArgb(65, 126, 169) : Color.FromArgb(52, 63, 74), x, y, 154, 72);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 13, active ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawClipped(canvas, value, x + 12, y + 32, 130, active ? Color.FromArgb(126, 194, 238) : Text);
        }

        private void RenderCpu(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "CPU", x, y + 4, Color.WhiteSmoke);
            SmallTextRenderer.DrawClipped(canvas, app.CpuInfo.Brand, x + 44, y + 4, width - 48, Text);
            DrawMode(canvas, x, y + 22, 92, 0, "LACZNIE");
            DrawMode(canvas, x + 98, y + 22, 114, 1, "LOGICZNE");

            if (app.CpuGraphMode == TaskManagerModernApp.CpuGraphLogical)
                RenderLogicalCpu(canvas, x, y + 58, width);
            else
                RenderTotalCpu(canvas, x, y + 58, width);
        }

        private void DrawMode(Canvas canvas, int x, int y, int width, int mode, string text)
        {
            bool active = app.CpuGraphMode == mode;
            canvas.DrawFilledRectangle(active ? Color.FromArgb(38, 67, 88) : Color.FromArgb(31, 38, 45), x, y, width, 26);
            canvas.DrawRectangle(active ? Color.FromArgb(73, 139, 184) : Color.FromArgb(58, 69, 80), x, y, width, 26);
            SmallTextRenderer.DrawClipped(canvas, text, x + 9, y + 10, width - 18, active ? Color.WhiteSmoke : Muted);
        }

        private const int GraphCpu = -1;
        private const int GraphMemory = -2;

        private void RenderTotalCpu(Canvas canvas, int x, int y, int width)
        {
            int graphH = Math.Max(145, Height - 390);
            DrawGraph(canvas, x, y + 16, width, graphH, app.CpuHistoryCount, GraphCpu, Accent, true);
            int stats = y + graphH + 54;
            DrawPercent(canvas, app.CpuUsagePercent, x, stats);
            int left = x + 124;
            int right = x + Math.Max(380, width / 2 + 80);
            int rightW = Math.Max(160, width - (right - x));
            DrawKey(canvas, left, stats, 230, "PHYSICAL CORES", (ulong)Math.Max(1, app.CpuInfo.PhysicalCores), null);
            DrawKey(canvas, left, stats + 20, 230, "LOGICAL CPU", (ulong)Math.Max(1, app.CpuInfo.LogicalProcessors), null);
            DrawKey(canvas, left, stats + 40, 230, "THREADS/CORE", (ulong)Math.Max(1, app.CpuInfo.ThreadsPerCore), null);
            DrawKey(canvas, left, stats + 60, 230, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, left, stats + 80, 230, "BASE / MAX", app.BaseMaxSpeedText);
            DrawKeyText(canvas, left, stats + 100, 230, "PLATFORM", "x86_64 Cosmos Native");
            DrawKey(canvas, right, stats, rightW, "SCHED THREADS", (ulong)Math.Max(0, app.SchedulerThreadCount), null);
            DrawKeyText(canvas, right, stats + 20, rightW, "SCHEDULER", app.SchedulerName);
            DrawKeyText(canvas, right, stats + 40, rightW, "VENDOR", app.CpuInfo.Vendor);
            DrawKeyText(canvas, right, stats + 60, rightW, "TOPOLOGY C/L/T", app.TopologyText);
            DrawKeyText(canvas, right, stats + 80, rightW, "CACHE L1/L2/L3", app.CacheText);
            DrawKeyText(canvas, right, stats + 100, rightW, "HARDWARE", app.CpuInfo.HypervisorPresent ? "VIRTUAL (QEMU/VM)" : "FIZYCZNY (BARE-METAL)");
        }

        private void RenderLogicalCpu(Canvas canvas, int x, int y, int width)
        {
            int total = app.LogicalDisplayCount;
            int start = app.LogicalPageStart;
            int count = Math.Min(app.LogicalPerPageCount, Math.Max(0, total - start));
            SmallTextRenderer.Draw(canvas, "LOGICAL PROCESSORS", x, y, Muted);
            if (app.LogicalPageCount > 1)
            {
                int right = x + width;
                SmallTextRenderer.DrawUInt(canvas, (ulong)(app.LogicalPageIndex + 1), right - 108, y, Text);
                SmallTextRenderer.Draw(canvas, "/", right - 92, y, Muted);
                SmallTextRenderer.DrawUInt(canvas, (ulong)app.LogicalPageCount, right - 80, y, Muted);
                DrawArrow(canvas, right - 60, y - 8, "<");
                DrawArrow(canvas, right - 28, y - 8, ">");
            }

            int gridY = y + 22;
            int availableH = Math.Max(220, Height - (gridY - Y) - FooterHeight - 18);
            int columns = count <= 4 ? 2 : count <= 9 ? 3 : 4;
            int rows = Math.Max(1, (count + columns - 1) / columns);
            int gap = 8;
            int cellW = Math.Max(110, (width - gap * (columns - 1)) / columns);
            int cellH = Math.Max(72, (availableH - gap * (rows - 1)) / rows);
            for (int i = 0; i < count; i++)
            {
                int cpu = start + i;
                int cx = x + (i % columns) * (cellW + gap);
                int cy = gridY + (i / columns) * (cellH + gap);
                DrawLogicalCard(canvas, cpu, cx, cy, cellW, cellH);
            }
        }

        private void DrawArrow(Canvas canvas, int x, int y, string text)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(31, 38, 45), x, y, 26, 26);
            canvas.DrawRectangle(Color.FromArgb(58, 69, 80), x, y, 26, 26);
            SmallTextRenderer.Draw(canvas, text, x + 9, y + 10, Text);
        }

        private void DrawLogicalCard(Canvas canvas, int cpu, int x, int y, int width, int height)
        {
            bool online = (uint)cpu < app.OnlineCpuCount;
            int usage = online ? app.GetLogicalUsage(cpu) : 0;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 30, 36), x, y, width, height);
            canvas.DrawRectangle(online ? Color.FromArgb(61, 91, 113) : Color.FromArgb(52, 58, 64), x, y, width, height);
            SmallTextRenderer.Draw(canvas, "CPU", x + 7, y + 8, online ? Text : Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)cpu, x + 34, y + 8, online ? Text : Muted);
            if (online)
            {
                SmallTextRenderer.DrawUInt(canvas, (ulong)usage, x + width - 42, y + 8, Color.FromArgb(126, 194, 238));
                SmallTextRenderer.Draw(canvas, "%", x + width - 18, y + 8, Color.FromArgb(126, 194, 238));
            }
            else SmallTextRenderer.Draw(canvas, "OFF", x + width - 30, y + 8, Muted);
            DrawGraph(canvas, x + 5, y + 24, width - 10, Math.Max(32, height - 30), app.GetLogicalHistoryCount(cpu), cpu, online ? Accent : Muted, false);
        }

        private void RenderMemory(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "MEMORY (RAM FIZYCZNY I ALOKATOR COSMOS)", x, y + 4, Color.WhiteSmoke);
            int graphH = Math.Max(120, Height - 430);
            DrawGraph(canvas, x, y + 36, width, graphH, app.MemoryHistoryCount, GraphMemory, Color.FromArgb(116, 174, 219), true);
            int stats = y + graphH + 50;
            int half = width / 2;

            // Physical Hardware RAM & PageAllocator Breakdown
            DrawKeyText(canvas, x, stats, half, "RAM ZAINSTALOWANY", app.InstalledRamText);
            DrawKeyText(canvas, x, stats + 20, half, "RAM WYKRYTY (FIRMWARE)", app.UsableRamText);
            DrawKeyText(canvas, x, stats + 40, half, "PULA HEAP COSMOS", app.HeapPoolText);
            DrawKeyText(canvas, x, stats + 60, half, "STRONY PAMIECI", app.MemoryPagesText);
            int leftValX = DrawKeyLabel(canvas, x, stats + 80, half, "STRONY GC/RAT/SYS");
            SmallTextRenderer.DrawPageBreakdown(canvas, app.RawGcHeapPages, app.RawRatHeapPages, app.RawSysPages, leftValX, stats + 80, Text);
            DrawKeyLabel(canvas, x, stats + 100, half, "BUFORY GRAFICZNE");
            SmallTextRenderer.DrawByteSizeSmart(canvas, app.RawGfxBufferBytes, leftValX, stats + 100, Text);
            DrawKeyLabel(canvas, x, stats + 120, half, "DELTA PAMIECI");
            SmallTextRenderer.DrawMemoryDeltaPages(canvas, app.RawDeltaPages, PageAllocator.PageSize, leftValX, stats + 120, Text);
            // Optimizer counters: numeric drawing avoids a fresh string on
            // each telemetry refresh. "Candidate" is unrooted cache memory,
            // NOT physical RAM already reclaimed by OrionGC.
            DrawKey(canvas, x, stats + 140, half, "MEMOPT PROBKI", app.OptimizerSamples, null);
            DrawKey(canvas, x, stats + 160, half, "MEMOPT ZADANIA", app.OptimizerRequests, null);

            // Cosmos Heap Allocator & GC Diagnostics
            DrawKeyText(canvas, x + half, stats, half, "HEAP UZYTY", app.MemoryDetailText);
            DrawKeyText(canvas, x + half, stats + 20, half, "HEAP WOLNY", app.FreeMemoryText);
            DrawKeyText(canvas, x + half, stats + 40, half, "GC ROZMIAR HEAP", app.GcHeapText);
            DrawKeyText(canvas, x + half, stats + 60, half, "GC ZATWIERDZONE", app.GcCommittedText);
            int rightValX = DrawKeyLabel(canvas, x + half, stats + 80, half, "GC FRAGMENTACJA");
            SmallTextRenderer.DrawMiBOrNa(canvas, app.RawGcFragmentedBytes, app.RawGcEnabled, rightValX, stats + 80, Text);
            DrawKeyLabel(canvas, x + half, stats + 100, half, "GC KOLEKCJE / ZWOL.");
            SmallTextRenderer.DrawGcCollections(canvas, app.RawGcCollectionIndex, app.RawGcObjectsFreed, app.RawGcEnabled, rightValX, stats + 100, Text);
            DrawKey(canvas, x + half, stats + 120, half, "CACHE SWEEPS", app.OptimizerSweeps, null);
            DrawKey(canvas, x + half, stats + 140, half, "ODPIETE BUFORY", app.OptimizerDetachedBuffers, null);
            DrawKey(canvas, x + half, stats + 160, half, "GC KANDYDAT KiB", app.OptimizerCandidateKiB, null);
        }

        private void RenderSystem(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "SYSTEM", x, y + 4, Color.WhiteSmoke);
            int cardY = y + 36;
            int gap = 8;
            int cardW = Math.Max(100, (width - gap * 3) / 4);
            DrawSystemCard(canvas, x, cardY, cardW, "CORES", (ulong)Math.Max(1, app.CpuInfo.PhysicalCores), "PHYSICAL");
            DrawSystemCard(canvas, x + cardW + gap, cardY, cardW, "LOGICAL", (ulong)Math.Max(1, app.CpuInfo.LogicalProcessors), "CPU THREADS");
            DrawSystemCard(canvas, x + (cardW + gap) * 2, cardY, cardW, "THREADS", (ulong)Math.Max(0, app.SchedulerThreadCount), "SCHEDULER");
            DrawSystemCard(canvas, x + (cardW + gap) * 3, cardY, cardW, "CPU", (ulong)app.CpuUsagePercent, "% TOTAL");

            int row = cardY + 88;
            int half = width / 2 - 8;
            DrawKeyClock(canvas, x, row, half, "CLOCK", app.ClockHour, app.ClockMinute, app.ClockSecondInt);
            DrawKeyText(canvas, x, row + 20, half, "DATE", app.DateText);
            DrawKey(canvas, x, row + 40, half, "UPTIME", app.UptimeSeconds, "SEC");
            DrawKey(canvas, x, row + 60, half, "MANAGER OPEN", app.ManagerOpenSeconds, "SEC");
            DrawKey(canvas, x, row + 80, half, "THREADS/CORE", (ulong)Math.Max(1, app.CpuInfo.ThreadsPerCore), null);
            DrawKey(canvas, x, row + 100, half, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, x, row + 120, half, "SCHEDULER", app.SchedulerName);
            DrawKey(canvas, x, row + 140, half, "SCHED TICK", app.SchedulerTickUs, "US");
            DrawKeyText(canvas, x, row + 160, half, "RAM SPRZET", app.HardwareRamText);

            int right = x + width / 2 + 8;
            DrawKey(canvas, right, row, half, "RUNNING", (ulong)Math.Max(0, app.RunningThreadCount), null);
            DrawKey(canvas, right, row + 20, half, "READY", (ulong)Math.Max(0, app.ReadyThreadCount), null);
            DrawKey(canvas, right, row + 40, half, "BLOCKED", (ulong)Math.Max(0, app.BlockedThreadCount), null);
            DrawKey(canvas, right, row + 60, half, "SLEEPING", (ulong)Math.Max(0, app.SleepingThreadCount), null);
            DrawKey(canvas, right, row + 80, half, "GUI APPS", (ulong)Math.Max(0, app.GuiAppCount), null);
            DrawKey(canvas, right, row + 100, half, "KERNEL PROCS", (ulong)Math.Max(0, app.KernelProcessCount), null);
            DrawKey(canvas, right, row + 120, half, "STORAGE", (ulong)Math.Max(0, app.StorageDeviceCount), null);
            DrawKeyText(canvas, right, row + 140, half, "NETWORK", app.NetworkReady ? "READY" : "OFFLINE");
            DrawKeyText(canvas, right, row + 160, half, "RAM UZYTY", app.MemoryDetailText);
        }

        private void DrawSystemCard(Canvas canvas, int x, int y, int width, string title, ulong value, string detail)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, 68);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, 68);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 13, Muted);
            SmallTextRenderer.DrawUInt(canvas, value, x + 12, y + 31, Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 12, y + 49, width - 24, Muted);
        }

        private void DrawGraph(Canvas canvas, int x, int y, int width, int height, int count, int graphKind, Color line, bool labels)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x, y, width, height);
            canvas.DrawRectangle(Color.FromArgb(66, 111, 145), x, y, width, height);
            for (int i = 1; i < 4; i++) canvas.DrawLine(Color.FromArgb(39, 52, 63), x + 1, y + i * height / 4, x + width - 1, y + i * height / 4);
            for (int i = 1; i < 6; i++) canvas.DrawLine(Color.FromArgb(35, 47, 58), x + i * width / 6, y + 1, x + i * width / 6, y + height - 1);

            if (count > 1)
            {
                int denom = Math.Max(1, app.HistoryCapacity - 1);
                int offset = app.HistoryCapacity - count;
                int previousX = x + offset * width / denom;
                int previousValue = graphKind == GraphCpu ? app.GetCpuHistory(0) :
                                    graphKind == GraphMemory ? app.GetMemoryHistory(0) :
                                    graphKind >= 0 ? app.GetLogicalHistory(graphKind, 0) : 0;
                int previousY = y + height - previousValue * height / 100;
                for (int i = 1; i < count; i++)
                {
                    int value = graphKind == GraphCpu ? app.GetCpuHistory(i) :
                                graphKind == GraphMemory ? app.GetMemoryHistory(i) :
                                graphKind >= 0 ? app.GetLogicalHistory(graphKind, i) : 0;
                    int px = x + (offset + i) * width / denom;
                    int py = y + height - value * height / 100;
                    canvas.DrawLine(line, previousX, previousY, px, py);
                    if (previousY + 1 < y + height && py + 1 < y + height)
                        canvas.DrawLine(line, previousX, previousY + 1, px, py + 1);
                    previousX = px;
                    previousY = py;
                }
            }
            if (labels)
            {
                SmallTextRenderer.Draw(canvas, "100%", x + 6, y + 7, Muted);
                SmallTextRenderer.Draw(canvas, "0%", x + 6, y + height - 12, Muted);
            }
        }

        private void DrawPercent(Canvas canvas, int value, int x, int y)
        {
            ulong bounded = (ulong)Math.Max(0, Math.Min(100, value));
            SmallTextRenderer.DrawUInt(canvas, bounded, x, y, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, "% UTILIZATION", x + SmallTextRenderer.WidthUInt(bounded) + 8, y, Muted);
        }

        private int DrawKeyLabel(Canvas canvas, int x, int y, int width, string key)
        {
            int keyW = Math.Min(142, Math.Max(84, width / 2));
            SmallTextRenderer.DrawClipped(canvas, key, x, y, keyW - 4, Muted);
            return x + keyW;
        }

        private void DrawKey(Canvas canvas, int x, int y, int width, string key, ulong value, string suffix)
        {
            int valueX = DrawKeyLabel(canvas, x, y, width, key);
            SmallTextRenderer.DrawUInt(canvas, value, valueX, y, Text);
            if (!string.IsNullOrEmpty(suffix)) SmallTextRenderer.Draw(canvas, suffix, valueX + SmallTextRenderer.WidthUInt(value) + 6, y, Text);
        }

        private void DrawKeyText(Canvas canvas, int x, int y, int width, string key, string value)
        {
            int valueX = DrawKeyLabel(canvas, x, y, width, key);
            SmallTextRenderer.DrawClipped(canvas, value, valueX, y, Math.Max(20, x + width - valueX), Text);
        }

        private void DrawKeyClock(Canvas canvas, int x, int y, int width, string key, int hour, int min, int sec)
        {
            int valueX = DrawKeyLabel(canvas, x, y, width, key);
            SmallTextRenderer.DrawTime(canvas, hour, min, sec, valueX, y, Text);
        }

        private void RenderFooter(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int y = Y + Height - FooterHeight;
            int width = Width - SidebarWidth - 12;
            if (width <= 0 || Height < FooterHeight)
                return;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x, y, width, FooterHeight - 4);
            canvas.DrawLine(Color.FromArgb(58, 69, 80), x, y, x + width, y);
            if (width < 520)
            {
                // At minimum window width, the fixed shortcut/clock columns
                // would start inside the sidebar and overlap the status.
                SmallTextRenderer.DrawClipped(canvas, app.Status, x + 8, y + 9,
                    Math.Max(1, width - 16), Text);
            }
            else
            {
                SmallTextRenderer.DrawClipped(canvas, app.Status, x + 8, y + 9, Math.Max(20, width - 260), Text);
                SmallTextRenderer.Draw(canvas, "F5 REFRESH   DEL END TASK", x + width - 244, y + 9, Muted);
                SmallTextRenderer.DrawTime(canvas, app.ClockHour, app.ClockMinute, app.ClockSecondInt, x + width - 72, y + 9, Text);
            }
        }
    }
}
