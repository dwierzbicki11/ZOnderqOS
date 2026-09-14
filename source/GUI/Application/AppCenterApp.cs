using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Browser for applications registered in the current GUI session. Metadata and
    /// launch actions come from AppRegistry, so App Center never keeps a parallel list.
    /// </summary>
    public sealed class AppCenterApp : Application
    {
        private const int VisibleRows = 6;
        private const int RowHeight = 57;
        private const int RowGap = 6;

        // AppCategory values intentionally map to these tab indexes.
        private static readonly string[] CategoryLabels =
        {
            "WSZYSTKIE", "SYSTEM", "NARZEDZIA", "PLIKI"
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
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);

        private readonly AppRegistry registry;
        private readonly Action closeCallback;
        private readonly int[] visibleIndices;

        private int category;
        private int visibleCount;
        private int selectedVisibleIndex;
        private int offset;
        private int hoveredTab = -1;
        private int hoveredRow = -1;
        private int hoveredAction = -1;
        private string searchText = string.Empty;
        private string statusMessage = "GOTOWE";
        private Color statusColor = Good;

        public AppCenterApp(int x, int y, AppRegistry appRegistry, Action onClose)
            : base("App Center")
        {
            registry = appRegistry ?? new AppRegistry();
            closeCallback = onClose;
            visibleIndices = new int[Math.Max(1, registry.Count)];
            Window = new Window(x, y, 1040, 700, "App Center - ZOnderqOS");
            Window.CloseAction = Close;
            RebuildFilter();
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
                RebuildFilter();
                SetStatus("ODSWIEZONO KATALOG APLIKACJI", Good);
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                ChangeCategory(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                ChangeCategory(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                MoveSelection(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                MoveSelection(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                LaunchSelected();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (searchText.Length > 0)
                {
                    searchText = searchText.Substring(0, searchText.Length - 1);
                    RebuildFilter();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                ResetFilter();
                return;
            }

            char ch = key.KeyChar;
            if (ch >= ' ' && ch != 127 && searchText.Length < 32)
            {
                searchText += ch;
                RebuildFilter();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredTab = -1;
                hoveredRow = -1;
                hoveredAction = -1;
                return;
            }

            UpdateHover(mouseX, mouseY);
            if (!leftClicked || leftWasClicked)
                return;

            if (hoveredTab >= 0)
            {
                category = hoveredTab;
                selectedVisibleIndex = 0;
                offset = 0;
                RebuildFilter();
                SetStatus("ZMIENIONO KATEGORIE", SystemTheme.Accent);
                return;
            }

            if (hoveredRow >= 0)
            {
                int visibleIndex = offset + hoveredRow;
                if (visibleIndex >= 0 && visibleIndex < visibleCount)
                {
                    selectedVisibleIndex = visibleIndex;
                    KeepSelectionVisible();
                    SetStatus("WYBRANO APLIKACJE", Good);
                }
                return;
            }

            if (hoveredAction == 0)
                LaunchSelected();
            else if (hoveredAction == 1)
                ResetFilter();
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);
            canvas.DrawFilledRectangle(Surface, Window.X + 1, Window.Y + 39,
                Window.Width - 2, Window.Height - 40);

            RenderHeader(canvas);
            RenderTabs(canvas);
            RenderSearch(canvas);
            RenderList(canvas);
            RenderDetails(canvas);
            RenderActions(canvas);
            RenderStatus(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 49;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Header, x, y, width, 54);
            canvas.DrawRectangle(Border, x, y, width, 54);
            canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 4, 54);
            IconManager.DrawScaled(canvas, IconType.AppCenter, x + 15, y + 13, 28, 28);
            SmallTextRenderer.Draw(canvas, "APP CENTER", x + 55, y + 13, Text);
            SmallTextRenderer.Draw(canvas, "LOKALNY KATALOG WBUDOWANYCH APLIKACJI ZONDERQOS", x + 55, y + 32, Muted);

            SmallTextRenderer.Draw(canvas, "ZAINSTALOWANE", x + width - 190, y + 14, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)registry.AppCenterCount, x + width - 70, y + 14, Text);
        }

        private void RenderTabs(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 111;
            int totalWidth = Window.Width - 36;
            const int gap = 7;
            int tabWidth = (totalWidth - gap * 3) / 4;

            for (int i = 0; i < CategoryLabels.Length; i++)
            {
                int tx = x + i * (tabWidth + gap);
                bool selected = category == i;
                bool hovered = hoveredTab == i;
                canvas.DrawFilledRectangle(selected ? PanelSelected : hovered ? PanelHover : Panel,
                    tx, y, tabWidth, 37);
                canvas.DrawRectangle(selected || hovered ? SystemTheme.AccentBorder : Border,
                    tx, y, tabWidth, 37);
                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, tx, y + 34, tabWidth, 3);
                SmallTextRenderer.DrawCentered(canvas, CategoryLabels[i], tx + 4, y + 15,
                    tabWidth - 8, selected ? Text : Muted);
            }
        }

        private void RenderSearch(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 157;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Header, x, y, width, 39);
            canvas.DrawRectangle(string.IsNullOrEmpty(searchText) ? Border : SystemTheme.AccentBorder,
                x, y, width, 39);
            IconManager.DrawScaled(canvas, IconType.Search, x + 11, y + 9, 20, 20);
            SmallTextRenderer.Draw(canvas, "SZUKAJ", x + 41, y + 17, Muted);

            if (string.IsNullOrEmpty(searchText))
            {
                SmallTextRenderer.Draw(canvas, "ZACZNIJ PISAC, ABY FILTROWAC APLIKACJE", x + 105, y + 17, Muted);
            }
            else
            {
                SmallTextRenderer.DrawClipped(canvas, searchText, x + 105, y + 17,
                    Math.Max(40, width - 185), Text);
                int caretX = x + 105 + Math.Min(SmallTextRenderer.Width(searchText) + 5, width - 120);
                canvas.DrawFilledRectangle(SystemTheme.Accent, caretX, y + 12, 1, 15);
            }

            SmallTextRenderer.Draw(canvas, "WYNIKI", x + width - 100, y + 17, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)visibleCount, x + width - 42, y + 17, Text);
        }

        private void RenderList(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 207;
            const int width = 640;

            for (int row = 0; row < VisibleRows; row++)
            {
                int visibleIndex = offset + row;
                int ry = y + row * (RowHeight + RowGap);
                bool exists = visibleIndex < visibleCount;
                bool selected = exists && visibleIndex == selectedVisibleIndex;
                bool hovered = exists && row == hoveredRow;

                canvas.DrawFilledRectangle(selected ? PanelSelected : hovered ? PanelHover : Panel,
                    x, ry, width, RowHeight);
                canvas.DrawRectangle(selected || hovered ? SystemTheme.AccentBorder : Border,
                    x, ry, width, RowHeight);

                if (!exists)
                {
                    if (row == 0 && visibleCount == 0)
                        SmallTextRenderer.Draw(canvas, "BRAK APLIKACJI PASUJACYCH DO FILTRA", x + 18, ry + 24, Muted);
                    continue;
                }

                AppDescriptor app = registry.GetAt(visibleIndices[visibleIndex]);
                if (app == null)
                    continue;

                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x, ry + 5, 3, RowHeight - 10);
                IconManager.DrawScaled(canvas, app.Icon, x + 14, ry + 15, 25, 25);
                SmallTextRenderer.DrawClipped(canvas, app.Name, x + 52, ry + 14, 300, Text);
                SmallTextRenderer.DrawClipped(canvas, app.Description, x + 52, ry + 34, 450, Muted);

                int chipX = x + width - 119;
                canvas.DrawFilledRectangle(Color.FromArgb(23, 31, 37), chipX, ry + 13, 104, 30);
                canvas.DrawRectangle(Border, chipX, ry + 13, 104, 30);
                SmallTextRenderer.DrawCentered(canvas, "WBUDOWANA", chipX + 5, ry + 25, 94,
                    selected ? SystemTheme.Accent : Good);
            }
        }

        private void RenderDetails(Canvas canvas)
        {
            int x = Window.X + 671;
            int y = Window.Y + 207;
            int width = Window.Width - 689;
            const int height = 372;
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "SZCZEGOLY", x + 16, y + 17, Text);
            canvas.DrawLine(Border, x + 16, y + 38, x + width - 16, y + 38);

            AppDescriptor app = SelectedApp();
            if (app == null)
            {
                SmallTextRenderer.Draw(canvas, "BRAK WYBRANEJ APLIKACJI", x + 16, y + 62, Muted);
                return;
            }

            IconManager.DrawScaled(canvas, app.Icon, x + 16, y + 55, 46, 46);
            SmallTextRenderer.DrawClipped(canvas, app.Name, x + 76, y + 66,
                Math.Max(50, width - 92), Text);
            SmallTextRenderer.Draw(canvas, "ZONDERQOS BUILT-IN", x + 76, y + 87, Good);

            DrawDetailLine(canvas, x, y + 127, width, "KATEGORIA", CategoryLabel(app.Category), Text);
            DrawDetailLine(canvas, x, y + 158, width, "STATUS", "ZAINSTALOWANA", Good);
            DrawDetailLine(canvas, x, y + 189, width, "ZRODLO", "SYSTEM", Text);
            DrawDetailLine(canvas, x, y + 220, width, "AKCJA", "URUCHOM", SystemTheme.Accent);

            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x + 14, y + 262, width - 28, 82);
            canvas.DrawRectangle(Border, x + 14, y + 262, width - 28, 82);
            SmallTextRenderer.DrawClipped(canvas, app.Description, x + 27, y + 281,
                Math.Max(40, width - 54), Text);
            SmallTextRenderer.DrawClipped(canvas,
                "App Center nie instaluje jeszcze pakietow z sieci.", x + 27, y + 317,
                Math.Max(40, width - 54), Warning);
        }

        private static string CategoryLabel(AppCategory appCategory)
        {
            int index = (int)appCategory;
            if (index < 1 || index >= CategoryLabels.Length)
                return "INNA";
            return CategoryLabels[index];
        }

        private static void DrawDetailLine(Canvas canvas, int x, int y, int width,
            string label, string value, Color valueColor)
        {
            SmallTextRenderer.Draw(canvas, label, x + 16, y, Muted);
            SmallTextRenderer.DrawClipped(canvas, value ?? string.Empty, x + 112, y,
                Math.Max(30, width - 128), valueColor);
        }

        private void RenderActions(Canvas canvas)
        {
            int y = Window.Y + 593;
            DrawAction(canvas, 0, Window.X + 18, y, 200, "URUCHOM", IconType.Play,
                visibleCount > 0 ? SystemTheme.Accent : Muted);
            DrawAction(canvas, 1, Window.X + 228, y, 220, "WYCZYSC FILTR", IconType.Refresh, Text);
            SmallTextRenderer.Draw(canvas, "ENTER URUCHAMIA  |  DELETE CZYSCI FILTR  |  ESC ZAMYKA",
                Window.X + 470, y + 17, Muted);
        }

        private void DrawAction(Canvas canvas, int index, int x, int y, int width, string label,
            IconType icon, Color textColor)
        {
            bool hovered = hoveredAction == index;
            canvas.DrawFilledRectangle(hovered ? PanelHover : Header, x, y, width, 42);
            canvas.DrawRectangle(hovered ? SystemTheme.AccentBorder : Border, x, y, width, 42);
            if (hovered)
                canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 3, 42);
            IconManager.DrawScaled(canvas, icon, x + 12, y + 11, 20, 20);
            SmallTextRenderer.DrawClipped(canvas, label, x + 42, y + 18,
                Math.Max(20, width - 54), textColor);
        }

        private void RenderStatus(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + Window.Height - 42;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), x, y, width, 27);
            canvas.DrawRectangle(Border, x, y, width, 27);
            canvas.DrawFilledRectangle(statusColor, x + 10, y + 11, 5, 5);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, x + 25, y + 11,
                Math.Max(40, width - 35), statusColor);
        }

        private void UpdateHover(int mouseX, int mouseY)
        {
            hoveredTab = -1;
            hoveredRow = -1;
            hoveredAction = -1;

            int tabsX = Window.X + 18;
            int tabsY = Window.Y + 111;
            int totalWidth = Window.Width - 36;
            const int gap = 7;
            int tabWidth = (totalWidth - gap * 3) / 4;
            for (int i = 0; i < CategoryLabels.Length; i++)
            {
                int tx = tabsX + i * (tabWidth + gap);
                if (Hit(mouseX, mouseY, tx, tabsY, tabWidth, 37))
                {
                    hoveredTab = i;
                    return;
                }
            }

            int listX = Window.X + 18;
            int listY = Window.Y + 207;
            for (int row = 0; row < VisibleRows; row++)
            {
                int visibleIndex = offset + row;
                if (visibleIndex >= visibleCount)
                    break;
                int ry = listY + row * (RowHeight + RowGap);
                if (Hit(mouseX, mouseY, listX, ry, 640, RowHeight))
                {
                    hoveredRow = row;
                    return;
                }
            }

            int actionY = Window.Y + 593;
            if (Hit(mouseX, mouseY, Window.X + 18, actionY, 200, 42))
                hoveredAction = 0;
            else if (Hit(mouseX, mouseY, Window.X + 228, actionY, 220, 42))
                hoveredAction = 1;
        }

        private void RebuildFilter()
        {
            visibleCount = 0;
            for (int i = 0; i < registry.Count; i++)
            {
                AppDescriptor app = registry.GetAt(i);
                if (app == null || !app.ShowInAppCenter)
                    continue;
                if (category != 0 && (int)app.Category != category)
                    continue;
                if (!string.IsNullOrEmpty(searchText) &&
                    app.Name.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0 &&
                    app.Description.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (visibleCount < visibleIndices.Length)
                    visibleIndices[visibleCount++] = i;
            }

            if (visibleCount == 0)
            {
                selectedVisibleIndex = 0;
                offset = 0;
                return;
            }

            if (selectedVisibleIndex >= visibleCount)
                selectedVisibleIndex = visibleCount - 1;
            if (selectedVisibleIndex < 0)
                selectedVisibleIndex = 0;
            KeepSelectionVisible();
        }

        private void ChangeCategory(int delta)
        {
            category += delta;
            if (category < 0)
                category = CategoryLabels.Length - 1;
            else if (category >= CategoryLabels.Length)
                category = 0;
            selectedVisibleIndex = 0;
            offset = 0;
            RebuildFilter();
            SetStatus("ZMIENIONO KATEGORIE", SystemTheme.Accent);
        }

        private void MoveSelection(int delta)
        {
            if (visibleCount <= 0)
                return;
            selectedVisibleIndex += delta;
            if (selectedVisibleIndex < 0)
                selectedVisibleIndex = visibleCount - 1;
            else if (selectedVisibleIndex >= visibleCount)
                selectedVisibleIndex = 0;
            KeepSelectionVisible();
        }

        private void KeepSelectionVisible()
        {
            if (selectedVisibleIndex < offset)
                offset = selectedVisibleIndex;
            else if (selectedVisibleIndex >= offset + VisibleRows)
                offset = selectedVisibleIndex - VisibleRows + 1;

            int maxOffset = Math.Max(0, visibleCount - VisibleRows);
            if (offset > maxOffset)
                offset = maxOffset;
            if (offset < 0)
                offset = 0;
        }

        private AppDescriptor SelectedApp()
        {
            if (visibleCount <= 0 || selectedVisibleIndex < 0 || selectedVisibleIndex >= visibleCount)
                return null;
            return registry.GetAt(visibleIndices[selectedVisibleIndex]);
        }

        private void LaunchSelected()
        {
            AppDescriptor app = SelectedApp();
            if (app == null)
            {
                SetStatus("BRAK APLIKACJI DO URUCHOMIENIA", Warning);
                return;
            }

            if (!app.Launch())
            {
                SetStatus("APLIKACJA NIE MA PODPIETEGO STARTERA", Warning);
                return;
            }

            SetStatus("URUCHOMIONO: " + app.Name, Good);
        }

        private void ResetFilter()
        {
            category = 0;
            searchText = string.Empty;
            selectedVisibleIndex = 0;
            offset = 0;
            RebuildFilter();
            SetStatus("WYCZYSZCZONO FILTR", Good);
        }

        private void SetStatus(string message, Color color)
        {
            statusMessage = message ?? "GOTOWE";
            statusColor = color;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }
    }
}
