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
        private readonly ApplicationManager applicationManager;
        private readonly Action closeCallback;
        private readonly TaskManagerView view;
        private readonly List<TaskManagerRow> rows = new List<TaskManagerRow>();

        private int selectedIndex = -1;
        private int scrollIndex;
        private int refreshFrame;
        private string status = "Gotowy";
        private string appsText = "APPS 0";
        private string kernelText = "KERNEL 0";
        private string ramText = "RAM --";
        private string ramDetail = "MEMORY UNAVAILABLE";

        public TaskManagerApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Manager zadan")
        {
            applicationManager = manager;
            closeCallback = onClose;
            Window = new Window(x, y, 790, 540, "Manager zadan");
            Window.CloseAction = Close;

            view = new TaskManagerView(10, 40, 770, 485, this);
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
            view.Width = Math.Max(420, Window.Width - 20);
            view.Height = Math.Max(260, Window.Height - 50);
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
                status = "Wyslano zamkniecie: " + row.Name;
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

            rows.Clear();
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
                    rows.Add(TaskManagerRow.ForApplication(app, state, detail, app != this));
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
                rows.Add(TaskManagerRow.ForKernel(process.PID, process.Name,
                    process.IsRunning ? "RUNNING" : "STOPPED",
                    protectedProcess ? "SYSTEM PROTECTED" : "KERNEL THREAD",
                    !protectedProcess));
            }

            appsText = "APPS " + appCount;
            kernelText = "KERNEL " + processes.Count;
            UpdateMemorySnapshot();

            selectedIndex = -1;
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

            ClampScroll();
            EnsureSelectionVisible();
        }

        private void UpdateMemorySnapshot()
        {
            try
            {
                ulong totalPages = PageAllocator.TotalPageCount;
                ulong freePages = PageAllocator.FreePageCount;
                if (totalPages == 0)
                {
                    ramText = "RAM --";
                    ramDetail = "NO PAGE DATA";
                    return;
                }

                ulong freePercent = (freePages * 100UL) / totalPages;
                ulong usedPercent = 100UL - freePercent;
                ramText = "RAM " + usedPercent + "%";
                ramDetail = "FREE " + freePercent + "%  " + freePages + "/" + totalPages + " PAGES";
            }
            catch
            {
                ramText = "RAM --";
                ramDetail = "MEMORY UNAVAILABLE";
            }
        }

        private void EnsureSelectionVisible()
        {
            if (selectedIndex < 0)
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
            int visible = Math.Max(1, view.VisibleRows);
            int max = Math.Max(0, rows.Count - visible);
            scrollIndex = Math.Max(0, Math.Min(scrollIndex, max));
        }

        internal void SetScrollIndex(int value)
        {
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
        public int SelectedIndex { get { return selectedIndex; } }
        public int ScrollIndex { get { return scrollIndex; } }
        public string Status { get { return status; } }
        public string AppsText { get { return appsText; } }
        public string KernelText { get { return kernelText; } }
        public string RamText { get { return ramText; } }
        public string RamDetail { get { return ramDetail; } }
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

        private const int ToolbarHeight = 42;
        private const int SummaryTop = 50;
        private const int SummaryHeight = 48;
        private const int ListTop = 108;
        private const int FooterHeight = 28;
        private const int RowHeight = 36;
        private const int ScrollReserve = 16;

        private static readonly Color Chrome = Color.FromArgb(30, 35, 41);
        private static readonly Color Raised = Color.FromArgb(40, 47, 55);
        private static readonly Color Border = Color.FromArgb(68, 80, 93);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color Text = Color.FromArgb(225, 231, 237);
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

        public int ToolbarActionAt(int x, int y)
        {
            if (y < 7 || y >= 36)
                return 0;
            if (x >= 8 && x < 92)
                return 1;
            if (x >= 98 && x < 202)
                return 2;
            return 0;
        }

        public int RowAt(int x, int y)
        {
            int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            if (x < 4 || x >= Width - ScrollReserve || y < ListTop || y >= ListTop + listHeight)
                return -1;

            int row = (y - ListTop) / RowHeight;
            int index = app.ScrollIndex + row;
            return index >= 0 && index < app.Rows.Count ? index : -1;
        }

        public bool HandleScrollMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
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
            RenderToolbar(canvas);
            RenderSummary(canvas);
            RenderRows(canvas);
            RenderFooter(canvas);
        }

        private void RenderToolbar(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Raised, X + 4, Y + 4, Width - 8, ToolbarHeight - 2);
            DrawButton(canvas, X + 8, Y + 7, 84, "REFRESH", IconType.Refresh, false);
            DrawButton(canvas, X + 98, Y + 7, 104, "END TASK", IconType.Close, true);
            SmallTextRenderer.DrawClipped(canvas, "SYSTEM TASK MONITOR", X + 218, Y + 18,
                Math.Max(20, Width - 232), Muted);
        }

        private void DrawButton(Canvas canvas, int x, int y, int width, string label, IconType icon, bool danger)
        {
            Color background = danger ? Color.FromArgb(68, 43, 48) : Color.FromArgb(49, 57, 66);
            Color border = danger ? Color.FromArgb(117, 67, 75) : Color.FromArgb(83, 96, 110);
            canvas.DrawFilledRectangle(background, x, y, width, 29);
            canvas.DrawRectangle(border, x, y, width, 29);
            IconManager.DrawScaled(canvas, icon, x + 6, y + 6, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 27, y + 11,
                Math.Max(10, width - 32), Color.WhiteSmoke);
        }

        private void RenderSummary(Canvas canvas)
        {
            int gap = 8;
            int cardWidth = Math.Max(90, (Width - 8 - gap * 2) / 3);
            int x = X + 4;
            DrawSummaryCard(canvas, x, app.AppsText, "GUI APPLICATIONS");
            x += cardWidth + gap;
            DrawSummaryCard(canvas, x, app.KernelText, "KERNEL PROCESSES");
            x += cardWidth + gap;
            DrawSummaryCard(canvas, x, app.RamText, app.RamDetail);
        }

        private void DrawSummaryCard(Canvas canvas, int x, string title, string detail)
        {
            int gap = 8;
            int cardWidth = Math.Max(90, (Width - 8 - gap * 2) / 3);
            canvas.DrawFilledRectangle(Color.FromArgb(25, 31, 37), x, Y + SummaryTop, cardWidth, SummaryHeight);
            canvas.DrawRectangle(Color.FromArgb(55, 67, 78), x, Y + SummaryTop, cardWidth, SummaryHeight);
            canvas.DrawFilledRectangle(Accent, x, Y + SummaryTop, 3, SummaryHeight);
            SmallTextRenderer.DrawClipped(canvas, title, x + 12, Y + SummaryTop + 13,
                cardWidth - 22, Text);
            SmallTextRenderer.DrawClipped(canvas, detail, x + 12, Y + SummaryTop + 30,
                cardWidth - 22, Muted);
        }

        private void RenderRows(Canvas canvas)
        {
            int listY = Y + ListTop;
            int listHeight = Math.Max(RowHeight, Height - ListTop - FooterHeight - 6);
            int contentWidth = Math.Max(80, Width - 8 - ScrollReserve);

            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), X + 4, listY, Width - 8, listHeight);
            canvas.DrawRectangle(Color.FromArgb(53, 64, 75), X + 4, listY, Width - 8, listHeight);

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
                    canvas.DrawFilledRectangle(Color.FromArgb(38, 63, 84), X + 6, y + 1,
                        contentWidth - 2, RowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, X + 6, y + 1, 3, RowHeight - 2);
                }
                else if ((rowIndex & 1) != 0)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(27, 33, 39), X + 6, y + 1,
                        contentWidth - 2, RowHeight - 2);
                }

                IconType icon = row.IsKernel ? IconType.Settings : GetApplicationIcon(row.GuiApplication);
                canvas.DrawFilledRectangle(Color.FromArgb(34, 41, 48), X + 14, y + 6, 24, 24);
                IconManager.DrawScaled(canvas, icon, X + 17, y + 9, 18, 18);

                string type = row.IsKernel ? "KERNEL" : "APP";
                SmallTextRenderer.Draw(canvas, type, X + 48, y + 8,
                    row.IsKernel ? Color.FromArgb(153, 177, 197) : Color.FromArgb(124, 186, 229));
                SmallTextRenderer.DrawClipped(canvas, row.Name, X + 100, y + 8,
                    Math.Max(30, contentWidth - 290), selected ? Color.WhiteSmoke : Text);

                int stateX = X + Math.Max(250, contentWidth - 174);
                SmallTextRenderer.DrawClipped(canvas, row.State, stateX, y + 8, 82,
                    row.State == "ACTIVE" ? Color.FromArgb(111, 194, 145) : Muted);
                SmallTextRenderer.DrawClipped(canvas, row.Detail, X + 100, y + 22,
                    Math.Max(30, contentWidth - 210), Muted);

                if (!row.CanEnd)
                    SmallTextRenderer.Draw(canvas, "LOCK", X + contentWidth - 48, y + 22,
                        Color.FromArgb(184, 126, 133));
            }

            if (app.Rows.Count == 0)
                SmallTextRenderer.Draw(canvas, "BRAK AKTYWNYCH ZADAN", X + 20, listY + 18, Muted);

            UpdateScrollBar();
            scrollBar.Render(canvas);
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
            if (name.IndexOf("diagn", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("about", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.About;
            return IconType.Settings;
        }

        private void RenderFooter(Canvas canvas)
        {
            int y = Y + Height - FooterHeight;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), X + 4, y, Width - 8, FooterHeight - 4);
            canvas.DrawLine(Color.FromArgb(64, 76, 88), X + 4, y, X + Width - 4, y);
            SmallTextRenderer.DrawClipped(canvas, app.Status, X + 12, y + 9,
                Math.Max(20, Width - 210), Color.FromArgb(184, 195, 205));
            SmallTextRenderer.DrawClipped(canvas, "F5 REFRESH", X + Width - 92, y + 9,
                80, Muted);
        }
    }
}
