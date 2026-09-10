using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Native monthly calendar. The 42-cell month grid is rebuilt only when the user
    /// changes month or refreshes/today is requested, so idle rendering does not create
    /// managed collections or formatted date strings every frame.
    /// </summary>
    public sealed class CalendarApp : Application
    {
        private const int GridColumns = 7;
        private const int GridRows = 6;
        private const int CellGap = 6;

        private static readonly string[] MonthNames =
        {
            "STYCZEN", "LUTY", "MARZEC", "KWIECIEN", "MAJ", "CZERWIEC",
            "LIPIEC", "SIERPIEN", "WRZESIEN", "PAZDZIERNIK", "LISTOPAD", "GRUDZIEN"
        };

        private static readonly string[] DayNames =
        {
            "PON", "WTO", "SRO", "CZW", "PIA", "SOB", "NIE"
        };

        private static readonly Color Surface = Color.FromArgb(19, 24, 30);
        private static readonly Color Header = Color.FromArgb(25, 32, 39);
        private static readonly Color Panel = Color.FromArgb(29, 36, 44);
        private static readonly Color PanelHover = Color.FromArgb(38, 50, 61);
        private static readonly Color PanelSelected = Color.FromArgb(37, 57, 74);
        private static readonly Color Border = Color.FromArgb(55, 68, 80);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);

        private readonly Action closeCallback;
        private readonly int[] days = new int[42];

        private int displayYear;
        private int displayMonth;
        private int selectedDay;
        private int todayYear;
        private int todayMonth;
        private int todayDay;
        private int hoveredCell = -1;
        private int hoveredControl = -1;
        private int lastMouseX = -1;
        private int lastMouseY = -1;
        private string statusMessage = "GOTOWE";

        public CalendarApp(int x, int y, Action onClose) : base("Kalendarz")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 900, 680, "Kalendarz - ZOnderqOS");
            Window.CloseAction = Close;
            GoToday();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshTodaySnapshot();
                RebuildMonth();
                statusMessage = "ODSWIEZONO DATE SYSTEMOWA";
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                ChangeMonth(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                ChangeMonth(1);
                return;
            }

            if (key.KeyChar == 't' || key.KeyChar == 'T')
                GoToday();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            lastMouseX = mouseX;
            lastMouseY = mouseY;
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);

            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredCell = -1;
                hoveredControl = -1;
                return;
            }

            UpdateHover(mouseX, mouseY);
            if (!leftClicked || leftWasClicked)
                return;

            if (hoveredControl == 0)
            {
                ChangeMonth(-1);
                return;
            }
            if (hoveredControl == 1)
            {
                GoToday();
                return;
            }
            if (hoveredControl == 2)
            {
                ChangeMonth(1);
                return;
            }

            if (hoveredCell >= 0 && hoveredCell < days.Length && days[hoveredCell] > 0)
            {
                selectedDay = days[hoveredCell];
                statusMessage = "WYBRANO DZIEN";
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);
            int contentX = Window.X + 1;
            int contentY = Window.Y + 39;
            int contentWidth = Window.Width - 2;
            int contentHeight = Window.Height - 40;
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            RenderHeader(canvas);
            RenderCalendar(canvas);
            RenderSidePanel(canvas);
            RenderStatus(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 50;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Header, x, y, width, 62);
            canvas.DrawRectangle(Border, x, y, width, 62);
            canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 4, 62);
            IconManager.DrawScaled(canvas, IconType.Calendar, x + 15, y + 17, 28, 28);
            SmallTextRenderer.Draw(canvas, "KALENDARZ", x + 55, y + 16, Text);
            SmallTextRenderer.Draw(canvas, "STRZALKI MIESIAC  |  T DZISIAJ  |  F5 ODSWIEZ", x + 55, y + 37, Muted);

            DrawControl(canvas, 0, x + width - 254, y + 13, 54, 36, "<");
            DrawControl(canvas, 1, x + width - 192, y + 13, 122, 36, "DZISIAJ");
            DrawControl(canvas, 2, x + width - 62, y + 13, 54, 36, ">");
        }

        private void DrawControl(Canvas canvas, int index, int x, int y, int width, int height, string label)
        {
            bool hover = hoveredControl == index;
            canvas.DrawFilledRectangle(hover ? PanelHover : Panel, x, y, width, height);
            canvas.DrawRectangle(hover ? SystemTheme.AccentBorder : Border, x, y, width, height);
            if (hover)
                canvas.DrawFilledRectangle(SystemTheme.Accent, x, y + height - 2, width, 2);
            SmallTextRenderer.DrawCentered(canvas, label, x + 4, y + 15, width - 8, hover ? Text : Muted);
        }

        private void RenderCalendar(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 124;
            int width = 614;
            int gridY = y + 76;
            int cellWidth = (width - CellGap * (GridColumns - 1)) / GridColumns;
            int cellHeight = 58;

            canvas.DrawFilledRectangle(Panel, x, y, width, 58);
            canvas.DrawRectangle(Border, x, y, width, 58);
            SmallTextRenderer.Draw(canvas, MonthNames[displayMonth - 1], x + 18, y + 17, Text);
            SmallTextRenderer.DrawInt(canvas, displayYear, x + 18 + SmallTextRenderer.Width(MonthNames[displayMonth - 1]) + 12, y + 17, Muted);

            for (int col = 0; col < GridColumns; col++)
            {
                int cx = x + col * (cellWidth + CellGap);
                SmallTextRenderer.DrawCentered(canvas, DayNames[col], cx, y + 46, cellWidth, Muted);
            }

            for (int row = 0; row < GridRows; row++)
            {
                for (int col = 0; col < GridColumns; col++)
                {
                    int index = row * GridColumns + col;
                    int cx = x + col * (cellWidth + CellGap);
                    int cy = gridY + row * (cellHeight + CellGap);
                    int day = days[index];
                    bool hovered = index == hoveredCell && day > 0;
                    bool selected = day > 0 && day == selectedDay;
                    bool today = day > 0 && displayYear == todayYear && displayMonth == todayMonth && day == todayDay;

                    Color bg = selected ? PanelSelected : hovered ? PanelHover : Panel;
                    canvas.DrawFilledRectangle(bg, cx, cy, cellWidth, cellHeight);
                    canvas.DrawRectangle(selected || hovered || today ? SystemTheme.AccentBorder : Border,
                        cx, cy, cellWidth, cellHeight);

                    if (selected)
                        canvas.DrawFilledRectangle(SystemTheme.Accent, cx, cy + cellHeight - 3, cellWidth, 3);
                    else if (today)
                        canvas.DrawFilledRectangle(Good, cx + 5, cy + 5, 4, 4);

                    if (day > 0)
                    {
                        int numberWidth = SmallTextRenderer.WidthInt(day);
                        int numberX = cx + (cellWidth - numberWidth) / 2;
                        SmallTextRenderer.DrawInt(canvas, day, numberX, cy + 26,
                            today ? Good : selected ? Text : col >= 5 ? Muted : Text);
                    }
                }
            }
        }

        private void RenderSidePanel(Canvas canvas)
        {
            int x = Window.X + 646;
            int y = Window.Y + 124;
            int width = Window.Width - 664;
            int height = 462;
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "WYBRANA DATA", x + 16, y + 18, Text);
            canvas.DrawLine(Border, x + 16, y + 40, x + width - 16, y + 40);

            DrawInfoLine(canvas, x, y + 63, width, "DZIEN", selectedDay);
            SmallTextRenderer.Draw(canvas, "MIESIAC", x + 16, y + 104, Muted);
            SmallTextRenderer.DrawClipped(canvas, MonthNames[displayMonth - 1], x + 16, y + 124, width - 32, Text);
            DrawInfoLine(canvas, x, y + 155, width, "ROK", displayYear);

            int zone = global::ZonderqOS.SystemSettings.TimeZoneOffsetHours;
            SmallTextRenderer.Draw(canvas, "STREFA CZASOWA", x + 16, y + 207, Muted);
            SmallTextRenderer.Draw(canvas, "UTC", x + 16, y + 228, Text);
            if (zone >= 0)
                SmallTextRenderer.Draw(canvas, "+", x + 43, y + 228, Text);
            SmallTextRenderer.DrawInt(canvas, zone, x + 54, y + 228, Text);

            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x + 14, y + 278, width - 28, 116);
            canvas.DrawRectangle(Border, x + 14, y + 278, width - 28, 116);
            SmallTextRenderer.Draw(canvas, "DZISIAJ", x + 28, y + 295, Muted);
            SmallTextRenderer.DrawInt(canvas, todayDay, x + 28, y + 319, Good);
            SmallTextRenderer.Draw(canvas, MonthNames[todayMonth - 1], x + 28, y + 344, Text);
            SmallTextRenderer.DrawInt(canvas, todayYear, x + 28, y + 367, Text);

            SmallTextRenderer.DrawClipped(canvas, "Kalendarz korzysta z czasu systemowego i ustawionej strefy UTC.",
                x + 16, y + 420, width - 32, Muted);
        }

        private static void DrawInfoLine(Canvas canvas, int x, int y, int width, string label, int value)
        {
            SmallTextRenderer.Draw(canvas, label, x + 16, y, Muted);
            SmallTextRenderer.DrawInt(canvas, value, x + 16, y + 21, Text);
            canvas.DrawLine(Border, x + 16, y + 43, x + width - 16, y + 43);
        }

        private void RenderStatus(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + Window.Height - 42;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Header, x, y, width, 27);
            canvas.DrawRectangle(Border, x, y, width, 27);
            canvas.DrawFilledRectangle(Good, x + 10, y + 10, 5, 5);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, x + 25, y + 10, width - 38, Good);
        }

        private void GoToday()
        {
            RefreshTodaySnapshot();
            displayYear = todayYear;
            displayMonth = todayMonth;
            selectedDay = todayDay;
            RebuildMonth();
            statusMessage = "DZISIAJ";
        }

        private void RefreshTodaySnapshot()
        {
            DateTime now = DateTime.UtcNow.AddHours(global::ZonderqOS.SystemSettings.TimeZoneOffsetHours);
            todayYear = now.Year;
            todayMonth = now.Month;
            todayDay = now.Day;
        }

        private void ChangeMonth(int delta)
        {
            int month = displayMonth + delta;
            int year = displayYear;
            if (month < 1)
            {
                month = 12;
                year--;
            }
            else if (month > 12)
            {
                month = 1;
                year++;
            }

            if (year < 1)
                year = 1;
            else if (year > 9999)
                year = 9999;

            displayYear = year;
            displayMonth = month;
            int daysInMonth = DateTime.DaysInMonth(displayYear, displayMonth);
            if (selectedDay < 1)
                selectedDay = 1;
            if (selectedDay > daysInMonth)
                selectedDay = daysInMonth;
            RebuildMonth();
            statusMessage = delta < 0 ? "POPRZEDNI MIESIAC" : "NASTEPNY MIESIAC";
        }

        private void RebuildMonth()
        {
            for (int i = 0; i < days.Length; i++)
                days[i] = 0;

            DateTime first = new DateTime(displayYear, displayMonth, 1);
            int firstColumn = ((int)first.DayOfWeek + 6) % 7;
            int count = DateTime.DaysInMonth(displayYear, displayMonth);
            for (int day = 1; day <= count; day++)
            {
                int index = firstColumn + day - 1;
                if (index >= 0 && index < days.Length)
                    days[index] = day;
            }
        }

        private void UpdateHover(int mouseX, int mouseY)
        {
            hoveredControl = -1;
            hoveredCell = -1;

            int headerX = Window.X + 18;
            int headerY = Window.Y + 50;
            int headerWidth = Window.Width - 36;
            if (Hit(mouseX, mouseY, headerX + headerWidth - 254, headerY + 13, 54, 36)) hoveredControl = 0;
            else if (Hit(mouseX, mouseY, headerX + headerWidth - 192, headerY + 13, 122, 36)) hoveredControl = 1;
            else if (Hit(mouseX, mouseY, headerX + headerWidth - 62, headerY + 13, 54, 36)) hoveredControl = 2;

            int gridX = Window.X + 18;
            int gridY = Window.Y + 200;
            int gridWidth = 614;
            int cellWidth = (gridWidth - CellGap * (GridColumns - 1)) / GridColumns;
            int cellHeight = 58;
            int relativeX = mouseX - gridX;
            int relativeY = mouseY - gridY;
            if (relativeX < 0 || relativeY < 0)
                return;

            int strideX = cellWidth + CellGap;
            int strideY = cellHeight + CellGap;
            int col = relativeX / strideX;
            int row = relativeY / strideY;
            if (col < 0 || col >= GridColumns || row < 0 || row >= GridRows)
                return;
            if (relativeX % strideX >= cellWidth || relativeY % strideY >= cellHeight)
                return;

            int index = row * GridColumns + col;
            if (index >= 0 && index < days.Length && days[index] > 0)
                hoveredCell = index;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }
    }
}
