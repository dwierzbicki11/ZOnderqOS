using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Hardware;

namespace ZonderqOS.GUI.Apps
{
    public sealed class ChiptuneApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color Panel = Color.FromArgb(28, 35, 45);
        private static readonly Color PanelHover = Color.FromArgb(38, 48, 62);
        private static readonly Color Border = Color.FromArgb(52, 66, 84);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color MutedColor = Color.FromArgb(135, 150, 168);
        private static readonly Color AccentColor = Color.FromArgb(45, 145, 235);
        private static readonly Color GreenColor = Color.FromArgb(55, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color CyanColor = Color.FromArgb(50, 210, 230);

        private const int HeaderHeight = 38;
        private readonly Action closeCallback;

        // Sound settings
        private int currentOctave = 4; // 3, 4, 5
        private int noteDurationMs = 120;
        private int lastPlayedFreq = 440;
        private string lastPlayedNote = "A4 (440 Hz)";
        private int activeKeyVisual = -1;
        private int visualWavePhase = 0;

        // Demo song player state
        private bool isPlayingSong = false;
        private int currentSongIndex = 0;
        private int songStepIndex = 0;
        private int songTimerTicks = 0;

        // Demo melodies: (Frequency, DurationMs)
        private static readonly int[][] MarioFrequencies = new int[][]
        {
            new int[] { 659, 100 }, new int[] { 659, 100 }, new int[] { 0, 80 }, new int[] { 659, 100 },
            new int[] { 0, 80 }, new int[] { 523, 100 }, new int[] { 659, 120 }, new int[] { 784, 180 },
            new int[] { 0, 150 }, new int[] { 392, 180 }
        };

        private static readonly int[][] TetrisFrequencies = new int[][]
        {
            new int[] { 659, 150 }, new int[] { 494, 90 }, new int[] { 523, 90 }, new int[] { 587, 150 },
            new int[] { 523, 90 }, new int[] { 494, 90 }, new int[] { 440, 150 }, new int[] { 440, 90 },
            new int[] { 523, 90 }, new int[] { 659, 150 }, new int[] { 587, 90 }, new int[] { 523, 90 },
            new int[] { 494, 180 }
        };

        private static readonly int[][] ImperialFrequencies = new int[][]
        {
            new int[] { 440, 180 }, new int[] { 440, 180 }, new int[] { 440, 180 }, new int[] { 349, 140 },
            new int[] { 523, 80 }, new int[] { 440, 180 }, new int[] { 349, 140 }, new int[] { 523, 80 },
            new int[] { 440, 260 }
        };

        private static readonly int[][] FanfareFrequencies = new int[][]
        {
            new int[] { 523, 100 }, new int[] { 659, 100 }, new int[] { 784, 100 }, new int[] { 1046, 260 }
        };

        // Note frequencies for Octave 4
        private static readonly int[] BaseFrequencies = new int[]
        {
            261, // 0: C4
            277, // 1: C#4
            293, // 2: D4
            311, // 3: D#4
            329, // 4: E4
            349, // 5: F4
            369, // 6: F#4
            392, // 7: G4
            415, // 8: G#4
            440, // 9: A4
            466, // 10: A#4
            493, // 11: B4
            523  // 12: C5
        };

        private static readonly string[] NoteNames = new string[]
        {
            "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B", "C+"
        };

        public ChiptuneApp(int x, int y, Action onClose) : base("Chiptune Studio")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 580, 410, "Chiptune Studio - Syntezator Retro 8-bit");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            StopAudio();
            base.Close();
            closeCallback?.Invoke();
        }

        private void StopAudio()
        {
            isPlayingSong = false;
            Speaker.Stop();
        }

        public void PlayTone(int freq, int durationMs, string noteLabel)
        {
            lastPlayedFreq = freq;
            lastPlayedNote = noteLabel + " (" + freq + " Hz)";
            visualWavePhase = (visualWavePhase + 1) % 8;

            if (freq > 20)
            {
                // In Cosmos Speaker.Beep/Tone
                int frames = Math.Max(2, durationMs / 16);
                Speaker.Beep(freq, frames);
            }
            else
            {
                Speaker.Stop();
            }
        }

        public void PlayNoteIndex(int noteIndex)
        {
            if (noteIndex < 0 || noteIndex >= BaseFrequencies.Length) return;

            activeKeyVisual = noteIndex;
            int baseFreq = BaseFrequencies[noteIndex];
            int freq = baseFreq;

            if (currentOctave == 3) freq = baseFreq / 2;
            else if (currentOctave == 5) freq = baseFreq * 2;

            string label = NoteNames[noteIndex] + currentOctave;
            PlayTone(freq, noteDurationMs, label);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            base.HandleKeyboard(key);
            if (key == null) return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                StopAudio();
                Close();
                return;
            }

            // Keyboard piano mapping (A-S-D-F-G-H-J-K)
            switch (key.Key)
            {
                case ConsoleKeyEx.A: PlayNoteIndex(0); break;  // C
                case ConsoleKeyEx.W: PlayNoteIndex(1); break;  // C#
                case ConsoleKeyEx.S: PlayNoteIndex(2); break;  // D
                case ConsoleKeyEx.E: PlayNoteIndex(3); break;  // D#
                case ConsoleKeyEx.D: PlayNoteIndex(4); break;  // E
                case ConsoleKeyEx.F: PlayNoteIndex(5); break;  // F
                case ConsoleKeyEx.T: PlayNoteIndex(6); break;  // F#
                case ConsoleKeyEx.G: PlayNoteIndex(7); break;  // G
                case ConsoleKeyEx.Y: PlayNoteIndex(8); break;  // G#
                case ConsoleKeyEx.H: PlayNoteIndex(9); break;  // A
                case ConsoleKeyEx.U: PlayNoteIndex(10); break; // A#
                case ConsoleKeyEx.J: PlayNoteIndex(11); break; // B
                case ConsoleKeyEx.K: PlayNoteIndex(12); break; // C+
                case ConsoleKeyEx.Spacebar:
                    StopAudio();
                    break;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            if (leftClicked && !leftWasClicked)
            {
                int wx = Window.X;
                int wy = Window.Y + HeaderHeight;

                // 1. Octave selector: wx + 16, wy + 16 (Oct 3, Oct 4, Oct 5)
                int octY = wy + 16;
                if (Hit(mouseX, mouseY, wx + 16, octY, 65, 24)) { currentOctave = 3; return; }
                if (Hit(mouseX, mouseY, wx + 86, octY, 65, 24)) { currentOctave = 4; return; }
                if (Hit(mouseX, mouseY, wx + 156, octY, 65, 24)) { currentOctave = 5; return; }

                // 2. Demo Song Buttons: wx + 240, wy + 16
                if (Hit(mouseX, mouseY, wx + 240, octY, 70, 24)) { StartSong(0); return; } // Mario
                if (Hit(mouseX, mouseY, wx + 316, octY, 70, 24)) { StartSong(1); return; } // Tetris
                if (Hit(mouseX, mouseY, wx + 392, octY, 70, 24)) { StartSong(2); return; } // Imperial
                if (Hit(mouseX, mouseY, wx + 468, octY, 85, 24)) { StartSong(3); return; } // Fanfare

                // 3. Stop button: wx + 470, wy + 52
                if (Hit(mouseX, mouseY, wx + 470, wy + 52, 85, 24))
                {
                    StopAudio();
                    return;
                }

                // 4. White piano keys (8 keys: C, D, E, F, G, A, B, C+)
                // Keys are positioned at wy + 210, width 58, height 130
                int[] whiteNoteIndices = new int[] { 0, 2, 4, 5, 7, 9, 11, 12 };
                int keyY = wy + 210;
                int keyW = 62;
                int keyH = 135;

                // Check black keys FIRST (they overlap white keys)
                // Black keys: C# (1), D# (3), F# (6), G# (8), A# (10)
                int blackW = 38;
                int blackH = 82;
                int[] blackNoteIndices = new int[] { 1, 3, 6, 8, 10 };
                int[] blackKeyPositions = new int[] { 0, 1, 3, 4, 5 }; // After which white key

                for (int b = 0; b < blackNoteIndices.Length; b++)
                {
                    int bx = wx + 20 + blackKeyPositions[b] * (keyW + 4) + (keyW - blackW / 2);
                    if (Hit(mouseX, mouseY, bx, keyY, blackW, blackH))
                    {
                        PlayNoteIndex(blackNoteIndices[b]);
                        return;
                    }
                }

                for (int w = 0; w < whiteNoteIndices.Length; w++)
                {
                    int kx = wx + 20 + w * (keyW + 4);
                    if (Hit(mouseX, mouseY, kx, keyY, keyW, keyH))
                    {
                        PlayNoteIndex(whiteNoteIndices[w]);
                        return;
                    }
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private void StartSong(int songId)
        {
            currentSongIndex = songId;
            songStepIndex = 0;
            songTimerTicks = 0;
            isPlayingSong = true;
        }

        private int[][] GetSongData(int id)
        {
            return id switch
            {
                1 => TetrisFrequencies,
                2 => ImperialFrequencies,
                3 => FanfareFrequencies,
                _ => MarioFrequencies
            };
        }

        private string GetSongName(int id)
        {
            return id switch
            {
                1 => "Tetris Korobeiniki",
                2 => "Marsz Imperialny",
                3 => "Fanfara ZOnderqOS",
                _ => "Super Mario Bros"
            };
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - HeaderHeight;

            Window.Render(canvas);

            // Background
            canvas.DrawFilledRectangle(Surface, wx, wy, ww, wh);

            // Demo player step tick
            if (isPlayingSong)
            {
                songTimerTicks++;
                int[][] song = GetSongData(currentSongIndex);
                if (songStepIndex < song.Length)
                {
                    int noteFreq = song[songStepIndex][0];
                    int noteDur = song[songStepIndex][1];
                    int noteFrames = Math.Max(3, noteDur / 30);

                    if (songTimerTicks % noteFrames == 0)
                    {
                        PlayTone(noteFreq, noteDur, "Nuta " + (songStepIndex + 1));
                        songStepIndex++;
                    }
                }
                else
                {
                    StopAudio();
                }
            }

            // Top Bar: Octave Controls & Presets
            int topBarY = wy + 14;
            canvas.DrawString("Oktawa:", Color.White, wx + 16, topBarY - 10);
            DrawPill(canvas, "Oktawa 3", wx + 16, topBarY + 4, 65, 24, currentOctave == 3);
            DrawPill(canvas, "Oktawa 4", wx + 86, topBarY + 4, 65, 24, currentOctave == 4);
            DrawPill(canvas, "Oktawa 5", wx + 156, topBarY + 4, 65, 24, currentOctave == 5);

            canvas.DrawString("Melodie demo (8-bit):", Color.White, wx + 240, topBarY - 10);
            DrawPill(canvas, "Mario", wx + 240, topBarY + 4, 70, 24, isPlayingSong && currentSongIndex == 0);
            DrawPill(canvas, "Tetris", wx + 316, topBarY + 4, 70, 24, isPlayingSong && currentSongIndex == 1);
            DrawPill(canvas, "Imperial", wx + 392, topBarY + 4, 70, 24, isPlayingSong && currentSongIndex == 2);
            DrawPill(canvas, "Fanfara", wx + 468, topBarY + 4, 85, 24, isPlayingSong && currentSongIndex == 3);

            // Oscilloscope & Display Box
            int dispY = wy + 52;
            int dispH = 138;
            canvas.DrawFilledRectangle(Panel, wx + 16, dispY, ww - 32, dispH);
            canvas.DrawRectangle(Border, wx + 16, dispY, ww - 32, dispH);

            // Status label & current note
            string modeText = isPlayingSong ? "ODTWARZANIE DEMO: " + GetSongName(currentSongIndex) : "TRYB SYNTEZATORA NA ZYWO (Steruj mysza lub klawiszami A-K)";
            canvas.DrawString(modeText, CyanColor, wx + 28, dispY + 10);
            canvas.DrawString("Biezacy dzwiek: " + lastPlayedNote, GoldColor, wx + 28, dispY + 28);
            canvas.DrawString("Uklad klawiatury: [A=C] [W=C#] [S=D] [E=D#] [D=E] [F=F] [T=F#] [G=G] [Y=G#] [H=A] [U=A#] [J=B] [K=C+]", MutedColor, wx + 28, dispY + 46);

            // Draw 8-bit Chiptune Square Waveform
            int waveY = dispY + 80;
            int waveH = 40;
            canvas.DrawFilledRectangle(Color.FromArgb(12, 16, 22), wx + 28, waveY, ww - 56, waveH);
            canvas.DrawRectangle(Color.FromArgb(35, 48, 65), wx + 28, waveY, ww - 56, waveH);

            Color waveColor = isPlayingSong ? GreenColor : CyanColor;
            int waveStep = 32;
            for (int x = wx + 34; x < wx + ww - 60; x += waveStep)
            {
                int highY = waveY + 8;
                int lowY = waveY + waveH - 8;
                // Draw square wave segments
                canvas.DrawLine(waveColor, x, highY, x + waveStep / 2, highY);
                canvas.DrawLine(waveColor, x + waveStep / 2, highY, x + waveStep / 2, lowY);
                canvas.DrawLine(waveColor, x + waveStep / 2, lowY, x + waveStep, lowY);
                canvas.DrawLine(waveColor, x + waveStep, lowY, x + waveStep, highY);
            }

            // Piano Keyboard
            int keyY = wy + 205;
            int keyW = 62;
            int keyH = 145;
            int[] whiteNoteIndices = new int[] { 0, 2, 4, 5, 7, 9, 11, 12 };

            // Render white keys
            for (int w = 0; w < whiteNoteIndices.Length; w++)
            {
                int kx = wx + 20 + w * (keyW + 4);
                bool isActive = activeKeyVisual == whiteNoteIndices[w];
                Color keyCol = isActive ? Color.FromArgb(200, 230, 255) : Color.FromArgb(245, 245, 250);
                canvas.DrawFilledRectangle(keyCol, kx, keyY, keyW, keyH);
                canvas.DrawRectangle(Color.FromArgb(100, 110, 125), kx, keyY, keyW, keyH);

                string keyName = NoteNames[whiteNoteIndices[w]] + currentOctave;
                canvas.DrawString(keyName, Color.FromArgb(40, 50, 60), kx + keyW / 2 - 10, keyY + keyH - 24);
            }

            // Render black keys (over white keys)
            int blackW = 38;
            int blackH = 88;
            int[] blackNoteIndices = new int[] { 1, 3, 6, 8, 10 };
            int[] blackKeyPositions = new int[] { 0, 1, 3, 4, 5 };

            for (int b = 0; b < blackNoteIndices.Length; b++)
            {
                int bx = wx + 20 + blackKeyPositions[b] * (keyW + 4) + (keyW - blackW / 2);
                bool isActive = activeKeyVisual == blackNoteIndices[b];
                Color keyCol = isActive ? AccentColor : Color.FromArgb(30, 36, 44);
                canvas.DrawFilledRectangle(keyCol, bx, keyY, blackW, blackH);
                canvas.DrawRectangle(Color.FromArgb(15, 20, 26), bx, keyY, blackW, blackH);

                string bName = NoteNames[blackNoteIndices[b]];
                canvas.DrawString(bName, Color.White, bx + 6, keyY + blackH - 22);
            }

            // Reset active visual after render
            if (activeKeyVisual >= 0)
                activeKeyVisual = -1;
        }

        private static void DrawPill(Canvas canvas, string text, int x, int y, int w, int h, bool active)
        {
            Color bg = active ? AccentColor : Panel;
            Color border = active ? Color.FromArgb(90, 180, 255) : Border;
            Color textCol = active ? Color.White : MutedColor;

            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawRectangle(border, x, y, w, h);
            canvas.DrawString(text, textCol, x + 8, y + 4);
        }
    }
}
