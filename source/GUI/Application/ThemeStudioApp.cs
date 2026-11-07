using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class ThemeStudioApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color Panel = Color.FromArgb(27, 34, 42);
        private static readonly Color PanelHover = Color.FromArgb(36, 45, 56);
        private static readonly Color PanelSelected = Color.FromArgb(44, 55, 68);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color GreenColor = Color.FromArgb(60, 200, 100);

        private static readonly string[] ThemeNames =
        {
            "Zorin & Win Blue",
            "Steel Graphite",
            "Emerald Forest",
            "Cyberpunk Neon",
            "Nordic Frost",
            "Retro 95 Navy",
            "OLED Gold",
            "Crimson Flame"
        };

        private static readonly Color[] ThemeAccentColors =
        {
            Color.FromArgb(27, 117, 208),
            Color.FromArgb(130, 151, 170),
            Color.FromArgb(62, 168, 118),
            Color.FromArgb(245, 55, 150),
            Color.FromArgb(90, 205, 240),
            Color.FromArgb(24, 75, 160),
            Color.FromArgb(240, 195, 45),
            Color.FromArgb(235, 65, 65)
        };

        private static readonly string[] BackgroundNames =
        {
            "Tapeta PNG",
            "Grafit",
            "Cosmos Navy",
            "OLED Czerń",
            "Retro 95 Teal",
            "Cyber Matrix",
            "Arktyczny Lód"
        };

        private static readonly Color[] BackgroundPreviewColors =
        {
            Color.FromArgb(35, 50, 75),   // Tapeta PNG
            Color.FromArgb(25, 30, 36),   // Grafit
            Color.FromArgb(17, 31, 47),   // Cosmos Navy
            Color.FromArgb(8, 10, 14),    // OLED Czerń
            Color.FromArgb(0, 128, 128),  // Retro 95 Teal
            Color.FromArgb(10, 26, 18),   // Cyber Matrix
            Color.FromArgb(22, 36, 48)    // Arktyczny Lód
        };

        private int hoveredTheme = -1;
        private int hoveredBackground = -1;
        private long toneEndTicks;
        private readonly Action closeCallback;

        public ThemeStudioApp(int x, int y, Action onClose) : base("Studio Motywow")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 520, 430, "Studio Motywow");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Update()
        {
            if (toneEndTicks > 0 && Stopwatch.GetTimestamp() >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }
        }

        private void PlayTone(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long clock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (clock * durationMs) / 1000;
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

            if (key.Key == ConsoleKeyEx.Tab || key.Key == ConsoleKeyEx.RightArrow)
            {
                global::ZonderqOS.SystemSettings.CycleAccentTheme();
                PlayTone(650, 30);
            }
            else if (key.Key == ConsoleKeyEx.B)
            {
                global::ZonderqOS.SystemSettings.CycleDesktopBackground();
                PlayTone(520, 30);
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);

            hoveredTheme = -1;
            hoveredBackground = -1;

            // 1. Accent theme tiles (8 tiles: 2 rows of 4)
            int themeGridY = contentY + 124;
            int tileW = (contentWidth - 28 - 3 * 8) / 4;
            int tileH = 34;

            for (int i = 0; i < 8; i++)
            {
                int col = i % 4;
                int row = i / 4;
                int tx = contentX + 14 + col * (tileW + 8);
                int ty = themeGridY + row * (tileH + 6);

                if (mouseX >= tx && mouseX < tx + tileW && mouseY >= ty && mouseY < ty + tileH)
                {
                    hoveredTheme = i;
                    if (leftClicked && !leftWasClicked)
                    {
                        global::ZonderqOS.SystemSettings.SetAccentTheme(i);
                        PlayTone((ushort)(600 + i * 40), 30);
                        return;
                    }
                }
            }

            // 2. Desktop Background tiles (7 tiles: 1 row)
            int bgGridY = contentY + 236;
            int bgTileW = (contentWidth - 28 - 6 * 6) / 7;
            int bgTileH = 50;

            for (int i = 0; i < 7; i++)
            {
                int bx = contentX + 14 + i * (bgTileW + 6);
                int by = bgGridY;

                if (mouseX >= bx && mouseX < bx + bgTileW && mouseY >= by && mouseY < by + bgTileH)
                {
                    hoveredBackground = i;
                    if (leftClicked && !leftWasClicked)
                    {
                        global::ZonderqOS.SystemSettings.SetDesktopBackground(i);
                        PlayTone(550, 30);
                        return;
                    }
                }
            }

            // 3. Reset Button
            int btnW = 150;
            int btnH = 24;
            int btnX = contentX + 14;
            int btnY = contentY + 316;

            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= btnX && mouseX < btnX + btnW && mouseY >= btnY && mouseY < btnY + btnH)
                {
                    global::ZonderqOS.SystemSettings.SetAccentTheme(0);
                    global::ZonderqOS.SystemSettings.SetDesktopBackground(0);
                    PlayTone(800, 40);
                }
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header labels
            SmallTextRenderer.Draw(canvas, "STUDIO MOTYWOW I PERSONALIZACJI", contentX + 14, contentY + 12, SystemTheme.Accent);
            SmallTextRenderer.Draw(canvas, "Kliknij styl akcentu lub tlo pulpitu - zmiana nastepuje natychmiast.",
                contentX + 14, contentY + 26, MutedColor);

            // Live Preview Card (Top Right)
            int prevW = 200;
            int prevH = 68;
            int prevX = contentX + contentWidth - prevW - 14;
            int prevY = contentY + 8;

            // Mini window preview
            canvas.DrawFilledRectangle(Panel, prevX, prevY, prevW, prevH);
            canvas.DrawRectangle(SystemTheme.AccentBorder, prevX, prevY, prevW, prevH);

            // Mini titlebar
            canvas.DrawFilledRectangle(SystemTheme.Accent, prevX + 1, prevY + 1, prevW - 2, 14);
            SmallTextRenderer.Draw(canvas, "PODGLAD MOTYWU", prevX + 6, prevY + 4, Color.White);

            // Mini buttons
            canvas.DrawFilledRectangle(Color.FromArgb(200, 220, 240), prevX + prevW - 26, prevY + 4, 6, 6);
            canvas.DrawFilledRectangle(Color.FromArgb(200, 220, 240), prevX + prevW - 17, prevY + 4, 6, 6);
            canvas.DrawFilledRectangle(Color.FromArgb(240, 80, 80), prevX + prevW - 8, prevY + 4, 6, 6);

            // Mini content
            SmallTextRenderer.Draw(canvas, "Akcent: " + global::ZonderqOS.SystemSettings.AccentThemeName,
                prevX + 8, prevY + 22, TextColor);
            SmallTextRenderer.Draw(canvas, "Pulpit: " + global::ZonderqOS.SystemSettings.DesktopBackgroundName,
                prevX + 8, prevY + 36, MutedColor);

            // Mini taskbar indicator
            canvas.DrawFilledRectangle(SystemTheme.AccentSoft, prevX + 1, prevY + prevH - 12, prevW - 2, 11);
            canvas.DrawFilledRectangle(SystemTheme.Accent, prevX + 6, prevY + prevH - 3, 24, 2);

            // Section 1: Motywy Akcentu
            SmallTextRenderer.Draw(canvas, "MOTYW KOLORYSTYCZNY (AKCENT OKIEN I PASKA ZADAN):", contentX + 14, contentY + 104, TextColor);

            int themeGridY = contentY + 120;
            int tileW = (contentWidth - 28 - 3 * 8) / 4;
            int tileH = 36;
            int activeAccent = global::ZonderqOS.SystemSettings.AccentTheme;

            for (int i = 0; i < 8; i++)
            {
                int col = i % 4;
                int row = i / 4;
                int tx = contentX + 14 + col * (tileW + 8);
                int ty = themeGridY + row * (tileH + 8);

                bool isSelected = (i == activeAccent);
                bool isHovered = (i == hoveredTheme);

                Color bg = isSelected ? PanelSelected : (isHovered ? PanelHover : Panel);
                Color border = isSelected ? SystemTheme.AccentBorder : (isHovered ? SystemTheme.Accent : Border);

                canvas.DrawFilledRectangle(bg, tx, ty, tileW, tileH);
                canvas.DrawRectangle(border, tx, ty, tileW, tileH);

                // Color swatch
                canvas.DrawFilledRectangle(ThemeAccentColors[i], tx + 5, ty + 5, 14, tileH - 10);

                // Theme Name
                SmallTextRenderer.Draw(canvas, ThemeNames[i], tx + 24, ty + 8, isSelected ? SystemTheme.Accent : TextColor);

                if (isSelected)
                {
                    canvas.DrawFilledRectangle(SystemTheme.Accent, tx + tileW - 6, ty + 6, 3, tileH - 12);
                }
            }

            // Section 2: Tła Pulpitu
            SmallTextRenderer.Draw(canvas, "STYL TLA PULPITU:", contentX + 14, contentY + 218, TextColor);

            int bgGridY = contentY + 234;
            int bgTileW = (contentWidth - 28 - 6 * 6) / 7;
            int bgTileH = 54;
            int activeBg = global::ZonderqOS.SystemSettings.DesktopBackgroundMode;

            for (int i = 0; i < 7; i++)
            {
                int bx = contentX + 14 + i * (bgTileW + 6);
                int by = bgGridY;

                bool isSelected = (i == activeBg);
                bool isHovered = (i == hoveredBackground);

                Color border = isSelected ? SystemTheme.AccentBorder : (isHovered ? SystemTheme.Accent : Border);

                canvas.DrawFilledRectangle(Panel, bx, by, bgTileW, bgTileH);
                canvas.DrawRectangle(border, bx, by, bgTileW, bgTileH);

                // Mini preview block
                canvas.DrawFilledRectangle(BackgroundPreviewColors[i], bx + 3, by + 3, bgTileW - 6, 26);

                // Label
                SmallTextRenderer.DrawCentered(canvas, BackgroundNames[i], bx, by + 35, bgTileW, isSelected ? SystemTheme.Accent : MutedColor);

                if (isSelected)
                {
                    canvas.DrawFilledRectangle(SystemTheme.Accent, bx + 4, by + bgTileH - 3, bgTileW - 8, 2);
                }
            }

            // Reset Button
            int btnW = 160;
            int btnH = 26;
            int btnX = contentX + 14;
            int btnY = contentY + 312;

            canvas.DrawFilledRectangle(Color.FromArgb(32, 40, 50), btnX, btnY, btnW, btnH);
            canvas.DrawRectangle(Border, btnX, btnY, btnW, btnH);
            SmallTextRenderer.DrawCentered(canvas, "PRZYWROC DOMYSLNE", btnX, btnY + 8, btnW, TextColor);

            // Instructions Footer
            SmallTextRenderer.Draw(canvas, "Wskazowka: Mozesz takze uzyc skrotu klawiszowego Tab (akcent) oraz B (tlo pulpitu).",
                contentX + 14, contentY + 355, MutedColor);
        }
    }
}
