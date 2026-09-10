using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using SchedulerThread = Cosmos.Kernel.Core.Scheduler.Thread;
using SchedulerThreadState = Cosmos.Kernel.Core.Scheduler.ThreadState;
using Font = Cosmos.Kernel.System.Graphics.Fonts.Font;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Windows-inspired task manager. The hot Update/Render path is intentionally
    /// allocation-free after the initial row pool has warmed up. This matters on
    /// Cosmos where repeated short-lived GUI strings can make the managed heap grow
    /// noticeably before the collector decides to reclaim them.
    /// </summary>
    public sealed class TaskManagerModernApp : Application
    {
        internal const int PageProcesses = 0;
        internal const int PagePerformance = 1;
        internal const int PageDetails = 2;
        internal const int PageServices = 3;

        internal const int PerfCpu = 0;
        internal const int PerfMemory = 1;
        internal const int PerfSystem = 2;

        private const int RefreshIntervalFrames = 45;
        private const int HistoryLength = 120;
        private const int InitialRowPool = 32;

        private const string StateActive = "ACTIVE";
        private const string StateRunning = "RUNNING";
        private const string StateMinimized = "MINIMIZED";
        private const string StateStopped = "STOPPED";
        private const string DetailKernel = "KERNEL THREAD";
        private const string DetailProtected = "SYSTEM PROTECTED";

        private readonly ApplicationManager applicationManager;
        private readonly Action closeCallback;
        private readonly TaskManagerModernView view;
        private readonly CpuHardwareInfo cpuInfo;

        private readonly List<KernelProcess> processSnapshot = new List<KernelProcess>(16);
        private readonly List<ModernTaskRow> rowPool = new List<ModernTaskRow>(InitialRowPool);
        private readonly List<ModernTaskRow> allRows = new List<ModernTaskRow>(InitialRowPool);
        private readonly List<ModernTaskRow> visibleRows = new List<ModernTaskRow>(InitialRowPool);

        private readonly int[] cpuHistory = new int[HistoryLength];
        private readonly int[] memoryHistory = new int[HistoryLength];
        private int cpuHistoryCount;
        private int cpuHistoryWrite;
        private int memoryHistoryCount;
        private int memoryHistoryWrite;

        private int activePage = PageProcesses;
        private int performanceResource = PerfCpu;
        private int selectedIndex = -1;
        private int scrollIndex;
        private int refreshFrame;
        private string status = "Gotowy";

        private int guiAppCount;
        private int kernelProcessCount;
        private int cpuUsagePercent;
        private ulong totalPages;
        private ulong freePages;
        private ulong usedMemoryPercent;
        private ulong gcHeapBytes;
        private ulong gcCommittedBytes;
        private ulong gcFragmentedBytes;
        private ulong gcPinnedObjects;
        private int gcCollections;
        private int storageDeviceCount;
        private int storagePartitionCount;
        private bool networkReady;

        private uint onlineCpuCount;
        private int schedulerThreadCount;
        private int readyThreadCount;
        private int runningThreadCount;
        private int blockedThreadCount;
        private int sleepingThreadCount;
        private string schedulerName = "N/A";

        private long lastCpuTimestamp;
        private ulong lastBusyCpuNs;
        private readonly long openedAtTimestamp;

        // Hardware strings are calculated once. They never change while the OS runs.
        private readonly string baseSpeedText;
        private readonly string maxSpeedText;
        private readonly string cacheText;
        private readonly string signatureText;
        private readonly string topologyText;

        public TaskManagerModernApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Manager zadan")
        {
            applicationManager = manager;
            closeCallback = onClose;
            cpuInfo = CpuHardwareInfo.Detect();
            openedAtTimestamp = Stopwatch.GetTimestamp();

            baseSpeedText = FormatSpeedOnce(cpuInfo.BaseMHz);
            maxSpeedText = FormatSpeedOnce(cpuInfo.MaxMHz);
            cacheText = FormatCacheOnce(cpuInfo.L1Bytes) + " / " +
                        FormatCacheOnce(cpuInfo.L2Bytes) + " / " +
                        FormatCacheOnce(cpuInfo.L3Bytes);
            signatureText = cpuInfo.Family + " / " + cpuInfo.Model + " / " + cpuInfo.Stepping;
            topologyText = cpuInfo.PhysicalCores + " / " + cpuInfo.LogicalProcessors + " / " + cpuInfo.ThreadsPerCore;

            for (int i = 0; i < InitialRowPool; i++)
                rowPool.Add(new ModernTaskRow());

            Window = new Window(x, y, 980, 660, "Manager zadan");
            Window.CloseAction = Close;

            view = new TaskManagerModernView(10, 40, 960, 605, this);
            Window.AddChild(view);
            UpdateLayout();
            RefreshSnapshot();
        }

        public override void Update()
        {
            UpdateLayout();
            refreshFrame++;
            if (refreshFrame >= RefreshIntervalFrames)
            {
                refreshFrame = 0;
                RefreshSnapshot();
            }
        }

        private void UpdateLayout()
        {
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(650, Window.Width - 20);
            view.Height = Math.Max(390, Window.Height - 50);
            ClampScroll();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            if (!Window.Visible || !IsRunning)
                return;

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

            int action = view.ToolbarActionAt(x, y);
            if (action == 1)
            {
                RefreshSnapshot();
                status = "Lista odswiezona";
                return;
            }
            if (action == 2)
            {
                EndSelectedTask();
                return;
            }

            if (activePage == PagePerformance)
            {
                int resource = view.PerformanceResourceAt(x, y);
                if (resource >= PerfCpu && resource <= PerfSystem)
                {
                    performanceResource = resource;
                    status = resource == PerfCpu ? "Wydajnosc CPU" :
                             resource == PerfMemory ? "Wydajnosc pamieci" : "Stan systemu";
                }
                return;
            }

            int rowIndex = view.RowAt(x, y);
            if (rowIndex >= 0 && rowIndex < visibleRows.Count)
            {
                selectedIndex = rowIndex;
                EnsureSelectionVisible();
                ModernTaskRow row = visibleRows[rowIndex];
                status = row.Name;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshSnapshot();
                status = "Lista odswiezona";
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                if (activePage == PagePerformance && performanceResource > PerfCpu)
                    performanceResource--;
                else
                    SetPage(Math.Max(PageProcesses, activePage - 1));
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                if (activePage == PagePerformance && performanceResource < PerfSystem)
                    performanceResource++;
                else
                    SetPage(Math.Min(PageServices, activePage + 1));
                return;
            }

            if (activePage == PagePerformance)
                return;

            if (key.Key == ConsoleKeyEx.UpArrow)
                Select(-1);
            else if (key.Key == ConsoleKeyEx.DownArrow)
                Select(1);
            else if (key.Key == ConsoleKeyEx.Delete)
                EndSelectedTask();
        }

        private void SetPage(int page)
        {
            if (page < PageProcesses || page > PageServices || page == activePage)
                return;

            activePage = page;
            selectedIndex = -1;
            scrollIndex = 0;
            RebuildVisibleRows(null, -1);

            if (page == PageProcesses) status = "Procesy";
            else if (page == PagePerformance) status = "Wydajnosc";
            else if (page == PageDetails) status = "Szczegoly";
            else status = "Uslugi kernela";
        }

        private void Select(int delta)
        {
            if (visibleRows.Count == 0)
                return;

            if (selectedIndex < 0)
                selectedIndex = delta >= 0 ? 0 : visibleRows.Count - 1;
            else
                selectedIndex = Math.Max(0, Math.Min(visibleRows.Count - 1, selectedIndex + delta));

            EnsureSelectionVisible();
            status = visibleRows[selectedIndex].Name;
        }

        private void EndSelectedTask()
        {
            if (activePage == PagePerformance)
            {
                status = "Wybierz proces w Procesach, Szczegolach lub Uslugach";
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= visibleRows.Count)
            {
                status = "Wybierz zadanie";
                return;
            }

            ModernTaskRow row = visibleRows[selectedIndex];
            if (!row.CanEnd)
            {
                status = "To zadanie jest chronione";
                return;
            }

            if (row.GuiApplication != null)
            {
                if (row.GuiApplication == this)
                {
                    status = "Manager zadan zamknij przyciskiem X";
                    return;
                }

                row.GuiApplication.Close();
                status = "Zakonczono aplikacje";
                RefreshSnapshot();
                return;
            }

            if (row.KernelPid > 0)
            {
                bool requested = ProcessManager.Kill(row.KernelPid);
                status = requested ? "Wyslano zatrzymanie procesu" : "Nie mozna zatrzymac procesu";
                RefreshSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            Application selectedApp = null;
            int selectedPid = -1;
            if (selectedIndex >= 0 && selectedIndex < visibleRows.Count)
            {
                selectedApp = visibleRows[selectedIndex].GuiApplication;
                selectedPid = visibleRows[selectedIndex].KernelPid;
            }

            BuildRows();
            UpdateMemorySnapshot();
            UpdateCpuSnapshot();
            UpdateSystemSnapshot();
            RebuildVisibleRows(selectedApp, selectedPid);
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
            int slot = 0;
            guiAppCount = 0;
            Application active = applicationManager != null ? applicationManager.ActiveApplication : null;

            if (applicationManager != null)
            {
                List<Application> apps = applicationManager.Applications;
                for (int i = 0; i < apps.Count; i++)
                {
                    Application application = apps[i];
                    if (application == null || !application.IsRunning || application.Window == null)
                        continue;

                    string state = application.Window.IsMinimized
                        ? StateMinimized
                        : active == application ? StateActive : StateRunning;

                    ModernTaskRow row = AcquireRow(slot++);
                    row.SetApplication(application, state, application != this);
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
                row.SetKernel(process, process.IsRunning ? StateRunning : StateStopped,
                    protectedProcess ? DetailProtected : DetailKernel, !protectedProcess);
                allRows.Add(row);
            }
        }

        private void RebuildVisibleRows(Application selectedApp, int selectedPid)
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
            if (selectedApp != null || selectedPid > 0)
            {
                for (int i = 0; i < visibleRows.Count; i++)
                {
                    ModernTaskRow row = visibleRows[i];
                    if (selectedApp != null && row.GuiApplication == selectedApp)
                    {
                        selectedIndex = i;
                        break;
                    }
                    if (selectedPid > 0 && row.KernelPid == selectedPid)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }

            ClampScroll();
            EnsureSelectionVisible();
        }

        private void UpdateMemorySnapshot()
        {
            try
            {
                totalPages = PageAllocator.TotalPageCount;
                freePages = PageAllocator.FreePageCount;
                usedMemoryPercent = totalPages == 0 ? 0 : ((totalPages - freePages) * 100UL) / totalPages;
                if (usedMemoryPercent > 100) usedMemoryPercent = 100;

                if (CosmosGc.IsEnabled)
                {
                    gcHeapBytes = CosmosGc.GetHeapSizeBytes();
                    gcCommittedBytes = CosmosGc.GetTotalCommittedBytes();
                    gcFragmentedBytes = CosmosGc.GetFragmentedBytes();
                    gcPinnedObjects = CosmosGc.GetPinnedObjectsCount();
                    gcCollections = CosmosGc.GetCollectionIndex();
                }
                else
                {
                    gcHeapBytes = 0;
                    gcCommittedBytes = 0;
                    gcFragmentedBytes = 0;
                    gcPinnedObjects = 0;
                    gcCollections = 0;
                }
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
                usedMemoryPercent = 0;
                gcHeapBytes = 0;
                gcCommittedBytes = 0;
                gcFragmentedBytes = 0;
                gcPinnedObjects = 0;
                gcCollections = 0;
            }

            RecordHistory(memoryHistory, ref memoryHistoryCount, ref memoryHistoryWrite, (int)usedMemoryPercent);
        }

        private void UpdateCpuSnapshot()
        {
            UpdateSchedulerStats();

            if (!SchedulerManager.IsReady || Stopwatch.Frequency <= 0)
            {
                cpuUsagePercent = 0;
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, 0);
                return;
            }

            try
            {
                long now = Stopwatch.GetTimestamp();
                ulong busy = SchedulerManager.GetBusyCpuTimeNs();

                if (lastCpuTimestamp == 0)
                {
                    lastCpuTimestamp = now;
                    lastBusyCpuNs = busy;
                    RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, 0);
                    return;
                }

                long elapsedTicksSigned = now - lastCpuTimestamp;
                if (elapsedTicksSigned <= 0)
                    return;

                ulong elapsedNs = TicksToNanoseconds((ulong)elapsedTicksSigned, (ulong)Stopwatch.Frequency);
                if (elapsedNs < 50_000_000UL)
                    return;

                ulong cpuCount = Math.Max(1UL, (ulong)onlineCpuCount);
                ulong capacityNs = elapsedNs * cpuCount;
                ulong busyDelta = busy >= lastBusyCpuNs ? busy - lastBusyCpuNs : 0;
                ulong usage = capacityNs > 0 ? (busyDelta * 100UL) / capacityNs : 0;
                cpuUsagePercent = (int)Math.Min(100UL, usage);

                lastCpuTimestamp = now;
                lastBusyCpuNs = busy;
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, cpuUsagePercent);
            }
            catch
            {
            }
        }

        private void UpdateSchedulerStats()
        {
            onlineCpuCount = 0;
            schedulerThreadCount = 0;
            readyThreadCount = 0;
            runningThreadCount = 0;
            blockedThreadCount = 0;
            sleepingThreadCount = 0;
            schedulerName = "N/A";

            try
            {
                onlineCpuCount = SchedulerManager.CpuCount;
                if (SchedulerManager.Current != null && !string.IsNullOrEmpty(SchedulerManager.Current.Name))
                    schedulerName = SchedulerManager.Current.Name;

                SchedulerThread[] threads = SchedulerManager.Threads;
                if (threads == null)
                    return;

                for (int i = 0; i < threads.Length; i++)
                {
                    SchedulerThread thread = threads[i];
                    if (thread == null)
                        continue;

                    schedulerThreadCount++;
                    if (thread.State == SchedulerThreadState.Ready) readyThreadCount++;
                    else if (thread.State == SchedulerThreadState.Running) runningThreadCount++;
                    else if (thread.State == SchedulerThreadState.Blocked) blockedThreadCount++;
                    else if (thread.State == SchedulerThreadState.Sleeping) sleepingThreadCount++;
                }
            }
            catch
            {
            }
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
            if (frequency == 0)
                return 0;
            return (ticks / frequency) * 1_000_000_000UL +
                   ((ticks % frequency) * 1_000_000_000UL) / frequency;
        }

        private static void RecordHistory(int[] history, ref int count, ref int writeIndex, int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            history[writeIndex] = value;
            writeIndex = (writeIndex + 1) % history.Length;
            if (count < history.Length)
                count++;
        }

        internal int GetCpuHistory(int chronologicalIndex)
        {
            return GetHistory(cpuHistory, cpuHistoryCount, cpuHistoryWrite, chronologicalIndex);
        }

        internal int GetMemoryHistory(int chronologicalIndex)
        {
            return GetHistory(memoryHistory, memoryHistoryCount, memoryHistoryWrite, chronologicalIndex);
        }

        private static int GetHistory(int[] history, int count, int writeIndex, int chronologicalIndex)
        {
            if (chronologicalIndex < 0 || chronologicalIndex >= count)
                return 0;
            int start = count < history.Length ? 0 : writeIndex;
            return history[(start + chronologicalIndex) % history.Length];
        }

        private void EnsureSelectionVisible()
        {
            if (selectedIndex < 0 || activePage == PagePerformance)
                return;

            int visible = Math.Max(1, view.VisibleRows);
            if (selectedIndex < scrollIndex)
                scrollIndex = selectedIndex;
            else if (selectedIndex >= scrollIndex + visible)
                scrollIndex = selectedIndex - visible + 1;
            ClampScroll();
        }

        private void ClampScroll()
        {
            if (activePage == PagePerformance)
            {
                scrollIndex = 0;
                return;
            }

            int max = Math.Max(0, visibleRows.Count - Math.Max(1, view.VisibleRows));
            scrollIndex = Math.Max(0, Math.Min(scrollIndex, max));
        }

        internal void SetScrollIndex(int value)
        {
            int max = Math.Max(0, visibleRows.Count - Math.Max(1, view.VisibleRows));
            scrollIndex = Math.Max(0, Math.Min(value, max));
        }

        internal ulong UptimeSeconds
        {
            get
            {
                long frequency = Stopwatch.Frequency;
                if (frequency <= 0)
                    return 0;
                long now = Stopwatch.GetTimestamp();
                long elapsed = now - openedAtTimestamp;
                return elapsed > 0 ? (ulong)elapsed / (ulong)frequency : 0;
            }
        }

        private static string FormatSpeedOnce(int mhz)
        {
            if (mhz <= 0) return "N/A";
            if (mhz >= 1000) return (mhz / 1000) + "." + ((mhz % 1000) / 100) + " GHZ";
            return mhz + " MHZ";
        }

        private static string FormatCacheOnce(ulong bytes)
        {
            if (bytes == 0) return "N/A";
            if (bytes >= 1024UL * 1024UL)
            {
                ulong mb10 = bytes * 10UL / (1024UL * 1024UL);
                return (mb10 / 10UL) + "." + (mb10 % 10UL) + " MB";
            }
            return (bytes / 1024UL) + " KB";
        }

        public override void Close()
        {
            base.Close();
            if (closeCallback != null)
                closeCallback();
        }

        internal List<ModernTaskRow> Rows { get { return visibleRows; } }
        internal int ActivePage { get { return activePage; } }
        internal int PerformanceResource { get { return performanceResource; } }
        internal int SelectedIndex { get { return selectedIndex; } }
        internal int ScrollIndex { get { return scrollIndex; } }
        internal string Status { get { return status; } }
        internal int GuiAppCount { get { return guiAppCount; } }
        internal int KernelProcessCount { get { return kernelProcessCount; } }
        internal int CpuUsagePercent { get { return cpuUsagePercent; } }
        internal ulong TotalPages { get { return totalPages; } }
        internal ulong FreePages { get { return freePages; } }
        internal ulong UsedMemoryPercent { get { return usedMemoryPercent; } }
        internal ulong UsedMemoryMb { get { return (totalPages - Math.Min(totalPages, freePages)) * PageAllocator.PageSize / (1024UL * 1024UL); } }
        internal ulong TotalMemoryMb { get { return totalPages * PageAllocator.PageSize / (1024UL * 1024UL); } }
        internal ulong GcHeapMb { get { return gcHeapBytes / (1024UL * 1024UL); } }
        internal ulong GcCommittedMb { get { return gcCommittedBytes / (1024UL * 1024UL); } }
        internal ulong GcFragmentedKb { get { return gcFragmentedBytes / 1024UL; } }
        internal ulong GcPinnedObjects { get { return gcPinnedObjects; } }
        internal int GcCollections { get { return gcCollections; } }
        internal int StorageDeviceCount { get { return storageDeviceCount; } }
        internal int StoragePartitionCount { get { return storagePartitionCount; } }
        internal bool NetworkReady { get { return networkReady; } }
        internal uint OnlineCpuCount { get { return onlineCpuCount; } }
        internal int SchedulerThreadCount { get { return schedulerThreadCount; } }
        internal int ReadyThreadCount { get { return readyThreadCount; } }
        internal int RunningThreadCount { get { return runningThreadCount; } }
        internal int BlockedThreadCount { get { return blockedThreadCount; } }
        internal int SleepingThreadCount { get { return sleepingThreadCount; } }
        internal string SchedulerName { get { return schedulerName; } }
        internal CpuHardwareInfo CpuInfo { get { return cpuInfo; } }
        internal int CpuHistoryCount { get { return cpuHistoryCount; } }
        internal int MemoryHistoryCount { get { return memoryHistoryCount; } }
        internal int HistoryCapacity { get { return HistoryLength; } }
        internal string BaseSpeedText { get { return baseSpeedText; } }
        internal string MaxSpeedText { get { return maxSpeedText; } }
        internal string CacheText { get { return cacheText; } }
        internal string SignatureText { get { return signatureText; } }
        internal string TopologyText { get { return topologyText; } }
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

        public void SetApplication(Application application, string state, bool canEnd)
        {
            GuiApplication = application;
            KernelPid = -1;
            IsKernel = false;
            CanEnd = canEnd;
            Name = application == null || string.IsNullOrEmpty(application.Name) ? "Application" : application.Name;
            State = state;
            Detail = "GUI WINDOW";
            WindowWidth = application != null && application.Window != null ? application.Window.Width : 0;
            WindowHeight = application != null && application.Window != null ? application.Window.Height : 0;
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
        private const int HeaderHeight = 52;
        private const int SummaryTop = 58;
        private const int SummaryHeight = 48;
        private const int TableHeaderTop = 114;
        private const int TableHeaderHeight = 30;
        private const int ListTop = 144;
        private const int FooterHeight = 28;
        private const int RowHeight = 36;
        private const int ScrollReserve = 16;
        private const int NavTop = 58;
        private const int NavHeight = 42;
        private const int PerfCardHeight = 72;
        private const int PerfCardGap = 10;

        private static readonly Color Chrome = Color.FromArgb(29, 34, 40);
        private static readonly Color Sidebar = Color.FromArgb(24, 29, 35);
        private static readonly Color Border = Color.FromArgb(64, 76, 88);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color Text = Color.FromArgb(228, 234, 239);
        private static readonly Color Muted = Color.FromArgb(139, 154, 168);
        private static readonly string[] DigitStrings = { "0", "1", "2", "3", "4", "5", "6", "7", "8", "9" };

        private readonly TaskManagerModernApp app;
        private readonly ScrollBar scrollBar;
        private readonly Font font = PCScreenFont.DefaultFont;

        public TaskManagerModernView(int x, int y, int width, int height, TaskManagerModernApp owner)
            : base(x, y, width, height)
        {
            app = owner;
            scrollBar = new ScrollBar(0, 0, 10, 100);
            scrollBar.ValueChanged = delegate(int value) { app.SetScrollIndex(value); };
        }

        public int VisibleRows
        {
            get
            {
                int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
                return Math.Max(1, listHeight / RowHeight);
            }
        }

        public int NavAt(int x, int y)
        {
            if (x < 6 || x >= SidebarWidth - 6 || y < NavTop)
                return -1;
            int relative = y - NavTop;
            int page = relative / NavHeight;
            return page >= TaskManagerModernApp.PageProcesses && page <= TaskManagerModernApp.PageServices &&
                   relative < NavHeight * 4 ? page : -1;
        }

        public int ToolbarActionAt(int x, int y)
        {
            if (x < SidebarWidth || y < 10 || y >= 40)
                return 0;
            int refreshX = Width - 210;
            int endX = Width - 114;
            if (x >= refreshX && x < refreshX + 88) return 1;
            if (x >= endX && x < endX + 100) return 2;
            return 0;
        }

        public int PerformanceResourceAt(int x, int y)
        {
            if (app.ActivePage != TaskManagerModernApp.PagePerformance)
                return -1;
            int left = SidebarWidth + 10;
            int top = 62;
            int width = 154;
            if (x < left || x >= left + width || y < top)
                return -1;
            int relative = y - top;
            int stride = PerfCardHeight + PerfCardGap;
            int index = relative / stride;
            if (index < 0 || index > 2 || relative % stride >= PerfCardHeight)
                return -1;
            return index;
        }

        public int RowAt(int x, int y)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance)
                return -1;
            int contentX = SidebarWidth + 8;
            int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            if (x < contentX || x >= Width - ScrollReserve || y < ListTop || y >= ListTop + listHeight)
                return -1;
            int row = (y - ListTop) / RowHeight;
            int index = app.ScrollIndex + row;
            return index >= 0 && index < app.Rows.Count ? index : -1;
        }

        public bool HandleScrollMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance)
                return false;
            UpdateScrollBar();
            return scrollBar.HandleMouse(mouseX, mouseY, left, oldLeft);
        }

        private void UpdateScrollBar()
        {
            int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            scrollBar.X = X + Width - 15;
            scrollBar.Y = Y + ListTop + 2;
            scrollBar.Width = 10;
            scrollBar.Height = Math.Max(24, listHeight - 4);
            scrollBar.SetRange(app.Rows.Count, VisibleRows);
            scrollBar.Value = app.ScrollIndex;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            canvas.DrawFilledRectangle(Chrome, X, Y, Width, Height);
            canvas.DrawRectangle(Border, X, Y, Width, Height);
            RenderSidebar(canvas);
            RenderHeader(canvas);

            if (app.ActivePage == TaskManagerModernApp.PagePerformance)
                RenderPerformance(canvas);
            else
                RenderProcessPage(canvas);

            RenderFooter(canvas);
        }

        private void RenderSidebar(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Sidebar, X + 1, Y + 1, SidebarWidth - 1, Height - 2);
            canvas.DrawLine(Color.FromArgb(52, 62, 72), X + SidebarWidth, Y + 1, X + SidebarWidth, Y + Height - 2);
            IconManager.DrawScaled(canvas, IconType.Settings, X + 16, Y + 16, 20, 20);
            SmallTextRenderer.Draw(canvas, "TASK MANAGER", X + 46, Y + 23, Text);

            DrawNavItem(canvas, TaskManagerModernApp.PageProcesses, "Procesy", IconType.FileManager);
            DrawNavItem(canvas, TaskManagerModernApp.PagePerformance, "Wydajnosc", IconType.Settings);
            DrawNavItem(canvas, TaskManagerModernApp.PageDetails, "Szczegoly", IconType.File);
            DrawNavItem(canvas, TaskManagerModernApp.PageServices, "Uslugi", IconType.Settings);

            int bottomY = Y + Height - 44;
            canvas.DrawLine(Color.FromArgb(48, 58, 68), X + 10, bottomY - 8, X + SidebarWidth - 10, bottomY - 8);
            IconManager.DrawScaled(canvas, IconType.About, X + 17, bottomY, 16, 16);
            SmallTextRenderer.Draw(canvas, "ZONDERQOS", X + 44, bottomY + 5, Muted);
        }

        private void DrawNavItem(Canvas canvas, int page, string label, IconType icon)
        {
            int y = Y + NavTop + page * NavHeight;
            bool active = app.ActivePage == page;
            if (active)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(38, 55, 70), X + 8, y + 2, SidebarWidth - 16, NavHeight - 4);
                canvas.DrawFilledRectangle(Accent, X + 8, y + 7, 3, NavHeight - 14);
            }
            canvas.DrawFilledRectangle(Color.FromArgb(31, 38, 45), X + 18, y + 10, 24, 24);
            IconManager.DrawScaled(canvas, icon, X + 21, y + 13, 18, 18);
            SmallTextRenderer.Draw(canvas, label, X + 52, y + 18, active ? Color.WhiteSmoke : Color.FromArgb(196, 205, 213));
        }

        private void RenderHeader(Canvas canvas)
        {
            int contentX = X + SidebarWidth + 8;
            int contentW = Width - SidebarWidth - 12;
            string title = app.ActivePage == TaskManagerModernApp.PageProcesses ? "Procesy" :
                           app.ActivePage == TaskManagerModernApp.PagePerformance ? "Wydajnosc" :
                           app.ActivePage == TaskManagerModernApp.PageDetails ? "Szczegoly" : "Uslugi";
            SmallTextRenderer.Draw(canvas, title, contentX + 4, Y + 21, Color.WhiteSmoke);

            int refreshX = X + Width - 210;
            int endX = X + Width - 114;
            DrawButton(canvas, refreshX, Y + 10, 88, "REFRESH", IconType.Refresh, false, true);
            DrawButton(canvas, endX, Y + 10, 100, "END TASK", IconType.Close, true,
                app.ActivePage != TaskManagerModernApp.PagePerformance);
            canvas.DrawLine(Color.FromArgb(52, 63, 74), contentX, Y + HeaderHeight, contentX + contentW, Y + HeaderHeight);
        }

        private void DrawButton(Canvas canvas, int x, int y, int width, string label, IconType icon, bool danger, bool enabled)
        {
            Color background = !enabled ? Color.FromArgb(35, 40, 46) : danger ? Color.FromArgb(63, 42, 47) : Color.FromArgb(47, 55, 64);
            Color border = !enabled ? Color.FromArgb(54, 62, 70) : danger ? Color.FromArgb(111, 65, 73) : Color.FromArgb(80, 94, 108);
            Color labelColor = enabled ? Color.WhiteSmoke : Color.FromArgb(112, 122, 132);
            canvas.DrawFilledRectangle(background, x, y, width, 30);
            canvas.DrawRectangle(border, x, y, width, 30);
            IconManager.DrawScaled(canvas, icon, x + 7, y + 7, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 29, y + 12, Math.Max(10, width - 34), labelColor);
        }

        private void RenderProcessPage(Canvas canvas)
        {
            int contentX = X + SidebarWidth + 8;
            int contentW = Width - SidebarWidth - 12;
            RenderSummary(canvas, contentX, contentW);
            RenderTableHeader(canvas, contentX, contentW);
            RenderRows(canvas, contentX, contentW);
        }

        private void RenderSummary(Canvas canvas, int x, int width)
        {
            int gap = 8;
            int cardW = Math.Max(86, (width - gap * 3) / 4);
            DrawSummaryNumber(canvas, x, cardW, "APPS", (ulong)app.GuiAppCount, "APPLICATIONS", false, false);
            DrawSummaryNumber(canvas, x + cardW + gap, cardW, "BACKGROUND", (ulong)app.KernelProcessCount, "KERNEL", false, false);
            DrawSummaryNumber(canvas, x + (cardW + gap) * 2, cardW, "CPU", (ulong)app.CpuUsagePercent, "TOTAL USAGE", true, true);
            DrawSummaryNumber(canvas, x + (cardW + gap) * 3, cardW, "MEMORY", app.UsedMemoryPercent, "PHYSICAL", false, true);
        }

        private void DrawSummaryNumber(Canvas canvas, int x, int width, string label, ulong value, string detail, bool accent, bool percent)
        {
            int y = Y + SummaryTop;
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, SummaryHeight);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, SummaryHeight);
            if (accent) canvas.DrawFilledRectangle(Accent, x, y, 3, SummaryHeight);
            SmallTextRenderer.Draw(canvas, label, x + 10, y + 12, Muted);
            SmallTextRenderer.DrawUInt(canvas, value, x + 10, y + 27, accent ? Color.FromArgb(137, 194, 233) : Text);
            if (percent)
                SmallTextRenderer.Draw(canvas, "%", x + 12 + SmallTextRenderer.WidthUInt(value), y + 27, accent ? Color.FromArgb(137, 194, 233) : Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 66, y + 27, Math.Max(20, width - 74), Muted);
        }

        private void RenderTableHeader(Canvas canvas, int x, int width)
        {
            int y = Y + TableHeaderTop;
            canvas.DrawFilledRectangle(Color.FromArgb(33, 39, 46), x, y, width, TableHeaderHeight);
            canvas.DrawLine(Color.FromArgb(57, 68, 79), x, y + TableHeaderHeight - 1, x + width, y + TableHeaderHeight - 1);

            if (app.ActivePage == TaskManagerModernApp.PageDetails)
            {
                SmallTextRenderer.Draw(canvas, "PID", x + 12, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "NAME", x + 74, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(320, width - 260), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "DETAIL", x + Math.Max(410, width - 154), y + 11, Muted);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, "NAME", x + 12, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "TYPE", x + Math.Max(280, width - 350), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(370, width - 240), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "ID", x + Math.Max(470, width - 108), y + 11, Muted);
            }
        }

        private void RenderRows(Canvas canvas, int contentX, int contentW)
        {
            int listY = Y + ListTop;
            int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            int rowWidth = Math.Max(80, contentW - ScrollReserve);
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), contentX, listY, contentW, listHeight);
            canvas.DrawRectangle(Color.FromArgb(50, 60, 70), contentX, listY, contentW, listHeight);

            int visible = VisibleRows;
            for (int rowIndex = 0; rowIndex < visible; rowIndex++)
            {
                int index = app.ScrollIndex + rowIndex;
                if (index >= app.Rows.Count) break;

                ModernTaskRow row = app.Rows[index];
                int y = listY + rowIndex * RowHeight;
                bool selected = index == app.SelectedIndex;
                if (selected)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(38, 62, 82), contentX + 2, y + 1, rowWidth - 2, RowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, contentX + 2, y + 1, 3, RowHeight - 2);
                }
                else if ((rowIndex & 1) != 0)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(27, 33, 39), contentX + 2, y + 1, rowWidth - 2, RowHeight - 2);
                }

                if (app.ActivePage == TaskManagerModernApp.PageDetails)
                    DrawDetailsRow(canvas, row, contentX, rowWidth, y, selected);
                else
                    DrawProcessRow(canvas, row, contentX, rowWidth, y, selected);
            }

            if (app.Rows.Count == 0)
                SmallTextRenderer.Draw(canvas, "BRAK AKTYWNYCH ZADAN", contentX + 16, listY + 18, Muted);

            UpdateScrollBar();
            scrollBar.Render(canvas);
        }

        private void DrawProcessRow(Canvas canvas, ModernTaskRow row, int x, int width, int y, bool selected)
        {
            IconType icon = row.IsKernel ? IconType.Settings : GetApplicationIcon(row.GuiApplication);
            canvas.DrawFilledRectangle(Color.FromArgb(34, 41, 48), x + 10, y + 6, 24, 24);
            IconManager.DrawScaled(canvas, icon, x + 13, y + 9, 18, 18);

            int typeX = x + Math.Max(280, width - 350);
            int statusX = x + Math.Max(370, width - 240);
            int idX = x + Math.Max(470, width - 108);
            int nameW = Math.Max(40, typeX - x - 54);
            SmallTextRenderer.DrawClipped(canvas, row.Name, x + 44, y + 14, nameW, selected ? Color.WhiteSmoke : Text);
            SmallTextRenderer.Draw(canvas, row.IsKernel ? "KERNEL" : "APP", typeX, y + 14, row.IsKernel ? Color.FromArgb(154, 176, 195) : Color.FromArgb(124, 186, 229));
            SmallTextRenderer.DrawClipped(canvas, row.State, statusX, y + 14, 86, row.State == "ACTIVE" ? Color.FromArgb(111, 194, 145) : Muted);
            if (row.KernelPid > 0)
                SmallTextRenderer.DrawUInt(canvas, (ulong)row.KernelPid, idX, y + 14, Muted);
            else
                SmallTextRenderer.Draw(canvas, "GUI", idX, y + 14, Muted);
            if (!row.CanEnd)
                SmallTextRenderer.Draw(canvas, "LOCK", x + width - 46, y + 25, Color.FromArgb(184, 126, 133));
        }

        private void DrawDetailsRow(Canvas canvas, ModernTaskRow row, int x, int width, int y, bool selected)
        {
            int stateX = x + Math.Max(320, width - 260);
            int detailX = x + Math.Max(410, width - 154);
            int nameW = Math.Max(40, stateX - x - 82);
            if (row.KernelPid > 0)
                SmallTextRenderer.DrawUInt(canvas, (ulong)row.KernelPid, x + 12, y + 14, Muted);
            else
                SmallTextRenderer.Draw(canvas, "GUI", x + 12, y + 14, Muted);
            SmallTextRenderer.DrawClipped(canvas, row.Name, x + 74, y + 14, nameW, selected ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawClipped(canvas, row.State, stateX, y + 14, 82, row.State == "ACTIVE" ? Color.FromArgb(111, 194, 145) : Muted);

            if (row.IsKernel)
            {
                SmallTextRenderer.DrawClipped(canvas, row.Detail, detailX, y + 14, Math.Max(30, width - (detailX - x) - 8), Muted);
            }
            else
            {
                SmallTextRenderer.DrawUInt(canvas, (ulong)Math.Max(0, row.WindowWidth), detailX, y + 14, Muted);
                int px = detailX + SmallTextRenderer.WidthUInt((ulong)Math.Max(0, row.WindowWidth)) + 6;
                SmallTextRenderer.Draw(canvas, "X", px, y + 14, Muted);
                SmallTextRenderer.DrawUInt(canvas, (ulong)Math.Max(0, row.WindowHeight), px + 10, y + 14, Muted);
            }
        }

        private void RenderPerformance(Canvas canvas)
        {
            int resourceX = X + SidebarWidth + 10;
            int resourceY = Y + 62;
            int resourceW = 154;
            DrawPerformanceResource(canvas, resourceX, resourceY, resourceW, TaskManagerModernApp.PerfCpu, "CPU", (ulong)app.CpuUsagePercent, "%", app.BaseSpeedText);
            DrawPerformanceResource(canvas, resourceX, resourceY + PerfCardHeight + PerfCardGap, resourceW, TaskManagerModernApp.PerfMemory, "MEMORY", app.UsedMemoryPercent, "%", "PHYSICAL RAM");
            DrawPerformanceResource(canvas, resourceX, resourceY + (PerfCardHeight + PerfCardGap) * 2, resourceW, TaskManagerModernApp.PerfSystem, "SYSTEM", (ulong)app.SchedulerThreadCount, "", "THREADS");

            int x = resourceX + resourceW + 16;
            int y = resourceY;
            int width = Math.Max(330, Width - (x - X) - 10);
            if (app.PerformanceResource == TaskManagerModernApp.PerfCpu)
                RenderCpuPerformance(canvas, x, y, width);
            else if (app.PerformanceResource == TaskManagerModernApp.PerfMemory)
                RenderMemoryPerformance(canvas, x, y, width);
            else
                RenderSystemPerformance(canvas, x, y, width);
        }

        private void DrawPerformanceResource(Canvas canvas, int x, int y, int width, int resource, string title, ulong value, string suffix, string detail)
        {
            bool selected = app.PerformanceResource == resource;
            Color background = selected ? Color.FromArgb(35, 55, 71) : Color.FromArgb(26, 32, 38);
            Color border = selected ? Color.FromArgb(65, 126, 169) : Color.FromArgb(52, 63, 74);
            canvas.DrawFilledRectangle(background, x, y, width, PerfCardHeight);
            canvas.DrawRectangle(border, x, y, width, PerfCardHeight);
            if (selected) canvas.DrawFilledRectangle(Accent, x, y, 3, PerfCardHeight);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 13, selected ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawUInt(canvas, value, x + 12, y + 31, selected ? Color.FromArgb(126, 194, 238) : Color.FromArgb(190, 201, 211));
            if (!string.IsNullOrEmpty(suffix))
                SmallTextRenderer.Draw(canvas, suffix, x + 14 + SmallTextRenderer.WidthUInt(value), y + 31, selected ? Color.FromArgb(126, 194, 238) : Color.FromArgb(190, 201, 211));
            SmallTextRenderer.DrawClipped(canvas, detail, x + 12, y + 50, width - 22, Muted);
        }

        private void RenderCpuPerformance(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "CPU", x, y + 4, Color.WhiteSmoke);
            SmallTextRenderer.DrawClipped(canvas, app.CpuInfo.Brand, x + 44, y + 4, Math.Max(50, width - 48), Color.FromArgb(190, 201, 211));
            SmallTextRenderer.Draw(canvas, "% UTILIZATION", x, y + 24, Muted);
            int graphY = y + 40;
            int graphHeight = Math.Max(150, Height - 350);
            DrawGraph(canvas, x, graphY, width, graphHeight, app.CpuHistoryCount, app.GetCpuHistory, Accent);

            int statsY = graphY + graphHeight + 20;
            DrawLargePercent(canvas, app.CpuUsagePercent, x, statsY, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, "UTILIZATION", x, statsY + 36, Muted);

            int leftX = x + 124;
            int rightX = x + Math.Max(330, width / 2 + 70);
            int rightW = Math.Max(160, width - (rightX - x));
            DrawKeyText(canvas, leftX, statsY + 2, 190, "BASE SPEED", app.BaseSpeedText);
            DrawKeyText(canvas, leftX, statsY + 20, 190, "MAX SPEED", app.MaxSpeedText);
            DrawKeyUInt(canvas, leftX, statsY + 38, 190, "THREADS", (ulong)app.SchedulerThreadCount, null);
            DrawKeyUInt(canvas, leftX, statsY + 56, 190, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, leftX, statsY + 74, 190, "SCHEDULER", app.SchedulerName);

            DrawKeyText(canvas, rightX, statsY + 2, rightW, "VENDOR", app.CpuInfo.Vendor);
            DrawKeyText(canvas, rightX, statsY + 20, rightW, "FAMILY/MODEL/STEP", app.SignatureText);
            DrawKeyText(canvas, rightX, statsY + 38, rightW, "CORES/LOGICAL/TPC", app.TopologyText);
            DrawKeyText(canvas, rightX, statsY + 56, rightW, "CACHE L1/L2/L3", app.CacheText);
            DrawKeyText(canvas, rightX, statsY + 74, rightW, "VIRTUALIZATION", app.CpuInfo.VirtualizationSupported ? "SUPPORTED" : "NO");
            DrawKeyText(canvas, rightX, statsY + 92, rightW, "HYPERVISOR", app.CpuInfo.HypervisorPresent ? "PRESENT" : "NO");
            SmallTextRenderer.Draw(canvas, "FEATURES", x, statsY + 118, Muted);
            SmallTextRenderer.DrawClipped(canvas, app.CpuInfo.Features, x + 58, statsY + 118, Math.Max(40, width - 62), Color.FromArgb(185, 199, 211));
        }

        private void RenderMemoryPerformance(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "MEMORY", x, y + 4, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, "% PHYSICAL MEMORY IN USE", x, y + 24, Muted);
            int graphY = y + 40;
            int graphHeight = Math.Max(150, Height - 350);
            DrawGraph(canvas, x, graphY, width, graphHeight, app.MemoryHistoryCount, app.GetMemoryHistory, Color.FromArgb(116, 174, 219));

            int statsY = graphY + graphHeight + 20;
            DrawLargePercent(canvas, (int)app.UsedMemoryPercent, x, statsY, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, "IN USE", x, statsY + 36, Muted);

            int leftX = x + 124;
            int rightX = x + Math.Max(340, width / 2 + 70);
            int rightW = Math.Max(160, width - (rightX - x));
            DrawKeyUInt(canvas, leftX, statsY + 2, 200, "USED", app.UsedMemoryMb, "MB");
            DrawKeyUInt(canvas, leftX, statsY + 20, 200, "TOTAL", app.TotalMemoryMb, "MB");
            DrawKeyUInt(canvas, leftX, statsY + 38, 200, "FREE PAGES", app.FreePages, null);
            DrawKeyUInt(canvas, leftX, statsY + 56, 200, "TOTAL PAGES", app.TotalPages, null);

            DrawKeyUInt(canvas, rightX, statsY + 2, rightW, "GC HEAP", app.GcHeapMb, "MB");
            DrawKeyUInt(canvas, rightX, statsY + 20, rightW, "GC COMMITTED", app.GcCommittedMb, "MB");
            DrawKeyUInt(canvas, rightX, statsY + 38, rightW, "FRAGMENTED", app.GcFragmentedKb, "KB");
            DrawKeyUInt(canvas, rightX, statsY + 56, rightW, "PINNED", app.GcPinnedObjects, null);
            DrawKeyUInt(canvas, rightX, statsY + 74, rightW, "COLLECTIONS", (ulong)Math.Max(0, app.GcCollections), null);
            DrawKeyText(canvas, rightX, statsY + 92, rightW, "COLLECTOR", CosmosGc.IsEnabled ? "ORION GC ACTIVE" : "GC OFF");
        }

        private void RenderSystemPerformance(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "SYSTEM", x, y + 4, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, "KERNEL AND HARDWARE OVERVIEW", x, y + 24, Muted);

            int cardY = y + 48;
            int gap = 10;
            int cardW = Math.Max(120, (width - gap * 2) / 3);
            DrawSystemCard(canvas, x, cardY, cardW, "GUI APPS", (ulong)app.GuiAppCount, "RUNNING WINDOWS");
            DrawSystemCard(canvas, x + cardW + gap, cardY, cardW, "KERNEL PROCS", (ulong)app.KernelProcessCount, "BACKGROUND");
            DrawSystemCard(canvas, x + (cardW + gap) * 2, cardY, cardW, "THREADS", (ulong)app.SchedulerThreadCount, "SCHEDULER");

            int lineY = cardY + 86;
            DrawKeyUInt(canvas, x, lineY, width / 2 - 8, "RUNNING THREADS", (ulong)app.RunningThreadCount, null);
            DrawKeyUInt(canvas, x, lineY + 20, width / 2 - 8, "READY THREADS", (ulong)app.ReadyThreadCount, null);
            DrawKeyUInt(canvas, x, lineY + 40, width / 2 - 8, "BLOCKED THREADS", (ulong)app.BlockedThreadCount, null);
            DrawKeyUInt(canvas, x, lineY + 60, width / 2 - 8, "SLEEPING THREADS", (ulong)app.SleepingThreadCount, null);
            DrawKeyUInt(canvas, x, lineY + 80, width / 2 - 8, "UPTIME", app.UptimeSeconds, "SEC");

            int rightX = x + width / 2 + 8;
            int rightW = width - width / 2 - 8;
            DrawKeyUInt(canvas, rightX, lineY, rightW, "STORAGE DEVICES", (ulong)Math.Max(0, app.StorageDeviceCount), null);
            DrawKeyUInt(canvas, rightX, lineY + 20, rightW, "PARTITIONS", (ulong)Math.Max(0, app.StoragePartitionCount), null);
            DrawKeyText(canvas, rightX, lineY + 40, rightW, "NETWORK", app.NetworkReady ? "READY" : "OFFLINE");
            DrawKeyUInt(canvas, rightX, lineY + 60, rightW, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, rightX, lineY + 80, rightW, "ARCHITECTURE", app.CpuInfo.Architecture);

            int noteY = lineY + 124;
            canvas.DrawFilledRectangle(Color.FromArgb(25, 31, 37), x, noteY, width, 58);
            canvas.DrawRectangle(Color.FromArgb(54, 66, 77), x, noteY, width, 58);
            SmallTextRenderer.Draw(canvas, "MEMORY STABILITY MODE", x + 12, noteY + 14, Color.FromArgb(129, 193, 235));
            SmallTextRenderer.DrawClipped(canvas, "REUSED SNAPSHOTS - FIXED HISTORY - NO PER-FRAME FORMAT STRINGS", x + 12, noteY + 33, width - 24, Muted);
        }

        private void DrawSystemCard(Canvas canvas, int x, int y, int width, string title, ulong value, string detail)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, 68);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, 68);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 13, Muted);
            SmallTextRenderer.DrawUInt(canvas, value, x + 12, y + 31, Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 12, y + 49, width - 24, Muted);
        }

        private delegate int HistoryGetter(int index);

        private void DrawGraph(Canvas canvas, int x, int y, int width, int height, int count, HistoryGetter getter, Color lineColor)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x, y, width, height);
            canvas.DrawRectangle(Color.FromArgb(66, 111, 145), x, y, width, height);
            for (int i = 1; i < 4; i++)
            {
                int gy = y + i * height / 4;
                canvas.DrawLine(Color.FromArgb(39, 52, 63), x + 1, gy, x + width - 1, gy);
            }
            for (int i = 1; i < 6; i++)
            {
                int gx = x + i * width / 6;
                canvas.DrawLine(Color.FromArgb(35, 47, 58), gx, y + 1, gx, y + height - 1);
            }

            if (count > 1)
            {
                int denominator = Math.Max(1, app.HistoryCapacity - 1);
                int startOffset = app.HistoryCapacity - count;
                int previousX = x + startOffset * width / denominator;
                int previousY = y + height - getter(0) * height / 100;
                for (int i = 1; i < count; i++)
                {
                    int px = x + (startOffset + i) * width / denominator;
                    int py = y + height - getter(i) * height / 100;
                    canvas.DrawLine(lineColor, previousX, previousY, px, py);
                    previousX = px;
                    previousY = py;
                }
            }

            SmallTextRenderer.Draw(canvas, "100%", x + 6, y + 7, Color.FromArgb(102, 122, 139));
            SmallTextRenderer.Draw(canvas, "0%", x + 6, y + height - 12, Color.FromArgb(102, 122, 139));
            SmallTextRenderer.Draw(canvas, "RECENT HISTORY", x + 8, y + height + 7, Muted);
        }

        private void DrawLargePercent(Canvas canvas, int value, int x, int y, Color color)
        {
            value = Math.Max(0, Math.Min(100, value));
            int charWidth = Math.Max(8, font.Width);
            int cursor = x;
            if (value == 100)
            {
                canvas.DrawString(DigitStrings[1], font, color, cursor, y); cursor += charWidth;
                canvas.DrawString(DigitStrings[0], font, color, cursor, y); cursor += charWidth;
                canvas.DrawString(DigitStrings[0], font, color, cursor, y); cursor += charWidth;
            }
            else if (value >= 10)
            {
                canvas.DrawString(DigitStrings[value / 10], font, color, cursor, y); cursor += charWidth;
                canvas.DrawString(DigitStrings[value % 10], font, color, cursor, y); cursor += charWidth;
            }
            else
            {
                canvas.DrawString(DigitStrings[value], font, color, cursor, y); cursor += charWidth;
            }
            canvas.DrawString("%", font, color, cursor, y);
        }

        private void DrawKeyText(Canvas canvas, int x, int y, int width, string key, string value)
        {
            int keyWidth = Math.Min(132, Math.Max(76, width / 2));
            SmallTextRenderer.DrawClipped(canvas, key, x, y, keyWidth - 4, Muted);
            SmallTextRenderer.DrawClipped(canvas, value, x + keyWidth, y, Math.Max(20, width - keyWidth), Text);
        }

        private void DrawKeyUInt(Canvas canvas, int x, int y, int width, string key, ulong value, string suffix)
        {
            int keyWidth = Math.Min(132, Math.Max(76, width / 2));
            SmallTextRenderer.DrawClipped(canvas, key, x, y, keyWidth - 4, Muted);
            int valueX = x + keyWidth;
            SmallTextRenderer.DrawUInt(canvas, value, valueX, y, Text);
            if (!string.IsNullOrEmpty(suffix))
                SmallTextRenderer.Draw(canvas, suffix, valueX + SmallTextRenderer.WidthUInt(value) + 6, y, Text);
        }

        private IconType GetApplicationIcon(Application application)
        {
            if (application == null || string.IsNullOrEmpty(application.Name)) return IconType.File;
            string name = application.Name;
            if (name.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0) return IconType.Terminal;
            if (name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0) return IconType.Folder;
            if (name.IndexOf("notat", StringComparison.OrdinalIgnoreCase) >= 0) return IconType.File;
            if (name.IndexOf("diagn", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("about", StringComparison.OrdinalIgnoreCase) >= 0) return IconType.About;
            return IconType.Settings;
        }

        private void RenderFooter(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int y = Y + Height - FooterHeight;
            int width = Width - SidebarWidth - 12;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x, y, width, FooterHeight - 4);
            canvas.DrawLine(Color.FromArgb(58, 69, 80), x, y, x + width, y);
            SmallTextRenderer.DrawClipped(canvas, app.Status, x + 8, y + 9, Math.Max(20, width - 220), Color.FromArgb(184, 195, 205));
            SmallTextRenderer.Draw(canvas, "F5 REFRESH   DEL END TASK", x + width - 174, y + 9, Muted);
        }
    }
}
