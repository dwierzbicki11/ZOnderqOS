using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

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

        private const int RefreshIntervalFrames = 30;
        private const int HistoryLength = 120;
        private const int LogicalPerPage = 16;
        private const int InitialRowPool = 32;

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

        private int activePage = PageProcesses;
        private int performanceResource = PerfCpu;
        private int cpuGraphMode = CpuGraphTotal;
        private int logicalPage;
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
        private string clockText = "--:--:--";
        private string dateText = "--.--.----";

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

            baseSpeedText = FormatSpeed(cpuInfo.BaseMHz);
            maxSpeedText = FormatSpeed(cpuInfo.MaxMHz);
            topologyText = Math.Max(1, cpuInfo.PhysicalCores) + " / " +
                           Math.Max(1, cpuInfo.LogicalProcessors) + " / " +
                           Math.Max(1, cpuInfo.ThreadsPerCore);
            cacheText = FormatBytes(cpuInfo.L1Bytes) + " / " + FormatBytes(cpuInfo.L2Bytes) + " / " + FormatBytes(cpuInfo.L3Bytes);
            signatureText = cpuInfo.Family + " / " + cpuInfo.Model + " / " + cpuInfo.Stepping;

            for (int i = 0; i < InitialRowPool; i++)
                rowPool.Add(new ModernTaskRow());

            EnsureLogicalCapacity(Math.Max(1, cpuInfo.LogicalProcessors));
            UpdateClock();

            Window = new Window(x, y, 1040, 690, "Manager zadan");
            Window.CloseAction = Close;
            view = new TaskManagerModernView(10, 40, 1020, 635, this);
            Window.AddChild(view);

            UpdateLayout();
            RefreshSnapshot();
        }

        public override void Update()
        {
            UpdateLayout();
            UpdateClock();
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
            view.Width = Math.Max(700, Window.Width - 20);
            view.Height = Math.Max(430, Window.Height - 50);
            ClampScroll();
            ClampLogicalPage();
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
                clockText = D2(now.Hour) + ":" + D2(now.Minute) + ":" + D2(now.Second);
                dateText = D2(now.Day) + "." + D2(now.Month) + "." + D4(now.Year);
            }
            catch
            {
                clockText = "--:--:--";
                dateText = "--.--.----";
            }
        }

        private static string D2(int value) => value < 10 ? "0" + Math.Max(0, value) : value.ToString();
        private static string D4(int value)
        {
            value = Math.Max(0, value);
            if (value < 10) return "000" + value;
            if (value < 100) return "00" + value;
            if (value < 1000) return "0" + value;
            return value.ToString();
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
                    performanceResource = resource;
                    return;
                }

                if (performanceResource == PerfCpu)
                {
                    int graph = view.CpuGraphModeAt(x, y);
                    if (graph >= 0)
                    {
                        cpuGraphMode = graph;
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
            if (activePage == PagePerformance)
                return;
            if (key.Key == ConsoleKeyEx.UpArrow) Select(-1);
            else if (key.Key == ConsoleKeyEx.DownArrow) Select(1);
            else if (key.Key == ConsoleKeyEx.Delete) EndSelectedTask();
        }

        private void SetPage(int page)
        {
            if (page < PageProcesses || page > PageServices || page == activePage)
                return;
            activePage = page;
            selectedIndex = -1;
            scrollIndex = 0;
            RebuildVisibleRows(null, -1);
            status = page == PageProcesses ? "Procesy" : page == PagePerformance ? "Wydajnosc" : page == PageDetails ? "Szczegoly" : "Uslugi";
        }

        private void Select(int delta)
        {
            if (visibleRows.Count == 0)
                return;
            selectedIndex = selectedIndex < 0
                ? (delta >= 0 ? 0 : visibleRows.Count - 1)
                : Math.Max(0, Math.Min(visibleRows.Count - 1, selectedIndex + delta));
            EnsureSelectionVisible();
            status = visibleRows[selectedIndex].Name;
        }

        private void EndSelectedTask()
        {
            if (activePage == PagePerformance || selectedIndex < 0 || selectedIndex >= visibleRows.Count)
            {
                status = "Wybierz zadanie";
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
            EnsureSelectionVisible();
        }

        private void UpdateMemorySnapshot()
        {
            try
            {
                totalPages = PageAllocator.TotalPageCount;
                freePages = Math.Min(PageAllocator.FreePageCount, totalPages);
                ulong usedPages = totalPages - freePages;
                usedMemoryPercent = totalPages == 0 ? 0 : usedPages * 100UL / totalPages;
                usedMemoryPercent = Math.Min(100UL, usedMemoryPercent);

                gcHeapBytes = 0;
                gcCommittedBytes = 0;
                if (CosmosGc.IsEnabled)
                {
                    gcHeapBytes = CosmosGc.GetHeapSizeBytes();
                    gcCommittedBytes = CosmosGc.GetTotalCommittedBytes();
                }
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
                usedMemoryPercent = 0;
                gcHeapBytes = 0;
                gcCommittedBytes = 0;
            }
            RecordHistory(memoryHistory, ref memoryHistoryCount, ref memoryHistoryWrite, (int)usedMemoryPercent);
        }

        private void UpdateCpuSnapshot()
        {
            uint cpuCount = 0;
            int slots = 0;
            ulong busyTotal = 0;
            try
            {
                cpuCount = SchedulerInfo.CpuCount;
                slots = SchedulerInfo.ThreadSlotCount;
                busyTotal = SchedulerInfo.BusyCpuTimeNs;
                schedulerName = string.IsNullOrEmpty(SchedulerInfo.SchedulerName) ? "N/A" : SchedulerInfo.SchedulerName;
                schedulerTickNs = SchedulerInfo.TickPeriodNs;
            }
            catch
            {
                schedulerName = "N/A";
                schedulerTickNs = 0;
            }

            onlineCpuCount = cpuCount;
            EnsureLogicalCapacity(Math.Max(1, Math.Max(cpuInfo.LogicalProcessors, (int)cpuCount)));
            EnsureThreadSlotCapacity(Math.Max(0, slots));

            schedulerThreadCount = 0;
            runningThreadCount = 0;
            readyThreadCount = 0;
            blockedThreadCount = 0;
            sleepingThreadCount = 0;
            Array.Clear(logicalBusyDelta, 0, logicalBusyDelta.Length);

            long now = Stopwatch.GetTimestamp();
            long elapsedTicks = lastCpuTimestamp == 0 ? 0 : now - lastCpuTimestamp;
            ulong elapsedNs = elapsedTicks > 0 && Stopwatch.Frequency > 0
                ? TicksToNanoseconds((ulong)elapsedTicks, (ulong)Stopwatch.Frequency)
                : 0;
            bool validWindow = lastCpuTimestamp != 0 && elapsedNs >= 1_000_000UL;

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
                if (validWindow && previousThreadValid[slot] && previousThreadIds[slot] == info.Id && info.TotalRuntimeNs >= previousThreadRuntime[slot])
                    delta = info.TotalRuntimeNs - previousThreadRuntime[slot];

                if (!info.IsIdle && info.CpuId < (uint)logicalBusyDelta.Length)
                    logicalBusyDelta[info.CpuId] += delta;

                previousThreadIds[slot] = info.Id;
                previousThreadRuntime[slot] = info.TotalRuntimeNs;
                previousThreadValid[slot] = true;
            }

            if (!validWindow)
            {
                cpuUsagePercent = 0;
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, 0);
                for (int cpu = 0; cpu < logicalCapacity; cpu++)
                {
                    logicalUsage[cpu] = 0;
                    RecordHistory(logicalHistory[cpu], ref logicalHistoryCount[cpu], ref logicalHistoryWrite[cpu], 0);
                }
            }
            else
            {
                ulong capacity = elapsedNs * Math.Max(1UL, (ulong)cpuCount);
                ulong busyDelta = busyTotal >= lastBusyCpuNs ? busyTotal - lastBusyCpuNs : 0;
                cpuUsagePercent = capacity == 0 ? 0 : (int)Math.Min(100UL, busyDelta * 100UL / capacity);
                RecordHistory(cpuHistory, ref cpuHistoryCount, ref cpuHistoryWrite, cpuUsagePercent);

                for (int cpu = 0; cpu < logicalCapacity; cpu++)
                {
                    int usage = (uint)cpu < cpuCount && elapsedNs > 0
                        ? (int)Math.Min(100UL, logicalBusyDelta[cpu] * 100UL / elapsedNs)
                        : 0;
                    logicalUsage[cpu] = usage;
                    RecordHistory(logicalHistory[cpu], ref logicalHistoryCount[cpu], ref logicalHistoryWrite[cpu], usage);
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

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0) return "N/A";
            if (bytes >= 1024UL * 1024UL) return (bytes / (1024UL * 1024UL)) + " MB";
            return (bytes / 1024UL) + " KB";
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
            ulong scaled = Math.Min(10000UL, used * 10000UL / total);
            return (scaled / 100UL) + "." + D2((int)(scaled % 100UL)) + "%";
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        internal List<ModernTaskRow> Rows => visibleRows;
        internal int ActivePage => activePage;
        internal int PerformanceResource => performanceResource;
        internal int CpuGraphMode => cpuGraphMode;
        internal int SelectedIndex => selectedIndex;
        internal int ScrollIndex => scrollIndex;
        internal string Status => status;
        internal string ClockText => clockText;
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
        internal string UsedMemoryText => FormatMiBPrecise(UsedPages * PageAllocator.PageSize);
        internal string FreeMemoryText => FormatMiBPrecise(freePages * PageAllocator.PageSize);
        internal string TotalMemoryText => FormatMiBPrecise(totalPages * PageAllocator.PageSize);
        internal string MemoryUsageText => FormatPercentPrecise(UsedPages, totalPages);
        internal string GcHeapText => FormatMiBPrecise(gcHeapBytes);
        internal string GcCommittedText => FormatMiBPrecise(gcCommittedBytes);
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

        public int VisibleRows => Math.Max(1, Math.Max(RowHeight, Height - ListTop - FooterHeight - 6) / RowHeight);

        public int NavAt(int x, int y)
        {
            if (x < 6 || x >= SidebarWidth - 6 || y < NavTop) return -1;
            int relative = y - NavTop;
            int page = relative / NavHeight;
            return page >= 0 && page <= 3 && relative < NavHeight * 4 ? page : -1;
        }

        public int ToolbarActionAt(int x, int y)
        {
            if (x < SidebarWidth || y < 8 || y >= 42) return 0;
            int refresh = Width - 210;
            int end = Width - 114;
            if (x >= refresh && x < refresh + 88) return 1;
            if (x >= end && x < end + 100) return 2;
            return 0;
        }

        public int PerformanceResourceAt(int x, int y)
        {
            if (app.ActivePage != TaskManagerModernApp.PagePerformance) return -1;
            int left = SidebarWidth + 10;
            int top = 64;
            int relative = y - top;
            int stride = PerfCardHeight + PerfCardGap;
            if (x < left || x >= left + 154 || relative < 0) return -1;
            int index = relative / stride;
            return index >= 0 && index <= 2 && relative % stride < PerfCardHeight ? index : -1;
        }

        public int CpuGraphModeAt(int x, int y)
        {
            if (app.ActivePage != TaskManagerModernApp.PagePerformance || app.PerformanceResource != TaskManagerModernApp.PerfCpu) return -1;
            int panelX = SidebarWidth + 180;
            int top = 86;
            if (y < top || y >= top + 26) return -1;
            if (x >= panelX && x < panelX + 92) return TaskManagerModernApp.CpuGraphTotal;
            if (x >= panelX + 98 && x < panelX + 212) return TaskManagerModernApp.CpuGraphLogical;
            return -1;
        }

        public int CpuLogicalPageActionAt(int x, int y)
        {
            if (app.CpuGraphMode != TaskManagerModernApp.CpuGraphLogical || app.LogicalPageCount <= 1) return 0;
            int top = 122;
            int right = Width - 16;
            if (y < top || y >= top + 26) return 0;
            if (x >= right - 60 && x < right - 34) return -1;
            if (x >= right - 28 && x < right - 2) return 1;
            return 0;
        }

        public int RowAt(int x, int y)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance) return -1;
            int contentX = SidebarWidth + 8;
            int listH = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            if (x < contentX || x >= Width - ScrollReserve || y < ListTop || y >= ListTop + listH) return -1;
            int index = app.ScrollIndex + (y - ListTop) / RowHeight;
            return index >= 0 && index < app.Rows.Count ? index : -1;
        }

        public bool HandleScrollMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            if (app.ActivePage == TaskManagerModernApp.PagePerformance) return false;
            UpdateScrollBar();
            return scrollBar.HandleMouse(mouseX, mouseY, left, oldLeft);
        }

        private void UpdateScrollBar()
        {
            int listH = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            scrollBar.X = X + Width - 15;
            scrollBar.Y = Y + ListTop + 2;
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
            canvas.DrawFilledRectangle(Sidebar, X + 1, Y + 1, SidebarWidth - 1, Height - 2);
            canvas.DrawLine(Color.FromArgb(52, 62, 72), X + SidebarWidth, Y + 1, X + SidebarWidth, Y + Height - 2);
            IconManager.DrawScaled(canvas, IconType.Settings, X + 16, Y + 16, 20, 20);
            SmallTextRenderer.Draw(canvas, "TASK MANAGER", X + 46, Y + 23, Text);
            DrawNav(canvas, 0, "Procesy", IconType.FileManager);
            DrawNav(canvas, 1, "Wydajnosc", IconType.Settings);
            DrawNav(canvas, 2, "Szczegoly", IconType.File);
            DrawNav(canvas, 3, "Uslugi", IconType.Settings);
            int bottom = Y + Height - 62;
            canvas.DrawLine(Color.FromArgb(48, 58, 68), X + 10, bottom - 8, X + SidebarWidth - 10, bottom - 8);
            SmallTextRenderer.Draw(canvas, "SYSTEM CLOCK", X + 16, bottom + 2, Muted);
            SmallTextRenderer.Draw(canvas, app.ClockText, X + 16, bottom + 20, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, app.DateText, X + 82, bottom + 20, Muted);
        }

        private void DrawNav(Canvas canvas, int page, string label, IconType icon)
        {
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
            int contentX = X + SidebarWidth + 8;
            string title = app.ActivePage == 0 ? "Procesy" : app.ActivePage == 1 ? "Wydajnosc" : app.ActivePage == 2 ? "Szczegoly" : "Uslugi";
            SmallTextRenderer.Draw(canvas, title, contentX + 4, Y + 21, Color.WhiteSmoke);
            int refresh = X + Width - 210;
            int end = X + Width - 114;
            int clock = Math.Max(contentX + 120, refresh - 154);
            SmallTextRenderer.Draw(canvas, app.ClockText, clock, Y + 13, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, app.DateText, clock, Y + 31, Muted);
            DrawButton(canvas, refresh, Y + 10, 88, "REFRESH", IconType.Refresh, false, true);
            DrawButton(canvas, end, Y + 10, 100, "END TASK", IconType.Close, true, app.ActivePage != 1);
            canvas.DrawLine(Color.FromArgb(52, 63, 74), contentX, Y + HeaderHeight, X + Width - 4, Y + HeaderHeight);
        }

        private void DrawButton(Canvas canvas, int x, int y, int width, string label, IconType icon, bool danger, bool enabled)
        {
            Color bg = !enabled ? Color.FromArgb(35, 40, 46) : danger ? Color.FromArgb(63, 42, 47) : Color.FromArgb(47, 55, 64);
            Color border = !enabled ? Color.FromArgb(54, 62, 70) : danger ? Color.FromArgb(111, 65, 73) : Color.FromArgb(80, 94, 108);
            canvas.DrawFilledRectangle(bg, x, y, width, 30);
            canvas.DrawRectangle(border, x, y, width, 30);
            IconManager.DrawScaled(canvas, icon, x + 7, y + 7, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 29, y + 12, width - 34, enabled ? Color.WhiteSmoke : Muted);
        }

        private void RenderProcessPage(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int width = Width - SidebarWidth - 12;
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
            int headerY = Y + TableHeaderTop;
            canvas.DrawFilledRectangle(Color.FromArgb(33, 39, 46), x, headerY, width, TableHeaderHeight);
            SmallTextRenderer.Draw(canvas, app.ActivePage == 2 ? "PID" : "NAME", x + 12, headerY + 11, Muted);
            SmallTextRenderer.Draw(canvas, app.ActivePage == 2 ? "NAME" : "TYPE", x + 90, headerY + 11, Muted);
            SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(370, width - 240), headerY + 11, Muted);
            SmallTextRenderer.Draw(canvas, "DETAIL", x + Math.Max(470, width - 120), headerY + 11, Muted);

            int listY = Y + ListTop;
            int listH = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            int rowW = width - ScrollReserve;
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
            int resourceX = X + SidebarWidth + 10;
            int resourceY = Y + 64;
            DrawResource(canvas, resourceX, resourceY, 0, "CPU", (ulong)app.CpuUsagePercent, "%");
            DrawResourceText(canvas, resourceX, resourceY + 82, 1, "MEMORY", app.MemoryUsageText);
            DrawResource(canvas, resourceX, resourceY + 164, 2, "SYSTEM", (ulong)Math.Max(0, app.SchedulerThreadCount), "");

            int x = resourceX + 170;
            int y = resourceY;
            int width = Math.Max(360, Width - (x - X) - 10);
            if (app.PerformanceResource == 0) RenderCpu(canvas, x, y, width);
            else if (app.PerformanceResource == 1) RenderMemory(canvas, x, y, width);
            else RenderSystem(canvas, x, y, width);
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

        private void RenderTotalCpu(Canvas canvas, int x, int y, int width)
        {
            int graphH = Math.Max(145, Height - 390);
            DrawGraph(canvas, x, y + 16, width, graphH, app.CpuHistoryCount, app.GetCpuHistory, -1, Accent, true);
            int stats = y + graphH + 54;
            DrawPercent(canvas, app.CpuUsagePercent, x, stats);
            int left = x + 124;
            int right = x + Math.Max(380, width / 2 + 80);
            int rightW = Math.Max(160, width - (right - x));
            DrawKey(canvas, left, stats, 230, "PHYSICAL CORES", (ulong)Math.Max(1, app.CpuInfo.PhysicalCores), null);
            DrawKey(canvas, left, stats + 20, 230, "LOGICAL CPU", (ulong)Math.Max(1, app.CpuInfo.LogicalProcessors), null);
            DrawKey(canvas, left, stats + 40, 230, "THREADS/CORE", (ulong)Math.Max(1, app.CpuInfo.ThreadsPerCore), null);
            DrawKey(canvas, left, stats + 60, 230, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, left, stats + 80, 230, "BASE / MAX", app.BaseSpeedText + " / " + app.MaxSpeedText);
            DrawKey(canvas, right, stats, rightW, "SCHED THREADS", (ulong)Math.Max(0, app.SchedulerThreadCount), null);
            DrawKeyText(canvas, right, stats + 20, rightW, "SCHEDULER", app.SchedulerName);
            DrawKeyText(canvas, right, stats + 40, rightW, "VENDOR", app.CpuInfo.Vendor);
            DrawKeyText(canvas, right, stats + 60, rightW, "TOPOLOGY C/L/T", app.TopologyText);
            DrawKeyText(canvas, right, stats + 80, rightW, "CACHE L1/L2/L3", app.CacheText);
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
            DrawGraph(canvas, x + 5, y + 24, width - 10, Math.Max(32, height - 30), app.GetLogicalHistoryCount(cpu), null, cpu, online ? Accent : Muted, false);
        }

        private void RenderMemory(Canvas canvas, int x, int y, int width)
        {
            SmallTextRenderer.Draw(canvas, "MEMORY / COSMOS HEAP", x, y + 4, Color.WhiteSmoke);
            int graphH = Math.Max(150, Height - 360);
            DrawGraph(canvas, x, y + 40, width, graphH, app.MemoryHistoryCount, app.GetMemoryHistory, -2, Color.FromArgb(116, 174, 219), true);
            int stats = y + graphH + 80;
            DrawKeyText(canvas, x, stats, width / 2, "HEAP USED", app.UsedMemoryText);
            DrawKeyText(canvas, x, stats + 20, width / 2, "HEAP FREE", app.FreeMemoryText);
            DrawKeyText(canvas, x, stats + 40, width / 2, "HEAP TOTAL", app.TotalMemoryText);
            DrawKeyText(canvas, x + width / 2, stats, width / 2, "HEAP USAGE", app.MemoryUsageText);
            DrawKeyText(canvas, x + width / 2, stats + 20, width / 2, "GC HEAP", app.GcHeapText);
            DrawKeyText(canvas, x + width / 2, stats + 40, width / 2, "GC COMMITTED", app.GcCommittedText);
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
            DrawKeyText(canvas, x, row, half, "CLOCK", app.ClockText);
            DrawKeyText(canvas, x, row + 20, half, "DATE", app.DateText);
            DrawKey(canvas, x, row + 40, half, "UPTIME", app.UptimeSeconds, "SEC");
            DrawKey(canvas, x, row + 60, half, "MANAGER OPEN", app.ManagerOpenSeconds, "SEC");
            DrawKey(canvas, x, row + 80, half, "THREADS/CORE", (ulong)Math.Max(1, app.CpuInfo.ThreadsPerCore), null);
            DrawKey(canvas, x, row + 100, half, "ONLINE CPU", app.OnlineCpuCount, null);
            DrawKeyText(canvas, x, row + 120, half, "SCHEDULER", app.SchedulerName);
            DrawKey(canvas, x, row + 140, half, "SCHED TICK", app.SchedulerTickUs, "US");

            int right = x + width / 2 + 8;
            DrawKey(canvas, right, row, half, "RUNNING", (ulong)Math.Max(0, app.RunningThreadCount), null);
            DrawKey(canvas, right, row + 20, half, "READY", (ulong)Math.Max(0, app.ReadyThreadCount), null);
            DrawKey(canvas, right, row + 40, half, "BLOCKED", (ulong)Math.Max(0, app.BlockedThreadCount), null);
            DrawKey(canvas, right, row + 60, half, "SLEEPING", (ulong)Math.Max(0, app.SleepingThreadCount), null);
            DrawKey(canvas, right, row + 80, half, "GUI APPS", (ulong)Math.Max(0, app.GuiAppCount), null);
            DrawKey(canvas, right, row + 100, half, "KERNEL PROCS", (ulong)Math.Max(0, app.KernelProcessCount), null);
            DrawKey(canvas, right, row + 120, half, "STORAGE", (ulong)Math.Max(0, app.StorageDeviceCount), null);
            DrawKeyText(canvas, right, row + 140, half, "NETWORK", app.NetworkReady ? "READY" : "OFFLINE");
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
        private void DrawGraph(Canvas canvas, int x, int y, int width, int height, int count, HistoryGetter getter, int logicalCpu, Color line, bool labels)
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
                int previousValue = logicalCpu >= 0 ? app.GetLogicalHistory(logicalCpu, 0) : getter != null ? getter(0) : 0;
                int previousY = y + height - previousValue * height / 100;
                for (int i = 1; i < count; i++)
                {
                    int value = logicalCpu >= 0 ? app.GetLogicalHistory(logicalCpu, i) : getter != null ? getter(i) : 0;
                    int px = x + (offset + i) * width / denom;
                    int py = y + height - value * height / 100;
                    canvas.DrawLine(line, previousX, previousY, px, py);
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

        private void DrawKey(Canvas canvas, int x, int y, int width, string key, ulong value, string suffix)
        {
            int keyW = Math.Min(142, Math.Max(84, width / 2));
            SmallTextRenderer.DrawClipped(canvas, key, x, y, keyW - 4, Muted);
            SmallTextRenderer.DrawUInt(canvas, value, x + keyW, y, Text);
            if (!string.IsNullOrEmpty(suffix)) SmallTextRenderer.Draw(canvas, suffix, x + keyW + SmallTextRenderer.WidthUInt(value) + 6, y, Text);
        }

        private void DrawKeyText(Canvas canvas, int x, int y, int width, string key, string value)
        {
            int keyW = Math.Min(142, Math.Max(84, width / 2));
            SmallTextRenderer.DrawClipped(canvas, key, x, y, keyW - 4, Muted);
            SmallTextRenderer.DrawClipped(canvas, value, x + keyW, y, Math.Max(20, width - keyW), Text);
        }

        private void RenderFooter(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int y = Y + Height - FooterHeight;
            int width = Width - SidebarWidth - 12;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x, y, width, FooterHeight - 4);
            canvas.DrawLine(Color.FromArgb(58, 69, 80), x, y, x + width, y);
            SmallTextRenderer.DrawClipped(canvas, app.Status, x + 8, y + 9, Math.Max(20, width - 260), Text);
            SmallTextRenderer.Draw(canvas, "F5 REFRESH   DEL END TASK", x + width - 244, y + 9, Muted);
            SmallTextRenderer.Draw(canvas, app.ClockText, x + width - 72, y + 9, Text);
        }
    }
}
