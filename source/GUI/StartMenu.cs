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
        private bool wasDrawnOpen;
        private int openingFrames;

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

        // The 560px design fits 800x600, but a 640x480 desktop gives Start
        // only 420px. Keep both result grids above the footer in that mode.
        // Render and hit-testing use the same Button geometry.
        private bool CompactLayout => Height < 530;
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

            // Tools use a stable three-column, two-row grid. This keeps six system tools
            // inside the menu instead of shrinking them until labels/icons overlap.
            const int toolColumns = 3;
            const int toolGap = 8;
            int toolWidth = Math.Max(100,
                (Width - Padding * 2 - toolGap * (toolColumns - 1)) / toolColumns);
            int toolIndex = 0;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                bool matches = Matches(toolLabels[i]);
                button.Visible = matches;
                if (!matches)
                    continue;

                int row = toolIndex / toolColumns;
                int column = toolIndex % toolColumns;
                button.X = X + Padding + column * (toolWidth + toolGap);
                button.Y = Y + ToolsCardsY + row * (ToolsCardsHeight + ToolsCardsGap);
                button.Width = toolWidth;
                button.Height = ToolsCardsHeight;
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

            RenderHeader(canvas);
            RenderSearch(canvas);
            RenderPinned(canvas);
            RenderTools(canvas);
            RenderFooter(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            UiPainter.RoundedTop(canvas, X + 1, Y + 1, Width - 2,
                HeaderHeight - 1, UiMetrics.MenuRadius - 1, Header);
            canvas.DrawFilledRectangle(Accent, X + 12, Y + 3,
                Math.Max(1, (Width - 24) * (8 - openingFrames) / 8), 2);
            if (openingFrames > 0)
                openingFrames--;
            IconManager.DrawScaled(canvas, IconType.Start, X + 17, Y + 13, 30, 30);
            canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, X + 58, Y + 7);
            SmallTextRenderer.Draw(canvas, "COSMOS GEN3 DESKTOP", X + 60, Y + 43, Muted);
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

                bool highlighted = button.IsHovered || selectedResult == visibleCount;
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
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                if (!button.Visible)
                    continue;

                bool highlighted = button.IsHovered || selectedResult == visibleIndex;
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

            IconManager.DrawScaled(canvas, IconType.Start, X + Padding, footerY + 21, 24, 24);
            string currentUser = SecurityContext.CurrentUser;
            if (string.IsNullOrEmpty(currentUser))
                currentUser = "USER";
            SmallTextRenderer.DrawClipped(canvas, currentUser, X + Padding + 34, footerY + 23, 58, Text);
            SmallTextRenderer.Draw(canvas, "LOCK", X + Padding + 34, footerY + 38, Muted);
            int autoLock = global::ZonderqOS.SystemSettings.AutoLockMinutes;
            if (autoLock <= 0)
            {
                SmallTextRenderer.Draw(canvas, "OFF", X + Padding + 62, footerY + 38, Muted);
            }
            else
            {
                SmallTextRenderer.DrawUInt(canvas, (ulong)autoLock, X + Padding + 62, footerY + 38, Muted);
                SmallTextRenderer.Draw(canvas, "M", X + Padding + 62 + SmallTextRenderer.WidthUInt((ulong)autoLock) + 3,
                    footerY + 38, Muted);
            }

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
                }
                UpdateLayout();
                return true;
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
                    selectedResult = 0;
                else if (key.Key == ConsoleKeyEx.End)
                    selectedResult = count - 1;
                else if (key.Key == ConsoleKeyEx.DownArrow)
                    selectedResult = selectedResult < 0 ? 0 : (selectedResult + 1) % count;
                else
                    selectedResult = selectedResult < 0 ? count - 1 : (selectedResult + count - 1) % count;
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

            UpdateLayout();
            UpdateButtonInteractions(pinnedButtons, mouseX, mouseY, isClicked, wasClicked);
            UpdateButtonInteractions(toolButtons, mouseX, mouseY, isClicked, wasClicked);
            UpdatePowerInteraction(lockButton, mouseX, mouseY, isClicked, wasClicked);
            UpdatePowerInteraction(exitButton, mouseX, mouseY, isClicked, wasClicked);
            UpdatePowerInteraction(rebootButton, mouseX, mouseY, isClicked, wasClicked);
            UpdatePowerInteraction(shutdownButton, mouseX, mouseY, isClicked, wasClicked);
        }

        private static void UpdateButtonInteractions(List<Button> buttons, int mouseX, int mouseY,
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
                    return;
                }
            }
        }

        private static void UpdatePowerInteraction(Button button, int mouseX, int mouseY,
            bool isClicked, bool wasClicked)
        {
            if (button == null)
                return;

            bool over = button.Contains(mouseX, mouseY);
            button.IsHovered = over;
            if (over && isClicked && !wasClicked)
                button.InvokeClick();
        }
    }
}
