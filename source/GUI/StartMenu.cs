using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
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

        private readonly List<Button> toolButtons = new List<Button>(4);
        private readonly List<string> toolLabels = new List<string>(4);
        private readonly List<IconType> toolIcons = new List<IconType>(4);

        private Button exitButton;
        private Button rebootButton;
        private Button shutdownButton;
        private string searchText = string.Empty;

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

        private static readonly Color Background = Color.FromArgb(24, 29, 35);
        private static readonly Color Header = Color.FromArgb(29, 36, 44);
        private static readonly Color Panel = Color.FromArgb(31, 38, 46);
        private static readonly Color PanelHover = Color.FromArgb(40, 55, 68);
        private static readonly Color Border = Color.FromArgb(59, 72, 84);
        private static readonly Color Accent = Color.FromArgb(64, 143, 204);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);

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

        // Compatibility with the older Start-menu setup API.
        public void AddItem(string text, Action onClick)
        {
            AddPinned(text, IconType.File, onClick);
        }

        public void AddItem(string text, IconType icon, Action onClick)
        {
            AddPinned(text, icon, onClick);
        }

        public void SetPowerActions(Action reboot, Action shutdown, Action exitGui)
        {
            rebootButton = CreatePowerButton(reboot);
            shutdownButton = CreatePowerButton(shutdown);
            exitButton = CreatePowerButton(exitGui);
            UpdateLayout();
        }

        public void ResetSearch()
        {
            searchText = string.Empty;
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
                button.Y = Y + PinnedTop + row * (PinnedCardHeight + PinnedGap);
                button.Width = cardWidth;
                button.Height = PinnedCardHeight;
                visiblePinned++;
            }

            int visibleTools = 0;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                if (Matches(toolLabels[i]))
                    visibleTools++;
            }

            int toolGap = 8;
            int toolWidth = visibleTools > 0
                ? Math.Max(82, (Width - Padding * 2 - toolGap * Math.Max(0, visibleTools - 1)) / visibleTools)
                : 82;
            int toolIndex = 0;
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                bool matches = Matches(toolLabels[i]);
                button.Visible = matches;
                if (!matches)
                    continue;

                button.X = X + Padding + toolIndex * (toolWidth + toolGap);
                button.Y = Y + ToolsTop;
                button.Width = toolWidth;
                button.Height = ToolHeight;
                toolIndex++;
            }

            int footerY = Y + Height - FooterHeight;
            int powerY = footerY + 16;
            if (shutdownButton != null)
            {
                shutdownButton.X = X + Width - Padding - 92;
                shutdownButton.Y = powerY;
                shutdownButton.Width = 92;
                shutdownButton.Height = 44;
            }
            if (rebootButton != null)
            {
                rebootButton.X = X + Width - Padding - 192;
                rebootButton.Y = powerY;
                rebootButton.Width = 92;
                rebootButton.Height = 44;
            }
            if (exitButton != null)
            {
                exitButton.X = X + Width - Padding - 274;
                exitButton.Y = powerY;
                exitButton.Width = 74;
                exitButton.Height = 44;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateLayout();

            canvas.DrawFilledRectangle(Color.FromArgb(11, 14, 18), X + 7, Y + 8, Width, Height);
            canvas.DrawFilledRectangle(Background, X, Y, Width, Height);
            canvas.DrawRectangle(Border, X, Y, Width, Height);

            RenderHeader(canvas);
            RenderSearch(canvas);
            RenderPinned(canvas);
            RenderTools(canvas);
            RenderFooter(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Header, X + 1, Y + 1, Width - 2, HeaderHeight - 1);
            canvas.DrawFilledRectangle(Accent, X + 1, Y + 1, 4, HeaderHeight - 1);
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
            canvas.DrawFilledRectangle(Color.FromArgb(28, 35, 42), searchX, searchY, searchWidth, SearchHeight);
            canvas.DrawRectangle(string.IsNullOrEmpty(searchText) ? Border : Accent,
                searchX, searchY, searchWidth, SearchHeight);
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
            SmallTextRenderer.Draw(canvas, "PRZYPINANE", X + Padding, Y + PinnedLabelTop, Muted);

            int visibleCount = 0;
            for (int i = 0; i < pinnedButtons.Count; i++)
            {
                Button button = pinnedButtons[i];
                if (!button.Visible)
                    continue;

                Color background = button.IsHovered ? PanelHover : Panel;
                Color border = button.IsHovered ? Color.FromArgb(72, 132, 177) : Border;
                canvas.DrawFilledRectangle(background, button.X, button.Y, button.Width, button.Height);
                canvas.DrawRectangle(border, button.X, button.Y, button.Width, button.Height);
                if (button.IsHovered)
                    canvas.DrawFilledRectangle(Accent, button.X, button.Y, button.Width, 2);

                int iconX = button.X + (button.Width - 32) / 2;
                IconManager.DrawScaled(canvas, pinnedIcons[i], iconX, button.Y + 13, 32, 32);
                SmallTextRenderer.DrawCentered(canvas, pinnedLabels[i], button.X + 7, button.Y + 56,
                    Math.Max(20, button.Width - 14), Text);
                visibleCount++;
            }

            if (visibleCount == 0)
                SmallTextRenderer.Draw(canvas, "BRAK WYNIKOW", X + Padding, Y + PinnedTop + 18, Muted);
        }

        private void RenderTools(Canvas canvas)
        {
            SmallTextRenderer.Draw(canvas, "NARZEDZIA", X + Padding, Y + ToolsLabelTop, Muted);
            for (int i = 0; i < toolButtons.Count; i++)
            {
                Button button = toolButtons[i];
                if (!button.Visible)
                    continue;

                canvas.DrawFilledRectangle(button.IsHovered ? PanelHover : Panel,
                    button.X, button.Y, button.Width, button.Height);
                canvas.DrawRectangle(button.IsHovered ? Color.FromArgb(72, 132, 177) : Border,
                    button.X, button.Y, button.Width, button.Height);
                IconManager.DrawScaled(canvas, toolIcons[i], button.X + 9, button.Y + 10, 20, 20);
                SmallTextRenderer.DrawClipped(canvas, toolLabels[i], button.X + 36, button.Y + 17,
                    Math.Max(18, button.Width - 43), Text);
            }
        }

        private void RenderFooter(Canvas canvas)
        {
            int footerY = Y + Height - FooterHeight;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), X + 1, footerY, Width - 2, FooterHeight - 1);
            canvas.DrawLine(Color.FromArgb(49, 61, 72), X + 1, footerY, X + Width - 2, footerY);

            IconManager.DrawScaled(canvas, IconType.Start, X + Padding, footerY + 21, 24, 24);
            SmallTextRenderer.Draw(canvas, "ZONDERQOS", X + Padding + 34, footerY + 23, Text);
            SmallTextRenderer.Draw(canvas, "GEN3", X + Padding + 34, footerY + 38, Muted);

            if (exitButton != null)
                RenderPowerButton(canvas, exitButton, IconType.Close, "GUI", false);
            if (rebootButton != null)
                RenderPowerButton(canvas, rebootButton, IconType.Reboot, "REBOOT", false);
            if (shutdownButton != null)
                RenderPowerButton(canvas, shutdownButton, IconType.Shutdown, "WYLACZ", true);
        }

        private static void RenderPowerButton(Canvas canvas, Button button, IconType icon, string label, bool danger)
        {
            Color normal = danger ? Color.FromArgb(57, 38, 43) : Color.FromArgb(31, 39, 47);
            Color hover = danger ? Color.FromArgb(86, 45, 52) : Color.FromArgb(42, 57, 70);
            Color border = danger ? Color.FromArgb(112, 61, 69) : Color.FromArgb(61, 80, 96);
            canvas.DrawFilledRectangle(button.IsHovered ? hover : normal, button.X, button.Y, button.Width, button.Height);
            canvas.DrawRectangle(button.IsHovered ? Accent : border, button.X, button.Y, button.Width, button.Height);
            IconManager.DrawScaled(canvas, icon, button.X + 8, button.Y + 11, 20, 20);
            SmallTextRenderer.DrawClipped(canvas, label, button.X + 34, button.Y + 18,
                Math.Max(16, button.Width - 40), Text);
        }

        /// <summary>
        /// Handles Start-menu search only while the menu is visible. String changes
        /// happen only on actual key presses, never during idle frames.
        /// </summary>
        public bool HandleKeyboard(KeyEvent key)
        {
            if (!Visible || key == null)
                return false;

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (searchText.Length > 0)
                    searchText = searchText.Substring(0, searchText.Length - 1);
                UpdateLayout();
                return true;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                for (int i = 0; i < pinnedButtons.Count; i++)
                {
                    if (pinnedButtons[i].Visible)
                    {
                        pinnedButtons[i].InvokeClick();
                        return true;
                    }
                }
                for (int i = 0; i < toolButtons.Count; i++)
                {
                    if (toolButtons[i].Visible)
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
                UpdateLayout();
                return true;
            }

            return false;
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible)
                return;

            UpdateLayout();
            UpdateButtonInteractions(pinnedButtons, mouseX, mouseY, isClicked, wasClicked);
            UpdateButtonInteractions(toolButtons, mouseX, mouseY, isClicked, wasClicked);
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
