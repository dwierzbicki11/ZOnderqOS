using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public sealed class TaskManagerApp : Application
    {
        internal const int PageProcesses = 0;
        internal const int PagePerformance = 1;
        internal const int PageDetails = 2;

        private readonly ApplicationManager applicationManager;
        private readonly Action closeCallback;
        private readonly TaskManagerView view;
        private readonly List<TaskManagerRow> allRows = new List<TaskManagerRow>();
        private readonly List<TaskManagerRow> rows = new List<TaskManagerRow>();
        private readonly int[] memoryHistory = new int[60];

        private int activePage = PageProcesses;
        private int selectedIndex = -1;
        private int scrollIndex;
        private int refreshFrame;
        private int memoryHistoryCount;
        private string status = "Gotowy";
        private string appsText = "APPS 0";
        private string kernelText = "KERNEL 0";
        private string ramText = "RAM --";
        private string ramDetail = "MEMORY UNAVAILABLE";
        private ulong usedMemoryPercent;
        private ulong freeMemoryPercent;
        private ulong totalPages;
        private ulong freePages;

        public TaskManagerApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Manager zadan")
        {
            applicationManager = manager;
            closeCallback = onClose;
            Window = new Window(x, y, 900, 610, "Manager zadan");
            Window.CloseAction = Close;

            view = new TaskManagerView(10, 40, 880, 555, this);
            Window.AddChild(view);
            UpdateLayout();
            RefreshSnapshot();
        }

        public override void Update()
        {
            UpdateLayout();
            refreshFrame++;
            if (refreshFrame >= 30)
            {
                refreshFrame = 0;
                RefreshSnapshot();
            }
        }

        private void UpdateLayout()
        {
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(560, Window.Width - 20);
            view.Height = Math.Max(330, Window.Height - 50);
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
                return;

            int index = view.RowAt(x, y);
            if (index >= 0 && index < rows.Count)
            {
                selectedIndex = index;
                EnsureSelectionVisible();
                TaskManagerRow row = rows[index];
                status = row.Name + " - " + row.State;
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
                SetPage(Math.Max(PageProcesses, activePage - 1));
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                SetPage(Math.Min(PageDetails, activePage + 1));
                return;
            }

            if (activePage == PagePerformance)
                return;

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                Select(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                Select(1);
                return;
            }
        }

        private void SetPage(int page)
        {
            if (page < PageProcesses || page > PageDetails || page == activePage)
                return;

            activePage = page;
            selectedIndex = -1;
            scrollIndex = 0;
            RebuildVisibleRows(null, -1);

            if (page == PageProcesses)
                status = "Procesy";
            else if (page == PagePerformance)
                status = "Wydajnosc systemu";
            else
                status = "Szczegoly procesow";
        }

        private void Select(int delta)
        {
            if (rows.Count == 0)
                return;

            if (selectedIndex < 0)
                selectedIndex = delta >= 0 ? 0 : rows.Count - 1;
            else
                selectedIndex = Math.Max(0, Math.Min(rows.Count - 1, selectedIndex + delta));

            EnsureSelectionVisible();
            status = rows[selectedIndex].Name + " - " + rows[selectedIndex].State;
        }

        private void EndSelectedTask()
        {
            if (activePage == PagePerformance)
            {
                status = "Wybierz proces w zakladce Procesy lub Szczegoly";
                return;
            }

            if (selectedIndex < 0 || selectedIndex >= rows.Count)
            {
                status = "Wybierz zadanie";
                return;
            }

            TaskManagerRow row = rows[selectedIndex];
            if (!row.CanEnd)
            {
                status = row.Name + " jest chronionym zadaniem systemowym";
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
                status = "Zakonczono zadanie: " + row.Name;
                RefreshSnapshot();
                return;
            }

            if (row.KernelPid > 0)
            {
                bool requested = ProcessManager.Kill(row.KernelPid);
                status = requested
                    ? "Wyslano zatrzymanie PID " + row.KernelPid
                    : "Nie mozna zatrzymac PID " + row.KernelPid;
                RefreshSnapshot();
            }
        }

        private void RefreshSnapshot()
        {
            Application selectedApp = null;
            int selectedPid = -1;
            if (selectedIndex >= 0 && selectedIndex < rows.Count)
            {
                selectedApp = rows[selectedIndex].GuiApplication;
                selectedPid = rows[selectedIndex].KernelPid;
            }

            allRows.Clear();
            int appCount = 0;
            Application active = applicationManager != null ? applicationManager.ActiveApplication : null;
            if (applicationManager != null)
            {
                List<Application> apps = applicationManager.Applications;
                for (int i = 0; i < apps.Count; i++)
                {
                    Application app = apps[i];
                    if (app == null || !app.IsRunning || app.Window == null)
                        continue;

                    string state = app.Window.IsMinimized
                        ? "MINIMIZED"
                        : active == app ? "ACTIVE" : "RUNNING";
                    string detail = app.Window.Width + "x" + app.Window.Height;
                    allRows.Add(TaskManagerRow.ForApplication(app, state, detail, app != this));
                    appCount++;
                }
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            for (int i = 0; i < processes.Count; i++)
            {
                KernelProcess process = processes[i];
                if (process == null)
                    continue;

                bool protectedProcess = string.Equals(process.Name, "sys_guardian", StringComparison.OrdinalIgnoreCase);
                allRows.Add(TaskManagerRow.ForKernel(process.PID, process.Name,
                    process.IsRunning ? "RUNNING" : "STOPPED",
                    protectedProcess ? "SYSTEM PROTECTED" : "KERNEL THREAD",
                    !protectedProcess));
            }

            appsText = "APPS " + appCount;
            kernelText = "KERNEL " + processes.Count;
            UpdateMemorySnapshot();
            RebuildVisibleRows(selectedApp, selectedPid);
        }

        private void RebuildVisibleRows(Application selectedApp, int selectedPid)
        {
            rows.Clear();
            if (activePage != PagePerformance)
            {
                for (int i = 0; i < allRows.Count; i++)
                    rows.Add(allRows[i]);
            }

            selectedIndex = -1;
            if (selectedApp != null || selectedPid > 0)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    if (selectedApp != null && rows[i].GuiApplication == selectedApp)
                    {
                        selectedIndex = i;
                        break;
                    }
                    if (selectedPid > 0 && rows[i].KernelPid == selectedPid)
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
                if (totalPages == 0)
                {
                    usedMemoryPercent = 0;
                    freeMemoryPercent = 0;
                    ramText = "RAM --";
                    ramDetail = "NO PAGE DATA";
                    RecordMemorySample(0);
                    return;
                }

                freeMemoryPercent = (freePages * 100UL) / totalPages;
                usedMemoryPercent = 100UL - freeMemoryPercent;
                ramText = "RAM " + usedMemoryPercent + "%";
                ramDetail = "FREE " + freeMemoryPercent + "%";
                RecordMemorySample((int)usedMemoryPercent);
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
                usedMemoryPercent = 0;
                freeMemoryPercent = 0;
                ramText = "RAM --";
                ramDetail = "MEMORY UNAVAILABLE";
                RecordMemorySample(0);
            }
        }

        private void RecordMemorySample(int value)
        {
            value = Math.Max(0, Math.Min(100, value));
            if (memoryHistoryCount < memoryHistory.Length)
            {
                memoryHistory[memoryHistoryCount++] = value;
                return;
            }

            for (int i = 1; i < memoryHistory.Length; i++)
                memoryHistory[i - 1] = memoryHistory[i];
            memoryHistory[memoryHistory.Length - 1] = value;
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

            int visible = Math.Max(1, view.VisibleRows);
            int max = Math.Max(0, rows.Count - visible);
            scrollIndex = Math.Max(0, Math.Min(scrollIndex, max));
        }

        internal void SetScrollIndex(int value)
        {
            if (activePage == PagePerformance)
                return;

            int visible = Math.Max(1, view.VisibleRows);
            int max = Math.Max(0, rows.Count - visible);
            scrollIndex = Math.Max(0, Math.Min(value, max));
        }

        public override void Close()
        {
            base.Close();
            if (closeCallback != null)
                closeCallback();
        }

        public List<TaskManagerRow> Rows { get { return rows; } }
        public int ActivePage { get { return activePage; } }
        public int SelectedIndex { get { return selectedIndex; } }
        public int ScrollIndex { get { return scrollIndex; } }
        public string Status { get { return status; } }
        public string AppsText { get { return appsText; } }
        public string KernelText { get { return kernelText; } }
        public string RamText { get { return ramText; } }
        public string RamDetail { get { return ramDetail; } }
        public ulong UsedMemoryPercent { get { return usedMemoryPercent; } }
        public ulong FreeMemoryPercent { get { return freeMemoryPercent; } }
        public ulong TotalPages { get { return totalPages; } }
        public ulong FreePages { get { return freePages; } }
        public int[] MemoryHistory { get { return memoryHistory; } }
        public int MemoryHistoryCount { get { return memoryHistoryCount; } }
    }

    public sealed class TaskManagerRow
    {
        public readonly string Name;
        public readonly string State;
        public readonly string Detail;
        public readonly Application GuiApplication;
        public readonly int KernelPid;
        public readonly bool CanEnd;
        public readonly bool IsKernel;

        private TaskManagerRow(string name, string state, string detail, Application app,
            int kernelPid, bool canEnd, bool isKernel)
        {
            Name = name;
            State = state;
            Detail = detail;
            GuiApplication = app;
            KernelPid = kernelPid;
            CanEnd = canEnd;
            IsKernel = isKernel;
        }

        public static TaskManagerRow ForApplication(Application app, string state, string detail, bool canEnd)
        {
            string name = app == null || string.IsNullOrEmpty(app.Name) ? "Application" : app.Name;
            return new TaskManagerRow(name, state, detail, app, -1, canEnd, false);
        }

        public static TaskManagerRow ForKernel(int pid, string name, string state, string detail, bool canEnd)
        {
            return new TaskManagerRow(string.IsNullOrEmpty(name) ? "kernel" : name,
                state, detail, null, pid, canEnd, true);
        }
    }

    internal sealed class TaskManagerView : Widget
    {
        private readonly TaskManagerApp app;
        private readonly ScrollBar scrollBar;

        private const int SidebarWidth = 162;
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

        private static readonly Color Chrome = Color.FromArgb(29, 34, 40);
        private static readonly Color Sidebar = Color.FromArgb(24, 29, 35);
        private static readonly Color Raised = Color.FromArgb(38, 45, 52);
        private static readonly Color Border = Color.FromArgb(64, 76, 88);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color Text = Color.FromArgb(228, 234, 239);
        private static readonly Color Muted = Color.FromArgb(139, 154, 168);

        public TaskManagerView(int x, int y, int width, int height, TaskManagerApp owner)
            : base(x, y, width, height)
        {
            app = owner;
            scrollBar = new ScrollBar(0, 0, 10, 100);
            scrollBar.ValueChanged = delegate(int value)
            {
                app.SetScrollIndex(value);
            };
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
            if (page >= TaskManagerApp.PageProcesses && page <= TaskManagerApp.PageDetails &&
                relative < NavHeight * 3)
                return page;
            return -1;
        }

        public int ToolbarActionAt(int x, int y)
        {
            if (x < SidebarWidth || y < 10 || y >= 40)
                return 0;

            int refreshX = Width - 206;
            int endX = Width - 112;
            if (x >= refreshX && x < refreshX + 86)
                return 1;
            if (x >= endX && x < endX + 98)
                return 2;
            return 0;
        }

        public int RowAt(int x, int y)
        {
            if (app.ActivePage == TaskManagerApp.PagePerformance)
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
            if (app.ActivePage == TaskManagerApp.PagePerformance)
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

            if (app.ActivePage == TaskManagerApp.PagePerformance)
                RenderPerformance(canvas);
            else
                RenderProcessPage(canvas);

            RenderFooter(canvas);
        }

        private void RenderSidebar(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Sidebar, X + 1, Y + 1, SidebarWidth - 1, Height - 2);
            canvas.DrawLine(Color.FromArgb(52, 62, 72), X + SidebarWidth, Y + 1,
                X + SidebarWidth, Y + Height - 2);

            IconManager.DrawScaled(canvas, IconType.Settings, X + 16, Y + 16, 20, 20);
            SmallTextRenderer.Draw(canvas, "TASK MANAGER", X + 46, Y + 23, Text);

            DrawNavItem(canvas, TaskManagerApp.PageProcesses, "Procesy", IconType.FileManager);
            DrawNavItem(canvas, TaskManagerApp.PagePerformance, "Wydajnosc", IconType.Settings);
            DrawNavItem(canvas, TaskManagerApp.PageDetails, "Szczegoly", IconType.File);

            int bottomY = Y + Height - 44;
            canvas.DrawLine(Color.FromArgb(48, 58, 68), X + 10, bottomY - 8,
                X + SidebarWidth - 10, bottomY - 8);
            IconManager.DrawScaled(canvas, IconType.About, X + 17, bottomY, 16, 16);
            SmallTextRenderer.Draw(canvas, "ZONDERQOS", X + 44, bottomY + 5, Muted);
        }

        private void DrawNavItem(Canvas canvas, int page, string label, IconType icon)
        {
            int y = Y + NavTop + page * NavHeight;
            bool active = app.ActivePage == page;
            if (active)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(38, 55, 70), X + 8, y + 2,
                    SidebarWidth - 16, NavHeight - 4);
                canvas.DrawFilledRectangle(Accent, X + 8, y + 7, 3, NavHeight - 14);
            }

            canvas.DrawFilledRectangle(Color.FromArgb(31, 38, 45), X + 18, y + 10, 24, 24);
            IconManager.DrawScaled(canvas, icon, X + 21, y + 13, 18, 18);
            SmallTextRenderer.Draw(canvas, label, X + 52, y + 18,
                active ? Color.WhiteSmoke : Color.FromArgb(196, 205, 213));
        }

        private void RenderHeader(Canvas canvas)
        {
            int contentX = X + SidebarWidth + 8;
            int contentW = Width - SidebarWidth - 12;
            string title = app.ActivePage == TaskManagerApp.PageProcesses
                ? "Procesy"
                : app.ActivePage == TaskManagerApp.PagePerformance ? "Wydajnosc" : "Szczegoly";

            SmallTextRenderer.Draw(canvas, title, contentX + 4, Y + 21, Color.WhiteSmoke);

            int refreshX = X + Width - 206;
            int endX = X + Width - 112;
            DrawButton(canvas, refreshX, Y + 10, 86, "REFRESH", IconType.Refresh, false, true);
            DrawButton(canvas, endX, Y + 10, 98, "END TASK", IconType.Close, true,
                app.ActivePage != TaskManagerApp.PagePerformance);

            canvas.DrawLine(Color.FromArgb(52, 63, 74), contentX, Y + HeaderHeight,
                contentX + contentW, Y + HeaderHeight);
        }

        private void DrawButton(Canvas canvas, int x, int y, int width, string label, IconType icon,
            bool danger, bool enabled)
        {
            Color background = !enabled
                ? Color.FromArgb(35, 40, 46)
                : danger ? Color.FromArgb(63, 42, 47) : Color.FromArgb(47, 55, 64);
            Color border = !enabled
                ? Color.FromArgb(54, 62, 70)
                : danger ? Color.FromArgb(111, 65, 73) : Color.FromArgb(80, 94, 108);
            Color labelColor = enabled ? Color.WhiteSmoke : Color.FromArgb(112, 122, 132);

            canvas.DrawFilledRectangle(background, x, y, width, 30);
            canvas.DrawRectangle(border, x, y, width, 30);
            IconManager.DrawScaled(canvas, icon, x + 7, y + 7, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 29, y + 12,
                Math.Max(10, width - 34), labelColor);
        }

        private void RenderProcessPage(Canvas canvas)
        {
            int contentX = X + SidebarWidth + 8;
            int contentW = Width - SidebarWidth - 12;

            RenderProcessSummary(canvas, contentX, contentW);
            RenderTableHeader(canvas, contentX, contentW);
            RenderRows(canvas, contentX, contentW);
        }

        private void RenderProcessSummary(Canvas canvas, int x, int width)
        {
            int gap = 8;
            int cardW = Math.Max(86, (width - gap * 3) / 4);
            DrawMiniCard(canvas, x, app.AppsText, "APPLICATIONS", cardW, false);
            DrawMiniCard(canvas, x + cardW + gap, app.KernelText, "BACKGROUND", cardW, false);
            DrawMiniCard(canvas, x + (cardW + gap) * 2, app.RamText, "MEMORY", cardW, true);
            DrawMiniCard(canvas, x + (cardW + gap) * 3,
                global::ZonderqOS.Network.IsReady ? "NET READY" : "NET OFF", "NETWORK", cardW, false);
        }

        private void DrawMiniCard(Canvas canvas, int x, string title, string detail, int width, bool accent)
        {
            int y = Y + SummaryTop;
            canvas.DrawFilledRectangle(Color.FromArgb(26, 32, 38), x, y, width, SummaryHeight);
            canvas.DrawRectangle(Color.FromArgb(53, 65, 76), x, y, width, SummaryHeight);
            if (accent)
                canvas.DrawFilledRectangle(Accent, x, y, 3, SummaryHeight);
            SmallTextRenderer.DrawClipped(canvas, title, x + 10, y + 13, width - 20,
                accent ? Color.FromArgb(137, 194, 233) : Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 10, y + 30, width - 20, Muted);
        }

        private void RenderTableHeader(Canvas canvas, int x, int width)
        {
            int y = Y + TableHeaderTop;
            canvas.DrawFilledRectangle(Color.FromArgb(33, 39, 46), x, y, width, TableHeaderHeight);
            canvas.DrawLine(Color.FromArgb(57, 68, 79), x, y + TableHeaderHeight - 1,
                x + width, y + TableHeaderHeight - 1);

            if (app.ActivePage == TaskManagerApp.PageDetails)
            {
                SmallTextRenderer.Draw(canvas, "PID", x + 12, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "NAME", x + 74, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(300, width - 260), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "DETAIL", x + Math.Max(390, width - 158), y + 11, Muted);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, "NAME", x + 12, y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "TYPE", x + Math.Max(260, width - 350), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "STATUS", x + Math.Max(350, width - 240), y + 11, Muted);
                SmallTextRenderer.Draw(canvas, "ID", x + Math.Max(450, width - 108), y + 11, Muted);
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
                if (index >= app.Rows.Count)
                    break;

                TaskManagerRow row = app.Rows[index];
                int y = listY + rowIndex * RowHeight;
                bool selected = index == app.SelectedIndex;

                if (selected)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(38, 62, 82), contentX + 2, y + 1,
                        rowWidth - 2, RowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, contentX + 2, y + 1, 3, RowHeight - 2);
                }
                else if ((rowIndex & 1) != 0)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(27, 33, 39), contentX + 2, y + 1,
                        rowWidth - 2, RowHeight - 2);
                }

                if (app.ActivePage == TaskManagerApp.PageDetails)
                    DrawDetailsRow(canvas, row, contentX, rowWidth, y, selected);
                else
                    DrawProcessRow(canvas, row, contentX, rowWidth, y, selected);
            }

            if (app.Rows.Count == 0)
                SmallTextRenderer.Draw(canvas, "BRAK AKTYWNYCH ZADAN", contentX + 16, listY + 18, Muted);

            UpdateScrollBar();
            scrollBar.Render(canvas);
        }

        private void DrawProcessRow(Canvas canvas, TaskManagerRow row, int x, int width, int y, bool selected)
        {
            IconType icon = row.IsKernel ? IconType.Settings : GetApplicationIcon(row.GuiApplication);
            canvas.DrawFilledRectangle(Color.FromArgb(34, 41, 48), x + 10, y + 6, 24, 24);
            IconManager.DrawScaled(canvas, icon, x + 13, y + 9, 18, 18);

            int typeX = x + Math.Max(260, width - 350);
            int statusX = x + Math.Max(350, width - 240);
            int idX = x + Math.Max(450, width - 108);
            int nameW = Math.Max(40, typeX - x - 54);

            SmallTextRenderer.DrawClipped(canvas, row.Name, x + 44, y + 14, nameW,
                selected ? Color.WhiteSmoke : Text);
            SmallTextRenderer.Draw(canvas, row.IsKernel ? "KERNEL" : "APP", typeX, y + 14,
                row.IsKernel ? Color.FromArgb(154, 176, 195) : Color.FromArgb(124, 186, 229));
            SmallTextRenderer.DrawClipped(canvas, row.State, statusX, y + 14, 86,
                row.State == "ACTIVE" ? Color.FromArgb(111, 194, 145) : Muted);
            SmallTextRenderer.Draw(canvas, row.KernelPid > 0 ? row.KernelPid.ToString() : "GUI",
                idX, y + 14, Muted);

            if (!row.CanEnd)
                SmallTextRenderer.Draw(canvas, "LOCK", x + width - 46, y + 25,
                    Color.FromArgb(184, 126, 133));
        }

        private void DrawDetailsRow(Canvas canvas, TaskManagerRow row, int x, int width, int y, bool selected)
        {
            int stateX = x + Math.Max(300, width - 260);
            int detailX = x + Math.Max(390, width - 158);
            int nameW = Math.Max(40, stateX - x - 82);

            SmallTextRenderer.Draw(canvas, row.KernelPid > 0 ? row.KernelPid.ToString() : "GUI",
                x + 12, y + 14, Muted);
            SmallTextRenderer.DrawClipped(canvas, row.Name, x + 74, y + 14, nameW,
                selected ? Color.WhiteSmoke : Text);
            SmallTextRenderer.DrawClipped(canvas, row.State, stateX, y + 14, 82,
                row.State == "ACTIVE" ? Color.FromArgb(111, 194, 145) : Muted);
            SmallTextRenderer.DrawClipped(canvas, row.Detail, detailX, y + 14,
                Math.Max(30, width - (detailX - x) - 8), Muted);
        }

        private void RenderPerformance(Canvas canvas)
        {
            int x = X + SidebarWidth + 16;
            int y = Y + 68;
            int width = Width - SidebarWidth - 28;
            int graphHeight = Math.Max(130, Height - 260);

            SmallTextRenderer.Draw(canvas, "Memory", x, y, Color.WhiteSmoke);
            SmallTextRenderer.Draw(canvas, app.UsedMemoryPercent + "% IN USE", x + width - 92, y, Accent);

            int graphY = y + 30;
            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x, graphY, width, graphHeight);
            canvas.DrawRectangle(Color.FromArgb(65, 105, 135), x, graphY, width, graphHeight);

            for (int i = 1; i < 4; i++)
            {
                int gy = graphY + i * graphHeight / 4;
                canvas.DrawLine(Color.FromArgb(39, 52, 63), x + 1, gy, x + width - 1, gy);
            }
            for (int i = 1; i < 6; i++)
            {
                int gx = x + i * width / 6;
                canvas.DrawLine(Color.FromArgb(35, 47, 58), gx, graphY + 1, gx, graphY + graphHeight - 1);
            }

            int count = app.MemoryHistoryCount;
            int[] history = app.MemoryHistory;
            if (count > 1)
            {
                int denominator = Math.Max(1, history.Length - 1);
                int start = Math.Max(0, count - history.Length);
                int previousX = x;
                int previousY = graphY + graphHeight - (history[start] * graphHeight / 100);
                for (int i = start + 1; i < count; i++)
                {
                    int sampleIndex = i - start;
                    int px = x + (sampleIndex * width) / denominator;
                    int py = graphY + graphHeight - (history[i] * graphHeight / 100);
                    canvas.DrawLine(Accent, previousX, previousY, px, py);
                    previousX = px;
                    previousY = py;
                }
            }

            SmallTextRenderer.Draw(canvas, "100%", x + 6, graphY + 7, Color.FromArgb(102, 122, 139));
            SmallTextRenderer.Draw(canvas, "0%", x + 6, graphY + graphHeight - 12, Color.FromArgb(102, 122, 139));

            int cardY = graphY + graphHeight + 14;
            int gap = 8;
            int cardW = Math.Max(96, (width - gap * 3) / 4);
            DrawPerformanceCard(canvas, x, cardY, cardW, "IN USE", app.UsedMemoryPercent + "%");
            DrawPerformanceCard(canvas, x + cardW + gap, cardY, cardW, "AVAILABLE", app.FreeMemoryPercent + "%");
            DrawPerformanceCard(canvas, x + (cardW + gap) * 2, cardY, cardW, "GUI APPS", app.AppsText);
            DrawPerformanceCard(canvas, x + (cardW + gap) * 3, cardY, cardW, "KERNEL", app.KernelText);

            SmallTextRenderer.DrawClipped(canvas,
                "PAGE ALLOCATOR  FREE " + app.FreePages + " / TOTAL " + app.TotalPages,
                x, cardY + 66, width, Muted);
        }

        private void DrawPerformanceCard(Canvas canvas, int x, int y, int width, string label, string value)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(27, 33, 39), x, y, width, 54);
            canvas.DrawRectangle(Color.FromArgb(54, 66, 77), x, y, width, 54);
            SmallTextRenderer.DrawClipped(canvas, label, x + 10, y + 13, width - 20, Muted);
            SmallTextRenderer.DrawClipped(canvas, value, x + 10, y + 32, width - 20, Text);
        }

        private IconType GetApplicationIcon(Application application)
        {
            if (application == null || string.IsNullOrEmpty(application.Name))
                return IconType.File;

            string name = application.Name;
            if (name.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Terminal;
            if (name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Folder;
            if (name.IndexOf("notat", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.File;
            if (name.IndexOf("manager", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("task", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Settings;
            if (name.IndexOf("diagn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("about", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.About;
            return IconType.Settings;
        }

        private void RenderFooter(Canvas canvas)
        {
            int x = X + SidebarWidth + 8;
            int y = Y + Height - FooterHeight;
            int width = Width - SidebarWidth - 12;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x, y, width, FooterHeight - 4);
            canvas.DrawLine(Color.FromArgb(58, 69, 80), x, y, x + width, y);
            SmallTextRenderer.DrawClipped(canvas, app.Status, x + 8, y + 9,
                Math.Max(20, width - 150), Color.FromArgb(184, 195, 205));
            SmallTextRenderer.DrawClipped(canvas, "F5 REFRESH", x + width - 90, y + 9, 80, Muted);
        }
    }
}
