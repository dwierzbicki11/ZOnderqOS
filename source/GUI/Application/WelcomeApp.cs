using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class WelcomeApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 24, 32);
        private static readonly Color Panel = Color.FromArgb(26, 36, 48);
        private static readonly Color PanelHover = Color.FromArgb(36, 50, 68);
        private static readonly Color Border = Color.FromArgb(48, 68, 92);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color MutedColor = Color.FromArgb(130, 155, 180);
        private static readonly Color AccentColor = Color.FromArgb(40, 150, 240);
        private static readonly Color GreenColor = Color.FromArgb(50, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);

        private const int HeaderHeight = 38;
        private int activeTab = 0; // 0 = Przegląd, 1 = Nowości Alfa, 2 = Skróty, 3 = Język/Lang, 4 = O Systemie
        private readonly Action closeCallback;
        private readonly Action<string> launchAppCallback;

        public WelcomeApp(int x, int y, Action onClose, Action<string> onLaunchApp = null)
            : base("Welcome Center")
        {
            closeCallback = onClose;
            launchAppCallback = onLaunchApp;
            Window = new Window(x, y, 620, 420, "Welcome to ZOnderqOS 1.0 Alpha");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            base.HandleKeyboard(key);
            if (key == null) return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Tab || key.Key == ConsoleKeyEx.RightArrow)
            {
                activeTab = (activeTab + 1) % 5;
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                activeTab = (activeTab + 4) % 5;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            base.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
            if (Window == null || !Window.Visible || Window.IsMinimized) return;

            int relX = mouseX - Window.X;
            int relY = mouseY - Window.Y;

            if (!isClicked || wasClicked) return;

            // Tab bar clicks (Y: 34 to 62)
            if (relY >= 34 && relY <= 62)
            {
                int tabWidth = (Window.Width - 32) / 5;
                for (int t = 0; t < 5; t++)
                {
                    if (relX >= 12 + t * tabWidth && relX < 12 + (t + 1) * tabWidth)
                    {
                        activeTab = t;
                        return;
                    }
                }
                return;
            }

            // Quick language buttons in Hero Banner (X: 470..525 and 535..590, Y: 75..105)
            if (activeTab == 0 && relY >= 75 && relY <= 105)
            {
                if (relX >= 470 && relX <= 525)
                {
                    // Polish UI is disabled until Beta 1; keep English selected.
                    return;
                }
                else if (relX >= 535 && relX <= 590)
                {
                    global::ZonderqOS.Localization.LocalizationManager.SetLanguage(global::ZonderqOS.Localization.Language.English);
                    return;
                }
            }

            // Tab 3 Language Selection Cards (Y: 130..220)
            if (activeTab == 3 && relY >= 130 && relY <= 220)
            {
                if (relX >= 30 && relX <= 280)
                {
                    global::ZonderqOS.Localization.LocalizationManager.SetLanguage(global::ZonderqOS.Localization.Language.Polish);
                    return;
                }
                else if (relX >= 300 && relX <= 550)
                {
                    global::ZonderqOS.Localization.LocalizationManager.SetLanguage(global::ZonderqOS.Localization.Language.English);
                    return;
                }
            }

            // Tab 0 Quick Action cards
            if (activeTab == 0 && relY >= 180 && relY <= 340)
            {
                int col1Left = 24, col1Right = 290;
                int col2Left = 310, col2Right = 580;

                // Row 1: Statki & Chiptune
                if (relY >= 180 && relY <= 226)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.Battleship); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Chiptune); Close(); }
                }
                // Row 2: Aktualizacje & Settings
                else if (relY >= 234 && relY <= 280)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.SystemUpdater); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Settings); Close(); }
                }
                // Row 3: File Manager & Terminal
                else if (relY >= 288 && relY <= 334)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.FileManager); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Terminal); Close(); }
                }
            }

            // Close button in bottom-right (Y: 365 to 395, X: 470 to 590)
            if (relY >= 365 && relY <= 395 && relX >= 470 && relX <= 590)
            {
                Close();
            }
        }

        public override void Render(Canvas canvas)
        {
            if (Window == null || !Window.Visible || Window.IsMinimized) return;

            base.Render(canvas);

            int x = Window.X + 8;
            int y = Window.Y + 28;
            int w = Window.Width - 16;
            int h = Window.Height - 36;

            canvas.DrawFilledRectangle(Surface, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            RenderTabBar(canvas, x + 8, y + 8, w - 16, 26);

            int contentY = y + 42;
            int contentH = h - 86;

            switch (activeTab)
            {
                case 0:
                    RenderOverviewTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 1:
                    RenderNewFeaturesTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 2:
                    RenderHotkeysTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 3:
                    RenderLanguageTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 4:
                    RenderAboutTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
            }

            RenderFooter(canvas, x + 8, y + h - 38, w - 16, 32);
        }

        private static readonly string[] TabLabels = { "1. OVERVIEW", "2. FEATURES", "3. SHORTCUTS", "4. LANGUAGE", "5. ABOUT" };

        private void RenderTabBar(Canvas canvas, int x, int y, int w, int h)
        {
            int tabW = w / TabLabels.Length;

            for (int i = 0; i < TabLabels.Length; i++)
            {
                int tx = x + i * tabW;
                bool isSel = (i == activeTab);

                Color bg = isSel ? Panel : Surface;
                Color border = isSel ? AccentColor : Border;
                Color textCol = isSel ? TextColor : MutedColor;

                UiPainter.RoundedPanel(canvas, tx, y, tabW - 4, h, 4, bg, border);
                SmallTextRenderer.DrawCentered(canvas, TabLabels[i], tx, y + 6, tabW - 4, textCol);
            }
        }

        private void RenderOverviewTab(Canvas canvas, int x, int y, int w, int h)
        {
            // Hero Banner
            UiPainter.RoundedPanel(canvas, x, y, w, 70, 6, Panel, Border);
            IconManager.DrawScaled(canvas, IconType.Start, x + 14, y + 14, 42, 42);

            SmallTextRenderer.Draw(canvas, "ZONDERQOS 1.0 ALPHA - TEST RELEASE", x + 66, y + 14, AccentColor);
            SmallTextRenderer.Draw(canvas, "Independent x86_64 operating system based on Cosmos Native Core.", x + 66, y + 30, TextColor);
            SmallTextRenderer.Draw(canvas, "29 bundled applications, persistent FAT32 storage and OTA updates.", x + 66, y + 46, MutedColor);

            // Quick language selector pills in top-right of hero banner
            bool isPl = global::ZonderqOS.Localization.LocalizationManager.CurrentLanguage == global::ZonderqOS.Localization.Language.Polish;
            UiPainter.RoundedPanel(canvas, x + w - 105, y + 10, 48, 22, 3, isPl ? AccentColor : Surface, Border);
            SmallTextRenderer.DrawCentered(canvas, "BETA", x + w - 105, y + 14, 48, MutedColor);

            UiPainter.RoundedPanel(canvas, x + w - 52, y + 10, 48, 22, 3, !isPl ? AccentColor : Surface, Border);
            SmallTextRenderer.DrawCentered(canvas, "EN", x + w - 52, y + 14, 48, !isPl ? Color.White : TextColor);

            // Subtitle
            SmallTextRenderer.Draw(canvas, "QUICK START - CHOOSE AN APPLICATION:", x + 4, y + 82, GoldColor);

            // Action cards (2 columns x 3 rows)
            int cardW = (w - 16) / 2;
            int cardH = 46;

            int col1 = x + 4;
            int col2 = x + 12 + cardW;

            // Row 1
            DrawActionCard(canvas, col1, y + 104, cardW, cardH, "⚓ Battleship", "Tactical strategy game with AI and sound", GreenColor);
            DrawActionCard(canvas, col2, y + 104, cardW, cardH, "🎵 Chiptune Studio", "8-bit music synthesizer and retro keyboard", AccentColor);

            // Row 2
            DrawActionCard(canvas, col1, y + 158, cardW, cardH, "🔄 Update Center", "Download OTA updates or install from USB", GoldColor);
            DrawActionCard(canvas, col2, y + 158, cardW, cardH, "🎨 Settings and Themes", "Customize desktop, colors and network options", TextColor);

            // Row 3
            DrawActionCard(canvas, col1, y + 212, cardW, cardH, "📁 File Manager", "Browse files on the ZONDERQDATA partition", TextColor);
            DrawActionCard(canvas, col2, y + 212, cardW, cardH, "💻 Terminal Console", "CLI shell, utilities and diagnostics", AccentColor);
        }

        private void DrawActionCard(Canvas canvas, int x, int y, int w, int h, string title, string sub, Color accent)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
            canvas.DrawFilledRectangle(accent, x + 2, y + 4, 3, h - 8);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 8, TextColor);
            SmallTextRenderer.Draw(canvas, sub, x + 12, y + 26, MutedColor);
        }

        private void RenderNewFeaturesTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "HIGHLIGHTS OF ALPHA 1.0:", x + 16, y + 14, GoldColor);

            int itemY = y + 38;
            int stride = 46;

            DrawFeatureItem(canvas, x + 16, itemY, "1. Battleship Game",
                "Two 10x10 grids, a 10-ship fleet, tactical AI (Hunt Mode) and PC speaker effects.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "2. Chiptune Studio & PC Speaker Audio Engine",
                "Retro keyboard synthesizer, real-time waveform display and built-in melodies.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "3. System Update Engine 2.0",
                "OTA/USB update sources, SHA-256 verification and rollback support.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "4. QuickNotes",
                "Desktop sticky notes with automatic saving to the user profile.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "5. Security and Persistent FAT32 ZONDERQDATA",
                "IntegrityGuard protection, PIN-based PassVault and safe storage management.");
        }

        private void DrawFeatureItem(Canvas canvas, int x, int y, string title, string desc)
        {
            SmallTextRenderer.Draw(canvas, title, x, y, AccentColor);
            SmallTextRenderer.Draw(canvas, desc, x + 8, y + 18, TextColor);
        }

        private void RenderHotkeysTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "KEYBOARD SHORTCUTS AND NAVIGATION:", x + 16, y + 14, GoldColor);

            int itemY = y + 42;
            int stride = 34;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Super / Win]", "Opens or closes the Start menu.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Alt + F4] / [Esc]", "Closes the active application window or dialog.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Ctrl + Shift + Esc]", "Opens Task Manager immediately.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Tab] / [Arrow Keys]", "Moves between tabs and dialog controls.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[zupdate] in CLI", "Manage updates from the command line ('zupdate check').");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Right Mouse Button]", "Desktop context menu (refresh, shortcuts, settings).");
        }

        private void DrawHotkeyRow(Canvas canvas, int x, int y, string key, string desc)
        {
            UiPainter.RoundedPanel(canvas, x, y - 2, 160, 24, 3, Surface, Border);
            SmallTextRenderer.DrawCentered(canvas, key, x, y + 3, 160, AccentColor);
            SmallTextRenderer.Draw(canvas, desc, x + 175, y + 3, TextColor);
        }

        private void RenderLanguageTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "SYSTEM LANGUAGE:", x + 16, y + 14, GoldColor);
            SmallTextRenderer.Draw(canvas, "English is the only interface language available in Alpha 1.", x + 16, y + 32, TextColor);

            bool isPl = global::ZonderqOS.Localization.LocalizationManager.CurrentLanguage == global::ZonderqOS.Localization.Language.Polish;

            // Card 1: Polski
            int cardW = (w - 32) / 2;
            int cardH = 90;
            int cardY = y + 60;

            Color plBorder = isPl ? AccentColor : Border;
            Color plBg = isPl ? Color.FromArgb(32, 48, 64) : Surface;
            UiPainter.RoundedPanel(canvas, x + 12, cardY, cardW, cardH, 6, plBg, plBorder);
            canvas.DrawFilledRectangle(isPl ? GreenColor : MutedColor, x + 14, cardY + 6, 4, cardH - 12);
            SmallTextRenderer.Draw(canvas, "POLISH (pl-PL) - BETA 1", x + 26, cardY + 12, isPl ? GoldColor : TextColor);
            SmallTextRenderer.Draw(canvas, "Polish language support", x + 26, cardY + 32, TextColor);
            SmallTextRenderer.Draw(canvas, "is planned for Beta 1.", x + 26, cardY + 48, MutedColor);
            if (isPl)
                SmallTextRenderer.Draw(canvas, "[V] ACTIVE SYSTEM LANGUAGE", x + 26, cardY + 68, GreenColor);
            else
                SmallTextRenderer.Draw(canvas, "[Available in Beta 1]", x + 26, cardY + 68, AccentColor);

            // Card 2: English
            int card2X = x + 20 + cardW;
            Color enBorder = !isPl ? AccentColor : Border;
            Color enBg = !isPl ? Color.FromArgb(32, 48, 64) : Surface;
            UiPainter.RoundedPanel(canvas, card2X, cardY, cardW, cardH, 6, enBg, enBorder);
            canvas.DrawFilledRectangle(!isPl ? GreenColor : MutedColor, card2X + 2, cardY + 6, 4, cardH - 12);
            SmallTextRenderer.Draw(canvas, "ENGLISH (en-US)", card2X + 14, cardY + 12, !isPl ? GoldColor : TextColor);
            SmallTextRenderer.Draw(canvas, "International desktop interface,", card2X + 14, cardY + 32, TextColor);
            SmallTextRenderer.Draw(canvas, "system toolbars, notifications and help.", card2X + 14, cardY + 48, MutedColor);
            if (!isPl)
                SmallTextRenderer.Draw(canvas, "[V] ACTIVE SYSTEM LANGUAGE", card2X + 14, cardY + 68, GreenColor);
            else
                SmallTextRenderer.Draw(canvas, "[Click to select English]", card2X + 14, cardY + 68, AccentColor);

            // Information note at bottom
            SmallTextRenderer.Draw(canvas, "INFO: Alpha 1 uses English (en-US) only.", x + 16, y + cardH + 75, MutedColor);
            SmallTextRenderer.Draw(canvas, "More interface languages are planned for Beta 1.", x + 16, y + cardH + 92, MutedColor);
        }

        private void RenderAboutTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "ZONDERQOS 1.0 ALPHA TECHNICAL SPECIFICATIONS:", x + 16, y + 14, GoldColor);

            int itemY = y + 42;
            int stride = 28;

            DrawSpecRow(canvas, x + 16, itemY, "Kernel:", "Cosmos Kernel 3.0.97 x64 Native Core (single vCPU)");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Architecture:", "Native AOT x86_64, IL2CPU compiler, no interpreter");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Desktop Environment:", "ZOnderqOS Gen 3 Desktop, VESA/GOP 1366x768 / 32bpp");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "File System:", "Dedicated FAT32 ZONDERQDATA partition (root '/'), fail-closed");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Graphics Performance:", "Rendering loop with reusable memory buffers");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Software Package:", "29 bundled applications, 10 retro games, package manager");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "License and Author:", "ZOnderqOS Project (2026) | Author: ZOnderq");
        }

        private void DrawSpecRow(Canvas canvas, int x, int y, string label, string value)
        {
            SmallTextRenderer.Draw(canvas, label, x, y, AccentColor);
            SmallTextRenderer.Draw(canvas, value, x + 170, y, TextColor);
        }

        private void RenderFooter(Canvas canvas, int x, int y, int w, int h)
        {
            SmallTextRenderer.Draw(canvas, "ZOnderqOS 1.0 Alpha - Thank you for testing this release!", x + 4, y + 10, MutedColor);

            int btnW = 120;
            int btnH = 26;
            int btnX = x + w - btnW;
            int btnY = y + 3;

            UiPainter.RoundedPanel(canvas, btnX, btnY, btnW, btnH, 4, AccentColor, AccentColor);
            SmallTextRenderer.DrawCentered(canvas, "GET STARTED", btnX, btnY + 6, btnW, Color.White);
        }
    }
}
