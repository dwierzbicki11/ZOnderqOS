using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.Audio;
using ZonderqOS.Hardware;

namespace ZonderqOS.GUI.Apps
{
    public sealed class MusicPlayerApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 23, 29);
        private static readonly Color LeftPanelBg = Color.FromArgb(24, 30, 38);
        private static readonly Color HeaderBg = Color.FromArgb(28, 36, 46);
        private static readonly Color ItemHover = Color.FromArgb(36, 46, 58);
        private static readonly Color ItemActive = Color.FromArgb(38, 62, 86);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color ButtonNormal = Color.FromArgb(33, 42, 52);
        private static readonly Color ButtonHover = Color.FromArgb(48, 62, 78);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(78, 185, 126);
        private static readonly Color BarCyan = Color.FromArgb(73, 163, 224);
        private static readonly Color BarMint = Color.FromArgb(78, 185, 126);

        private readonly MelodyPlayer player = new MelodyPlayer();
        private readonly Action closeCallback;
        private int hoveredTrack = -1;
        private int hoveredBtn = -1; // 0: Prev, 1: Play/Pause, 2: Stop, 3: Next, 4: Mute

        public MusicPlayerApp(int x, int y, Action onClose) : base("Muzyka")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 560, 380, "Odtwarzacz Muzyki");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            player.Stop();
            Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override bool NeedsContinuousRedraw => player.IsPlaying;

        public override void Update()
        {
            player.Update();
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

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (player.IsPlaying) player.Pause();
                else player.Play();
                return;
            }

            if (key.Key == ConsoleKeyEx.S)
            {
                player.Stop();
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow || key.Key == ConsoleKeyEx.N)
            {
                player.Next();
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow || key.Key == ConsoleKeyEx.P)
            {
                player.Previous();
                return;
            }

            if (key.Key == ConsoleKeyEx.M)
            {
                Speaker.Enabled = !Speaker.Enabled;
                if (!Speaker.Enabled) Speaker.Stop();
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
            int contentHeight = Math.Max(1, Window.Height - 46);

            int listWidth = 190;

            // 1. Tracklist Click Check
            hoveredTrack = -1;
            int trackY = contentY + 36;
            for (int i = 0; i < Melodies.Playlist.Length; i++)
            {
                if (mouseX >= contentX && mouseX < contentX + listWidth &&
                    mouseY >= trackY && mouseY < trackY + 28)
                {
                    hoveredTrack = i;
                    if (leftClicked && !leftWasClicked)
                    {
                        player.Play(i);
                        return;
                    }
                }
                trackY += 30;
            }

            // 2. Playback Control Buttons Click Check
            int ctrlX = contentX + listWidth + 12;
            int ctrlY = contentY + contentHeight - 42;
            int bW = 54;
            int bH = 28;

            hoveredBtn = -1;
            if (mouseY >= ctrlY && mouseY < ctrlY + bH)
            {
                if (mouseX >= ctrlX && mouseX < ctrlX + bW) hoveredBtn = 0; // Prev
                else if (mouseX >= ctrlX + 60 && mouseX < ctrlX + 60 + bW) hoveredBtn = 1; // Play/Pause
                else if (mouseX >= ctrlX + 120 && mouseX < ctrlX + 120 + bW) hoveredBtn = 2; // Stop
                else if (mouseX >= ctrlX + 180 && mouseX < ctrlX + 180 + bW) hoveredBtn = 3; // Next
                else if (mouseX >= ctrlX + 248 && mouseX < ctrlX + 248 + 68) hoveredBtn = 4; // Mute
            }

            if (leftClicked && !leftWasClicked && hoveredBtn >= 0)
            {
                switch (hoveredBtn)
                {
                    case 0: player.Previous(); break;
                    case 1:
                        if (player.IsPlaying) player.Pause();
                        else player.Play();
                        break;
                    case 2: player.Stop(); break;
                    case 3: player.Next(); break;
                    case 4:
                        Speaker.Enabled = !Speaker.Enabled;
                        if (!Speaker.Enabled) Speaker.Stop();
                        break;
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

            int listWidth = 190;

            // Background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Left Panel (Tracklist)
            canvas.DrawFilledRectangle(LeftPanelBg, contentX, contentY, listWidth, contentHeight);
            canvas.DrawFilledRectangle(Border, contentX + listWidth - 1, contentY, 1, contentHeight);

            // Tracklist Header
            canvas.DrawFilledRectangle(HeaderBg, contentX, contentY, listWidth, 30);
            canvas.DrawFilledRectangle(Border, contentX, contentY + 29, listWidth, 1);
            SmallTextRenderer.Draw(canvas, "UTWORY CHIPTUNE", contentX + 10, contentY + 11, TextColor);

            int trackY = contentY + 34;
            for (int i = 0; i < Melodies.Playlist.Length; i++)
            {
                Track t = Melodies.Playlist[i];
                bool isCur = (player.CurrentTrackIndex == i);
                Color rowBg = isCur ? ItemActive : (hoveredTrack == i ? ItemHover : LeftPanelBg);

                canvas.DrawFilledRectangle(rowBg, contentX + 2, trackY, listWidth - 4, 28);
                if (isCur)
                {
                    canvas.DrawFilledRectangle(AccentColor, contentX + 2, trackY, 3, 28);
                    string marker = player.IsPlaying ? ">" : " ";
                    SmallTextRenderer.Draw(canvas, marker, contentX + 8, trackY + 9, AccentColor);
                }

                Color nameCol = isCur ? AccentColor : TextColor;
                SmallTextRenderer.DrawClipped(canvas, t.Title, contentX + 22, trackY + 9, listWidth - 30, nameCol);
                trackY += 30;
            }

            // Right Panel Area
            int rightX = contentX + listWidth + 12;
            int rightWidth = contentWidth - listWidth - 24;

            // Track Info Card
            int cardY = contentY + 10;
            int cardH = 54;
            canvas.DrawFilledRectangle(HeaderBg, rightX, cardY, rightWidth, cardH);
            canvas.DrawFilledRectangle(Border, rightX, cardY, rightWidth, cardH);

            Track cur = player.CurrentTrack;
            string curTitle = cur != null ? cur.Title : "Brak utworu";
            string curAuthor = cur != null ? ("Kompozytor: " + cur.Author) : "";
            SmallTextRenderer.Draw(canvas, curTitle, rightX + 14, cardY + 12, TextColor);
            SmallTextRenderer.Draw(canvas, curAuthor, rightX + 14, cardY + 28, MutedColor);

            int freq = player.CurrentFrequency;
            string freqStr = freq > 0 ? (freq + " Hz") : (player.IsPlaying ? "Pauza" : "Zatrzymany");
            Color freqColor = freq > 0 ? AccentColor : MutedColor;
            int fw = SmallTextRenderer.Width(freqStr);
            SmallTextRenderer.Draw(canvas, freqStr, rightX + rightWidth - fw - 14, cardY + 18, freqColor);

            // Animated Spectrum Visualizer (8 bars)
            int eqX = rightX + (rightWidth - 260) / 2;
            int eqY = contentY + 84;
            int maxBarH = 75;

            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 23), eqX - 10, eqY, 280, maxBarH + 20);
            canvas.DrawFilledRectangle(Border, eqX - 10, eqY, 280, maxBarH + 20);

            for (int b = 0; b < 8; b++)
            {
                int barX = eqX + b * 32;
                int val = player.SpectrumBars[b];
                int bh = Math.Max(3, (val * maxBarH) / 10);
                int by = eqY + maxBarH - bh + 10;

                Color c = (b < 4) ? BarCyan : BarMint;
                canvas.DrawFilledRectangle(c, barX, by, 22, bh);
                // Top highlight
                canvas.DrawFilledRectangle(Color.White, barX, by, 22, 2);
            }

            // Interactive Piano Keyboard (C4 to B5)
            int pianoX = rightX + (rightWidth - 280) / 2;
            int pianoY = eqY + maxBarH + 32;
            DrawPianoKeys(canvas, pianoX, pianoY, player.CurrentFrequency);

            // Bottom Playback Controls Bar
            int ctrlY = contentY + contentHeight - 42;
            DrawCtrlButton(canvas, rightX, ctrlY, 54, 28, "|<", hoveredBtn == 0);

            string playText = player.IsPlaying ? "||" : ">";
            DrawCtrlButton(canvas, rightX + 60, ctrlY, 54, 28, playText, hoveredBtn == 1);

            DrawCtrlButton(canvas, rightX + 120, ctrlY, 54, 28, "[ ]", hoveredBtn == 2);
            DrawCtrlButton(canvas, rightX + 180, ctrlY, 54, 28, ">|", hoveredBtn == 3);

            string muteText = Speaker.Enabled ? "DZWIEK" : "WYCISZ";
            Color muteColor = Speaker.Enabled ? AccentColor : Color.FromArgb(240, 70, 70);
            DrawCtrlButton(canvas, rightX + 248, ctrlY, 68, 28, muteText, hoveredBtn == 4, muteColor);
        }

        private static void DrawPianoKeys(Canvas canvas, int x, int y, int activeFreq)
        {
            // 14 White keys
            int kw = 20;
            int kh = 48;
            int[] whiteFreqs = new int[]
            {
                262, 294, 330, 349, 392, 440, 494, // C4..B4
                523, 587, 659, 698, 784, 880, 988  // C5..B5
            };

            for (int i = 0; i < 14; i++)
            {
                int kx = x + i * kw;
                bool isKeyActive = (activeFreq >= whiteFreqs[i] - 10 && activeFreq <= whiteFreqs[i] + 10);
                Color keyColor = isKeyActive ? AccentColor : Color.FromArgb(240, 244, 248);

                canvas.DrawFilledRectangle(keyColor, kx, y, kw - 1, kh);
                canvas.DrawFilledRectangle(Border, kx, y, kw - 1, 1);
                canvas.DrawFilledRectangle(Border, kx, y + kh - 1, kw - 1, 1);
                canvas.DrawFilledRectangle(Border, kx, y, 1, kh);
                canvas.DrawFilledRectangle(Border, kx + kw - 2, y, 1, kh);
            }

            // Black keys: positions in octaves
            int[] blackOffsets = new int[] { 13, 33, 73, 93, 113, 153, 173, 213, 233, 253 };
            int[] blackFreqs = new int[] { 277, 311, 370, 415, 466, 554, 622, 740, 831, 932 };

            for (int b = 0; b < blackOffsets.Length; b++)
            {
                int bx = x + blackOffsets[b];
                bool isKeyActive = (activeFreq >= blackFreqs[b] - 10 && activeFreq <= blackFreqs[b] + 10);
                Color bCol = isKeyActive ? Color.FromArgb(73, 163, 224) : Color.FromArgb(20, 25, 30);

                canvas.DrawFilledRectangle(bCol, bx, y, 12, 28);
                canvas.DrawFilledRectangle(Border, bx, y, 12, 1);
                canvas.DrawFilledRectangle(Border, bx, y + 27, 12, 1);
                canvas.DrawFilledRectangle(Border, bx, y, 1, 28);
                canvas.DrawFilledRectangle(Border, bx + 11, y, 1, 28);
            }
        }

        private static void DrawCtrlButton(Canvas canvas, int x, int y, int w, int h, string text, bool hovered, Color? txtColor = null)
        {
            Color bg = hovered ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawFilledRectangle(Border, x, y, w, 1);
            canvas.DrawFilledRectangle(Border, x, y + h - 1, w, 1);
            canvas.DrawFilledRectangle(Border, x, y, 1, h);
            canvas.DrawFilledRectangle(Border, x + w - 1, y, 1, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + 9, w, txtColor ?? TextColor);
        }
    }
}
