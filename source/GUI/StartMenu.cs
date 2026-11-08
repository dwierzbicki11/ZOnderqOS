using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Modern fixed-layout Start menu. All buttons/icons are created once during GUI
    /// setup. Rendering reuses the same objects and icon pixel caches, so leaving Start
    /// open does not continuously create managed objects.
    /// </summary>
    public class StartMenu : Widget
    {
        private readonly List<Button> pinnedButtons = new List<Button>(6);
        private readonly List<string> pinnedLabels = new List<string>(6);
        private readonly List<IconType> pinnedIcons = new List<IconType>(6);

        private readonly List<Button> toolButtons = new List<Button>(6);
        private readonly List<string> toolLabels = new List<string>(6);
        private readonly List<IconType> toolIcons = new List<IconType>(6);

        private Button lockButton;
        private Button exitButton;
        private Button rebootButton;
        private Button shutdownButton;
        private string searchText = string.Empty;
        // Index into the currently filtered pinned + tools results (-1 = none).
        // Keyboard selection is independent from pointer hover.
        private int selectedResult = -1;
        // Pointer movement, not a stationary hover, returns selection to mouse.
        private int lastPointerX = int.MinValue;
        private int lastPointerY = int.MinValue;
        private bool wasDrawnOpen;
        private int openingFrames;
        // Tiny displays show a page of matching Start entries at a time.
        // Paging never changes the keyboard selection of a hidden button.
        private int tinyFirstResult;
        // -1 means the full desktop grid was last used; otherwise this
        // remembers the visible tiny page size across mode changes.
        private int lastTinyCapacity = -1;

        // Tools Scrollbar state
        private int toolScrollRow;
        private int toolTotalRows;
        private int toolVisibleRows = 2;
        private bool scrollThumbHovered;
        private bool scrollTrackHovered;

        private const int Padding = 14;
        private const int HeaderHeight = 66;
        private const int SearchTop = 76;
        private const int SearchHeight = 38;
        private const int PinnedLabelTop = 128;
        private const int PinnedTop = 146;
        private const int PinnedColumns = 3;
        private const int PinnedCardHeight = 88;
        private const int PinnedGap = 9;
        private const int ToolsLabelTop = 342;
        private const int ToolsTop = 360;
        private const int ToolHeight = 42;
        private const int FooterHeight = 76;
        private const int TinyFooterHeight = 48;
        private const int TinyResultsTop = 70;
        private const int TinyRowHeight = 42;
        private const int TinyRowGap = 4;
        private const int TinyColumnGap = 6;

        // The 560px design fits 800x600, but a 640x480 desktop gives Start
        // only 420px. Keep both result grids above the footer in that mode.
        // Render and hit-testing use the same Button geometry.
        private bool CompactLayout => Height < 530;
        // Below 400px the normal two-grid layout overlaps the power footer.
        // A compact searchable result strip replaces the overlapping controls.
        private bool TinyLayout => Height < 400 || Width < 320;
        private int TinyColumns => Width >= 270 ? 3 : Width >= 160 ? 2 : 1;
        private int TinyRows => Math.Max(0, (Height - TinyFooterHeight - TinyResultsTop - 6 + TinyRowGap) /
            (TinyRowHeight + TinyRowGap));
        private int PinnedLabelY => CompactLayout ? 118 : PinnedLabelTop;
        private int PinnedCardsY => CompactLayout ? 134 : PinnedTop;
        private int PinnedCardsHeight => CompactLayout ? 53 : PinnedCardHeight;
        private int PinnedCardsGap => CompactLayout ? 5 : PinnedGap;
        private int ToolsLabelY => CompactLayout ? 250 : ToolsLabelTop;
        private int ToolsCardsY => CompactLayout ? 264 : ToolsTop;
        private int ToolsCardsHeight => CompactLayout ? 30 : ToolHeight;
        private int ToolsCardsGap => CompactLayout ? 4 : 8;

        private static readonly Color Background = Color.FromArgb(24, 29, 35);
        private static readonly Color Header = Color.FromArgb(29, 36, 44);
        private static readonly Color Panel = Color.FromArgb(31, 38, 46);
        private static readonly Color PanelHover = Color.FromArgb(40, 55, 68);
        private static readonly Color Border = Color.FromArgb(59, 72, 84);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);

        private static Color Accent
        {
            get { return SystemTheme.Accent; }
        }

        public StartMenu(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = false;
        }

        public void AddPinned(string text, IconType icon, Action onClick)
        {
            Button button = CreateMenuButton(onClick);
            pinnedButtons.Add(button);
            pinnedLabels.Add(text ?? string.Empty);
            pinnedIcons.Add(icon);
            UpdateLayout();
        }

        public void AddTool(string text, IconType icon, Action onClick)
        {
            Button button = CreateMenuButton(onClick);
            toolButtons.Add(button);
            toolLabels.Add(text ?? string.Empty);
            toolIcons.Add(icon);
            UpdateLayout();
        }

        public void AddItem(string text, Action onClick)
        {
            AddPinned(text, IconType.File, onClick);
        }

        public void AddItem(string text, IconType icon, Action onClick)
        {
            AddPinned(text, icon, onClick);
        }

        public void SetPowerActions(Action reboot, Action shutdown, Action logout)
        {
            rebootButton = CreatePowerButton(reboot);
            shutdownButton = CreatePowerButton(shutdown);
            exitButton = CreatePowerButton(logout);
            UpdateLayout();
        }

        public void SetLockAction(Action lockAction)
        {
            lockButton = CreatePowerButton(lockAction);
            UpdateLayout();
        }

        public void ResetSearch()
        {
            searchText = string.Empty;
            selectedResult = -1;
            tinyFirstResult = 0;
            toolScrollRow = 0;
            lastPointerX = int.MinValue;
            lastPointerY = int.MinValue;
            UpdateLayout();
        }

        private Button CreateMenuButton(Action onClick)
        {
            return new Button(0, 0, 80, 40, string.Empty, () =>
            {
                Visible = false;
                onClick?.Invoke();
            });
        }

        private Button CreatePowerButton(Action onClick)
        {
            return new Button(0, 0, 82, 44, string.Empty, () =>
            {
                Visible = false;
                onClick?.Invoke();
            });
        }

        private bool Matches(string label)
        {
            return string.IsNullOrEmpty(searchText) ||
                   (!string.IsNullOrEmpty(label) && label.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private int PinnedCardWidth
        {
            get
            {
                int available = Width - Padding * 2 - PinnedGap * (PinnedColumns - 1);
                return Math.Max(86, available / PinnedColumns);
            }
        }

        private void UpdateLayout()
        {
            if (TinyLayout)
            {
                UpdateTinyLayout();
                return;
            }

            // Tiny pages use page-local selection. Expand it back to a
            // global filtered index before returning to the full grid.
            if (lastTinyCapacity >= 0)
            {
                if (selectedResult >= 0)
                    selectedResult += tinyFirstResult;
                tinyFirstResult = 0;
                lastTinyCapacity = -1;
            }

            SetPowerVisibility(true);
            int cardWidth = PinnedCardWidth;
            int visiblePinned = 0;
            for (int i = 0; i < pinnedButtons.Count; i++)
            {
                Button button = pinnedButtons[i];
                bool matches = Matches(pinnedLabels[i]);
                button.Visible = matches;
                if (!matches)
                    continue;

                int row = visiblePinned / PinnedColumns;
                int column = visiblePinned % PinnedColumns;
                button.X = X + Padding + column * (cardWidth + PinnedGap);
                button.Y = Y + PinnedCardsY + row * (PinnedCardsHeight + PinnedCardsGap);
                button.Width = cardWidth;
                button.Height = PinnedCardsHeight;
                visiblePinned++;
            }

            // Tools use a scrollable three-column grid with scrollbar when rows exceed visible capacity.
            const int toolColumns = 3;
            const int toolGap = 8;
            int maxAvailableToolsHeight = Height - FooterHeight - ToolsCardsY - 6;
            toolVisibleRows = Math.Max(1, (maxAvailableToolsHeight + ToolsCardsGap) / (ToolsCardsHeight + ToolsCardsGap));

            int matchingToolCount = 0;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                if (Matches(toolLabels[i]))
                    matchingToolCount++;
            }
            toolTotalRows = (matchingToolCount + toolColumns - 1) / toolColumns;
            bool hasToolScroll = toolTotalRows > toolVisibleRows;

            if (!hasToolScroll || toolScrollRow > toolTotalRows - toolVisibleRows)
                toolScrollRow = Math.Max(0, Math.Min(toolScrollRow, Math.Max(0, toolTotalRows - toolVisibleRows)));

            int scrollReserved = hasToolScroll ? 14 : 0;
            int toolWidth = Math.Max(60,
                (Width - Padding * 2 - scrollReserved - toolGap * (toolColumns - 1)) / toolColumns);

            int toolIndex = 0;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                bool matches = Matches(toolLabels[i]);
                if (!matches)
                {
                    button.Visible = false;
                    continue;
                }

                int absRow = toolIndex / toolColumns;
                int column = toolIndex % toolColumns;
                int row = absRow - toolScrollRow;

                if (row >= 0 && row < toolVisibleRows)
                {
                    button.Visible = true;
                    button.X = X + Padding + column * (toolWidth + toolGap);
                    button.Y = Y + ToolsCardsY + row * (ToolsCardsHeight + ToolsCardsGap);
                    button.Width = toolWidth;
                    button.Height = ToolsCardsHeight;
                }
                else
                {
                    button.Visible = false;
                }
                toolIndex++;
            }

            // Four compact session/power controls fit beside the footer identity block.
            // Preserve the identity block when the menu is narrower than 480px.
            // Four icon-only controls remain independently clickable on 320px+.
            int powerGap = Width < 480 ? 6 : 8;
            int powerWidth = Width < 480
                ? Math.Max(24, (Width - Padding * 2 - 110 - powerGap * 3) / 4)
                : 82;
            int powerY = Y + Height - FooterHeight + 16;
            int powerRight = X + Width - Padding;

            if (shutdownButton != null)
            {
                shutdownButton.X = powerRight - powerWidth;
                shutdownButton.Y = powerY;
                shutdownButton.Width = powerWidth;
                shutdownButton.Height = 44;
            }
            if (rebootButton != null)
            {
                rebootButton.X = powerRight - powerWidth * 2 - powerGap;
                rebootButton.Y = powerY;
                rebootButton.Width = powerWidth;
                rebootButton.Height = 44;
            }
            if (exitButton != null)
            {
                exitButton.X = powerRight - powerWidth * 3 - powerGap * 2;
                exitButton.Y = powerY;
                exitButton.Width = powerWidth;
                exitButton.Height = 44;
            }
            if (lockButton != null)
            {
                lockButton.X = powerRight - powerWidth * 4 - powerGap * 3;
                lockButton.Y = powerY;
                lockButton.Width = powerWidth;
                lockButton.Height = 44;
            }
        }

        private void SetPowerVisibility(bool visible)
        {
            if (lockButton != null) lockButton.Visible = visible;
            if (exitButton != null) exitButton.Visible = visible;
            if (rebootButton != null) rebootButton.Visible = visible;
            if (shutdownButton != null) shutdownButton.Visible = visible;
        }

        private int CountMatchingResults()
        {
            int count = 0;
            for (int i = 0; i < pinnedLabels.Count; i++)
                if (Matches(pinnedLabels[i])) count++;
            for (int i = 0; i < toolLabels.Count; i++)
                if (Matches(toolLabels[i])) count++;
            return count;
        }

        private void ChangeTinyPage(int direction)
        {
            int capacity = TinyColumns * TinyRows;
            if (capacity <= 0)
                return;
            int total = CountMatchingResults();
            int pages = Math.Max(1, (total + capacity - 1) / capacity);
            int current = tinyFirstResult / capacity;
            tinyFirstResult = ((current + direction + pages) % pages) * capacity;
            selectedResult = -1;
            UpdateLayout();
            UiAnimator.RequestFrames();
        }

        private void UpdateTinyLayout()
        {
            // The same clipped Button rectangles drive both rendering and
            // hit-testing. No application control may overlap the power row.
            int columns = TinyColumns;
            int rows = TinyRows;
            int capacity = columns * rows;
            int total = CountMatchingResults();
            if (capacity != lastTinyCapacity)
            {
                // Preserve the selected APPLICATION, not its old column
                // number. Reflowing a 3-column page to 2 columns must not
                // make Enter launch the neighboring tile.
                int selectedGlobal = selectedResult >= 0
                    ? tinyFirstResult + selectedResult : -1;
                if (capacity > 0 && selectedGlobal >= 0 && selectedGlobal < total)
                {
                    tinyFirstResult = (selectedGlobal / capacity) * capacity;
                    selectedResult = selectedGlobal - tinyFirstResult;
                }
                else if (capacity <= 0)
                    selectedResult = -1;
                lastTinyCapacity = capacity;
            }
            if (capacity <= 0 || total == 0)
                tinyFirstResult = 0;
            else
            {
                // Re-align when the framebuffer changes the number of
                // columns/rows. A stale page offset must not hide a tile.
                tinyFirstResult = (tinyFirstResult / capacity) * capacity;
                if (tinyFirstResult >= total)
                    tinyFirstResult = ((total - 1) / capacity) * capacity;
            }
            int buttonWidth = Math.Max(1, (Width - Padding * 2 - (columns - 1) * TinyColumnGap) / columns);
            int matchIndex = 0;
            for (int group = 0; group < 2; group++)
            {
                List<Button> buttons = group == 0 ? pinnedButtons : toolButtons;
                List<string> labels = group == 0 ? pinnedLabels : toolLabels;
                for (int i = 0; i < buttons.Count; i++)
                {
                    Button button = buttons[i];
                    bool matches = Matches(labels[i]);
                    bool show = matches && matchIndex >= tinyFirstResult &&
                        matchIndex < tinyFirstResult + capacity;
                    button.Visible = show;
                    if (matches)
                        matchIndex++;
                    if (!show)
                        continue;
                    int index = matchIndex - tinyFirstResult - 1;
                    button.X = X + Padding + (index % columns) * (buttonWidth + TinyColumnGap);
                    button.Y = Y + TinyResultsTop + (index / columns) * (TinyRowHeight + TinyRowGap);
                    button.Width = buttonWidth;
                    button.Height = TinyRowHeight;
                }
            }

            // A resolution change can shrink the page from three columns
            // to two (or remove the last row). Never retain keyboard focus
            // on a tile that was just hidden by the new viewport.
            int visibleResults = Math.Min(capacity, Math.Max(0, total - tinyFirstResult));
            if (visibleResults == 0)
                selectedResult = -1;
            else if (selectedResult >= visibleResults)
                selectedResult = visibleResults - 1;

            // Keep the four session actions separate and fully inside the
            // panel. On impossibly narrow firmware modes hide them rather
            // than exposing invisible/overlapping power hitboxes.
            bool powerFits = Width >= 160 && Height >= TinyFooterHeight;
            SetPowerVisibility(powerFits);
            int gap = 6;
            int width = Math.Max(1, (Width - Padding * 2 - 3 * gap) / 4);
            int right = X + Width - Padding;
            int y = Y + Height - TinyFooterHeight + 6;
            // Layout runs on every render and pointer event. Do not allocate
            // a temporary Button[] on the hot GUI path.
            PlaceTinyPower(shutdownButton, 0, width, gap, right, y);
            PlaceTinyPower(rebootButton, 1, width, gap, right, y);
            PlaceTinyPower(exitButton, 2, width, gap, right, y);
            PlaceTinyPower(lockButton, 3, width, gap, right, y);
        }

        private static void PlaceTinyPower(Button button, int index, int width,
            int gap, int right, int y)
        {
            if (button == null)
                return;
            button.X = right - (index + 1) * width - index * gap;
            button.Y = y;
            button.Width = width;
            button.Height = 36;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
            {
                wasDrawnOpen = false;
                return;
            }

            if (!wasDrawnOpen)
            {
                openingFrames = 8;
                wasDrawnOpen = true;
                UiAnimator.RequestFrames(8);
            }
            UpdateLayout();

            UiPainter.Shadow(canvas, X, Y, Width, Height, UiMetrics.MenuRadius);
            UiPainter.RoundedPanel(canvas, X, Y, Width, Height,
                UiMetrics.MenuRadius, UiPalette.Surface, UiPalette.Border);

            if (TinyLayout)
            {
                RenderTiny(canvas);
                return;
            }

            RenderHeader(canvas);
            RenderSearch(canvas);
            RenderPinned(canvas);
            RenderTools(canvas);
            RenderFooter(canvas);
        }

        private void RenderTiny(Canvas canvas)
        {
            // A 320x240 framebuffer leaves a 304x180 menu. The regular
            // header/search and footer would overlap the pinned cards.
            SmallTextRenderer.DrawClipped(canvas, "START", X + 12, Y + 9,
                Math.Max(1, Width - 24), Text);
            if (TinyColumns * TinyRows > 0 && CountMatchingResults() > TinyColumns * TinyRows && Width >= 160)
            {
                SmallTextRenderer.Draw(canvas, "<<", X + Width - 68, Y + 9, Muted);
                SmallTextRenderer.Draw(canvas, ">>", X + Width - 36, Y + 9, Muted);
            }
            int searchWidth = Math.Max(1, Width - 24);
            UiPainter.RoundedPanel(canvas, X + 12, Y + 30, searchWidth, 30,
                UiMetrics.ButtonRadius, UiPalette.SurfaceRaised, UiPalette.Border);
            SmallTextRenderer.DrawClipped(canvas,
                string.IsNullOrEmpty(searchText) ? "Szukaj aplikacji..." : searchText,
                X + 20, Y + 40, Math.Max(1, searchWidth - 16),
                string.IsNullOrEmpty(searchText) ? Muted : Text);

            int index = 0;
            for (int group = 0; group < 2; group++)
            {
                List<Button> buttons = group == 0 ? pinnedButtons : toolButtons;
                List<string> labels = group == 0 ? pinnedLabels : toolLabels;
                List<IconType> icons = group == 0 ? pinnedIcons : toolIcons;
                for (int i = 0; i < buttons.Count; i++)
                {
                    Button button = buttons[i];
                    if (!button.Visible)
                        continue;
                    bool highlighted = selectedResult >= 0
                        ? selectedResult == index : button.IsHovered;
                    UiPainter.RoundedPanel(canvas, button.X, button.Y,
                        button.Width, button.Height, UiMetrics.TileRadius,
                        highlighted ? PanelHover : Panel,
                        highlighted ? SystemTheme.AccentBorder : Border);
                    IconManager.DrawScaled(canvas, icons[i], button.X + 7,
                        button.Y + 11, 18, 18);
                    SmallTextRenderer.DrawClipped(canvas, labels[i], button.X + 29,
                        button.Y + 17, Math.Max(1, button.Width - 34), Text);
                    index++;
                }
            }

            int footerY = Y + Height - TinyFooterHeight;
            UiPainter.RoundedFill(canvas, X + 2, footerY, Width - 4,
                TinyFooterHeight - 2, UiMetrics.ButtonRadius, Header);
            if (lockButton != null && lockButton.Visible)
                RenderPowerButton(canvas, lockButton, IconType.Settings, "BLOKUJ", false);
            if (exitButton != null && exitButton.Visible)
                RenderPowerButton(canvas, exitButton, IconType.Close, "WYLOGUJ", false);
            if (rebootButton != null && rebootButton.Visible)
                RenderPowerButton(canvas, rebootButton, IconType.Reboot, "REBOOT", false);
            if (shutdownButton != null && shutdownButton.Visible)
                RenderPowerButton(canvas, shutdownButton, IconType.Shutdown, "WYLACZ", true);
        }

        private void RenderHeader(Canvas canvas)
        {
            UiPainter.RoundedTop(canvas, X + 1, Y + 1, Width - 2,
                HeaderHeight - 1, UiMetrics.MenuRadius - 1, Header);
            canvas.DrawFilledRectangle(Accent, X + 12, Y + 3,
                Math.Max(1, (Width - 24) * (8 - openingFrames) / 8), 2);
            if (openingFrames > 0)
                openingFrames--;

            // Modern Windows & Zorin 4-tile emblem
            int emblemX = X + 18;
            int emblemY = Y + 16;
            canvas.DrawFilledRectangle(Accent, emblemX, emblemY, 11, 11);
            canvas.DrawFilledRectangle(SystemTheme.AccentBorder, emblemX + 14, emblemY, 11, 11);
            canvas.DrawFilledRectangle(SystemTheme.AccentBorder, emblemX, emblemY + 14, 11, 11);
            canvas.DrawFilledRectangle(Accent, emblemX + 14, emblemY + 14, 11, 11);

            canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, X + 60, Y + 7);
            SmallTextRenderer.Draw(canvas, "ZORIN & WINDOWS FLUENT DESKTOP", X + 62, Y + 43, Muted);
            canvas.DrawLine(Color.FromArgb(49, 61, 72), X + 1, Y + HeaderHeight, X + Width - 2, Y + HeaderHeight);
        }

        private void RenderSearch(Canvas canvas)
        {
            int searchX = X + Padding;
            int searchY = Y + SearchTop;
            int searchWidth = Width - Padding * 2;
            UiPainter.RoundedPanel(canvas, searchX, searchY, searchWidth, SearchHeight,
                UiMetrics.ButtonRadius, UiPalette.SurfaceRaised,
                string.IsNullOrEmpty(searchText) ? UiPalette.Border : Accent);
            IconManager.DrawScaled(canvas, IconType.Search, searchX + 10, searchY + 9, 20, 20);

            if (string.IsNullOrEmpty(searchText))
            {
                SmallTextRenderer.Draw(canvas, "Szukaj aplikacji...", searchX + 40, searchY + 16, Muted);
            }
            else
            {
                SmallTextRenderer.DrawClipped(canvas, searchText, searchX + 40, searchY + 16,
                    Math.Max(20, searchWidth - 56), Text);
                int caretX = searchX + 40 + Math.Min(SmallTextRenderer.Width(searchText) + 5, searchWidth - 14);
                canvas.DrawFilledRectangle(Accent, caretX, searchY + 12, 1, 14);
            }
        }

        private void RenderPinned(Canvas canvas)
        {
            SmallTextRenderer.Draw(canvas, "PRZYPINANE", X + Padding, Y + PinnedLabelY, Muted);

            int visibleCount = 0;
            for (int i = 0; i < pinnedButtons.Count; i++)
            {
                Button button = pinnedButtons[i];
                if (!button.Visible)
                    continue;

                bool highlighted = selectedResult >= 0
                    ? selectedResult == visibleCount : button.IsHovered;
                Color background = highlighted ? PanelHover : Panel;
                Color border = highlighted ? SystemTheme.AccentBorder : Border;
                UiPainter.RoundedPanel(canvas, button.X, button.Y, button.Width, button.Height,
                    UiMetrics.TileRadius, background, border);
                if (highlighted)
                    canvas.DrawFilledRectangle(Accent, button.X, button.Y, button.Width, 2);

                int iconSize = CompactLayout ? 24 : 32;
                int iconX = button.X + (button.Width - iconSize) / 2;
                IconManager.DrawScaled(canvas, pinnedIcons[i], iconX,
                    button.Y + (CompactLayout ? 5 : 13), iconSize, iconSize);
                SmallTextRenderer.DrawCentered(canvas, pinnedLabels[i], button.X + 7,
                    button.Y + (CompactLayout ? 33 : 56),
                    Math.Max(20, button.Width - 14), Text);
                visibleCount++;
            }

            if (visibleCount == 0)
                SmallTextRenderer.Draw(canvas, "BRAK WYNIKOW", X + Padding, Y + PinnedCardsY + 18, Muted);
        }

        private void RenderTools(Canvas canvas)
        {
            // The tools continue the same filtered keyboard index after pinned items.
            int visibleIndex = 0;
            for (int i = 0; i < pinnedButtons.Count; i++)
                if (pinnedButtons[i].Visible)
                    visibleIndex++;
            SmallTextRenderer.Draw(canvas, "NARZEDZIA", X + Padding, Y + ToolsLabelY, Muted);

            // Tools Scrollbar and Page Controls if needed
            if (!TinyLayout && toolTotalRows > toolVisibleRows)
            {
                int controlsX = X + Width - Padding - 58;
                SmallTextRenderer.Draw(canvas, "▲", controlsX, Y + ToolsLabelY, toolScrollRow > 0 ? SystemTheme.Accent : Muted);
                string pageInfo = (toolScrollRow + 1) + "/" + toolTotalRows;
                SmallTextRenderer.DrawCentered(canvas, pageInfo, controlsX + 12, Y + ToolsLabelY, 32, Muted);
                SmallTextRenderer.Draw(canvas, "▼", controlsX + 46, Y + ToolsLabelY, toolScrollRow < toolTotalRows - toolVisibleRows ? SystemTheme.Accent : Muted);

                // Vertical scrollbar track and thumb on right edge
                int scrollX = X + Width - Padding - 8;
                int scrollY = Y + ToolsCardsY;
                int scrollH = toolVisibleRows * ToolsCardsHeight + (toolVisibleRows - 1) * ToolsCardsGap;

                Color trackBg = scrollTrackHovered ? Color.FromArgb(28, 36, 46) : Color.FromArgb(20, 26, 33);
                UiPainter.RoundedPanel(canvas, scrollX, scrollY, 6, scrollH, 3, trackBg, Color.FromArgb(40, 50, 62));

                int thumbH = Math.Max(16, (scrollH * toolVisibleRows) / toolTotalRows);
                int maxTravel = scrollH - thumbH;
                int thumbY = scrollY + (toolTotalRows > toolVisibleRows ? (maxTravel * toolScrollRow) / (toolTotalRows - toolVisibleRows) : 0);
                Color thumbColor = scrollThumbHovered ? SystemTheme.Accent : SystemTheme.AccentSoft;
                canvas.DrawFilledRectangle(thumbColor, scrollX + 1, thumbY, 4, thumbH);
            }
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                if (!button.Visible)
                    continue;

                bool highlighted = selectedResult >= 0
                    ? selectedResult == visibleIndex : button.IsHovered;
                UiPainter.RoundedPanel(canvas, button.X, button.Y, button.Width, button.Height,
                    UiMetrics.TileRadius, highlighted ? PanelHover : Panel,
                    highlighted ? UiPalette.AccentBorder : Border);
                int toolIconSize = CompactLayout ? 16 : 20;
                IconManager.DrawScaled(canvas, toolIcons[i], button.X + 9,
                    button.Y + (CompactLayout ? 7 : 10), toolIconSize, toolIconSize);
                SmallTextRenderer.DrawClipped(canvas, toolLabels[i],
                    button.X + (CompactLayout ? 30 : 36),
                    button.Y + (CompactLayout ? 10 : 17),
                    Math.Max(18, button.Width - (CompactLayout ? 37 : 43)), Text);
                visibleIndex++;
            }
        }

        private void RenderFooter(Canvas canvas)
        {
            int footerY = Y + Height - FooterHeight;
            UiPainter.RoundedFill(canvas, X + 2, footerY, Width - 4, FooterHeight - 3,
                UiMetrics.ButtonRadius, Color.FromArgb(20, 27, 36));
            canvas.DrawLine(Color.FromArgb(49, 61, 72), X + 1, footerY, X + Width - 2, footerY);

            // Modern Zorin OS & Windows user avatar
            int avatarX = X + Padding;
            int avatarY = footerY + 20;
            UiPainter.RoundedPanel(canvas, avatarX, avatarY, 26, 26, 6, SystemTheme.AccentSoft, SystemTheme.AccentBorder);
            SmallTextRenderer.DrawCentered(canvas, "Z", avatarX, avatarY + 9, 26, Color.White);

            string currentUser = SecurityContext.CurrentUser;
            if (string.IsNullOrEmpty(currentUser))
                currentUser = "USER";
            SmallTextRenderer.DrawClipped(canvas, currentUser, X + Padding + 34, footerY + 22, 58, Text);
            SmallTextRenderer.Draw(canvas, "Admin", X + Padding + 34, footerY + 38, SystemTheme.Accent);

            if (lockButton != null)
                RenderPowerButton(canvas, lockButton, IconType.Settings, "BLOKUJ", false);
            if (exitButton != null)
                RenderPowerButton(canvas, exitButton, IconType.Close, "WYLOGUJ", false);
            if (rebootButton != null)
                RenderPowerButton(canvas, rebootButton, IconType.Reboot, "REBOOT", false);
            if (shutdownButton != null)
                RenderPowerButton(canvas, shutdownButton, IconType.Shutdown, "WYLACZ", true);
        }

        private static void RenderPowerButton(Canvas canvas, Button button, IconType icon, string label, bool danger)
        {
            Color normal = danger ? Color.FromArgb(57, 38, 43) : Color.FromArgb(31, 39, 47);
            Color hover = danger ? Color.FromArgb(86, 45, 52) : SystemTheme.AccentSoft;
            Color border = danger ? Color.FromArgb(112, 61, 69) : Color.FromArgb(61, 80, 96);
            UiPainter.RoundedPanel(canvas, button.X, button.Y, button.Width, button.Height,
                UiMetrics.ButtonRadius, button.IsHovered ? hover : normal,
                button.IsHovered ? (danger ? UiPalette.DangerBorder : Accent) : border);
            if (button.Width < 72)
            {
                // Narrow screens cannot fit four power labels beside the user.
                IconManager.DrawScaled(canvas, icon, button.X + (button.Width - 20) / 2,
                    button.Y + 11, 20, 20);
            }
            else
            {
                IconManager.DrawScaled(canvas, icon, button.X + 8, button.Y + 11, 20, 20);
                SmallTextRenderer.DrawClipped(canvas, label, button.X + 34, button.Y + 18,
                    Math.Max(16, button.Width - 40), Text);
            }
        }

        private int CountVisibleResults()
        {
            int count = 0;
            for (int i = 0; i < pinnedButtons.Count; i++)
                if (pinnedButtons[i].Visible)
                    count++;
            for (int i = 0; i < toolButtons.Count; i++)
                if (toolButtons[i].Visible)
                    count++;
            return count;
        }

        /// <summary>
        /// Handles Start-menu search only while the menu is visible. String changes
        /// happen only on actual key presses, never during idle frames.
        /// </summary>
        public bool HandleKeyboard(KeyEvent key)
        {
            if (!Visible || key == null)
                return false;

            // Start owns the keyboard while open. Do not treat Ctrl/Alt
            // shortcuts as search text, Backspace edits or Enter launches.
            // Shift remains available for uppercase search characters.
            if ((key.Modifiers & (ConsoleModifiers.Control | ConsoleModifiers.Alt)) != 0)
                return true;

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (searchText.Length > 0)
                {
                    searchText = searchText.Substring(0, searchText.Length - 1);
                    selectedResult = -1;
                    tinyFirstResult = 0;
                }
                UpdateLayout();
                return true;
            }

            // Page through all matching results on a tiny display, even when
            // only three buttons fit above the power controls.
            if (TinyLayout && key.Modifiers == 0 &&
                (key.Key == ConsoleKeyEx.PageUp || key.Key == ConsoleKeyEx.PageDown))
            {
                ChangeTinyPage(key.Key == ConsoleKeyEx.PageDown ? 1 : -1);
                return true;
            }

            if (!TinyLayout && key.Modifiers == 0 &&
                (key.Key == ConsoleKeyEx.PageUp || key.Key == ConsoleKeyEx.PageDown))
            {
                if (key.Key == ConsoleKeyEx.PageDown && toolTotalRows > toolVisibleRows)
                {
                    toolScrollRow = Math.Min(toolTotalRows - toolVisibleRows, toolScrollRow + toolVisibleRows);
                    UpdateLayout();
                    return true;
                }
                else if (key.Key == ConsoleKeyEx.PageUp && toolTotalRows > toolVisibleRows)
                {
                    toolScrollRow = Math.Max(0, toolScrollRow - toolVisibleRows);
                    UpdateLayout();
                    return true;
                }
            }

            // Navigate filtered results without opening a different app on Enter.
            // Home/End select boundaries; Up/Down wrap across pinned and tools.
            if (key.Modifiers == 0 &&
                (key.Key == ConsoleKeyEx.UpArrow || key.Key == ConsoleKeyEx.DownArrow ||
                 key.Key == ConsoleKeyEx.Home || key.Key == ConsoleKeyEx.End))
            {
                UpdateLayout();
                int count = CountVisibleResults();
                if (count == 0)
                    selectedResult = -1;
                else if (key.Key == ConsoleKeyEx.Home)
                {
                    // Home/End are global search-result boundaries, even if
                    // only one tiny page fits in the current framebuffer.
                    if (TinyLayout && tinyFirstResult != 0)
                    {
                        tinyFirstResult = 0;
                        UpdateLayout();
                    }
                    selectedResult = 0;
                }
                else if (key.Key == ConsoleKeyEx.End)
                {
                    if (TinyLayout)
                    {
                        int capacity = TinyColumns * TinyRows;
                        int total = CountMatchingResults();
                        if (capacity > 0 && total > 0)
                        {
                            tinyFirstResult = ((total - 1) / capacity) * capacity;
                            UpdateLayout();
                            count = CountVisibleResults();
                        }
                    }
                    selectedResult = count - 1;
                }
                else if (key.Key == ConsoleKeyEx.DownArrow)
                {
                    // On tiny screens only one page is painted. Crossing its
                    // final tile must reveal the next page, not loop forever
                    // within the same three visible results.
                    if (TinyLayout && selectedResult == count - 1 &&
                        CountMatchingResults() > TinyColumns * TinyRows)
                    {
                        ChangeTinyPage(1);
                        selectedResult = 0;
                    }
                    else
                        selectedResult = selectedResult < 0 ? 0 : (selectedResult + 1) % count;
                }
                else
                {
                    if (TinyLayout && selectedResult == 0 &&
                        CountMatchingResults() > TinyColumns * TinyRows)
                    {
                        ChangeTinyPage(-1);
                        selectedResult = CountVisibleResults() - 1;
                    }
                    else
                        selectedResult = selectedResult < 0 ? count - 1 : (selectedResult + count - 1) % count;
                }
                UiAnimator.RequestFrames();
                return true;
            }

            if (key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.NumEnter)
            {
                // Shift+Enter must not launch the first visible application.
                if (key.Modifiers != 0)
                    return true;

                UpdateLayout();
                int target = selectedResult < 0 ? 0 : selectedResult;
                for (int i = 0; i < pinnedButtons.Count; i++)
                {
                    if (!pinnedButtons[i].Visible)
                        continue;
                    if (target-- == 0)
                    {
                        pinnedButtons[i].InvokeClick();
                        return true;
                    }
                }
                for (int i = 0; i < toolButtons.Count; i++)
                {
                    if (!toolButtons[i].Visible)
                        continue;
                    if (target-- == 0)
                    {
                        toolButtons[i].InvokeClick();
                        return true;
                    }
                }
                return true;
            }

            char ch = key.KeyChar;
            if (ch >= ' ' && ch != 127 && searchText.Length < 40)
            {
                searchText += ch;
                selectedResult = -1;
                tinyFirstResult = 0;
                UpdateLayout();
                return true;
            }

            // Start is modal: navigation and Delete must never fall through
            // to the active application (especially Task Manager).
            return true;
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible)
                return;

            if (lastPointerX != int.MinValue &&
                (mouseX != lastPointerX || mouseY != lastPointerY))
                selectedResult = -1;
            lastPointerX = mouseX;
            lastPointerY = mouseY;

            UpdateLayout();
            // The two tiny-page controls are outside all app and power hitboxes.
            // Consume their press before dispatching the result buttons.
            if (TinyLayout && isClicked && !wasClicked && Width >= 160 &&
                TinyColumns * TinyRows > 0 && CountMatchingResults() > TinyColumns * TinyRows &&
                mouseY >= Y + 4 && mouseY < Y + 27)
            {
                if (mouseX >= X + Width - 68 && mouseX < X + Width - 40)
                {
                    ChangeTinyPage(-1);
                    return;
                }
                if (mouseX >= X + Width - 36 && mouseX < X + Width - 8)
                {
                    ChangeTinyPage(1);
                    return;
                }
            }

            scrollThumbHovered = false;
            scrollTrackHovered = false;
            if (!TinyLayout && toolTotalRows > toolVisibleRows)
            {
                int controlsX = X + Width - Padding - 58;
                if (mouseY >= Y + ToolsLabelY - 4 && mouseY < Y + ToolsLabelY + 16)
                {
                    if (mouseX >= controlsX - 4 && mouseX < controlsX + 16)
                    {
                        if (isClicked && !wasClicked && toolScrollRow > 0)
                        {
                            toolScrollRow--;
                            UpdateLayout();
                            return;
                        }
                    }
                    else if (mouseX >= controlsX + 42 && mouseX < controlsX + 62)
                    {
                        if (isClicked && !wasClicked && toolScrollRow < toolTotalRows - toolVisibleRows)
                        {
                            toolScrollRow++;
                            UpdateLayout();
                            return;
                        }
                    }
                }

                int scrollX = X + Width - Padding - 10;
                int scrollY = Y + ToolsCardsY;
                int scrollH = toolVisibleRows * ToolsCardsHeight + (toolVisibleRows - 1) * ToolsCardsGap;
                if (mouseX >= scrollX - 4 && mouseX < scrollX + 14 && mouseY >= scrollY && mouseY < scrollY + scrollH)
                {
                    scrollTrackHovered = true;
                    int thumbH = Math.Max(16, (scrollH * toolVisibleRows) / toolTotalRows);
                    int maxTravel = scrollH - thumbH;
                    int thumbY = scrollY + (toolTotalRows > toolVisibleRows ? (maxTravel * toolScrollRow) / (toolTotalRows - toolVisibleRows) : 0);
                    if (mouseY >= thumbY && mouseY < thumbY + thumbH)
                    {
                        scrollThumbHovered = true;
                    }

                    if (isClicked && !wasClicked)
                    {
                        if (mouseY < thumbY && toolScrollRow > 0)
                        {
                            toolScrollRow--;
                            UpdateLayout();
                            return;
                        }
                        else if (mouseY > thumbY + thumbH && toolScrollRow < toolTotalRows - toolVisibleRows)
                        {
                            toolScrollRow++;
                            UpdateLayout();
                            return;
                        }
                    }
                }
            }
            // A button callback may launch an app, lock the session or power
            // off the machine. Never dispatch the same press to a second
            // overlapping control (possible on very small framebuffers).
            if (UpdateButtonInteractions(pinnedButtons, mouseX, mouseY, isClicked, wasClicked))
                return;
            if (UpdateButtonInteractions(toolButtons, mouseX, mouseY, isClicked, wasClicked))
                return;
            if (UpdatePowerInteraction(lockButton, mouseX, mouseY, isClicked, wasClicked))
                return;
            if (UpdatePowerInteraction(exitButton, mouseX, mouseY, isClicked, wasClicked))
                return;
            if (UpdatePowerInteraction(rebootButton, mouseX, mouseY, isClicked, wasClicked))
                return;
            UpdatePowerInteraction(shutdownButton, mouseX, mouseY, isClicked, wasClicked);
        }

        private static bool UpdateButtonInteractions(List<Button> buttons, int mouseX, int mouseY,
            bool isClicked, bool wasClicked)
        {
            for (int i = 0; i < buttons.Count; i++)
            {
                Button button = buttons[i];
                if (!button.Visible)
                {
                    button.IsHovered = false;
                    continue;
                }

                bool over = button.Contains(mouseX, mouseY);
                button.IsHovered = over;
                if (over && isClicked && !wasClicked)
                {
                    button.InvokeClick();
                    return true;
                }
            }
            return false;
        }

        private static bool UpdatePowerInteraction(Button button, int mouseX, int mouseY,
            bool isClicked, bool wasClicked)
        {
            if (button == null || !button.Visible)
                return false;

            bool over = button.Contains(mouseX, mouseY);
            button.IsHovered = over;
            if (over && isClicked && !wasClicked)
            {
                button.InvokeClick();
                return true;
            }
            return false;
        }
    }
}
