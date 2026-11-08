using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class QuickNotesApp : Application
    {
        private static readonly Color[] NoteColors = new Color[]
        {
            Color.FromArgb(254, 240, 138), // 0: Classic Yellow
            Color.FromArgb(187, 247, 208), // 1: Mint Green
            Color.FromArgb(186, 230, 253), // 2: Sky Blue
            Color.FromArgb(254, 205, 211)  // 3: Rose Pink
        };

        private static readonly Color NoteDarkBorder = Color.FromArgb(180, 160, 60);
        private static readonly Color TextColor = Color.FromArgb(30, 30, 30);
        private static readonly Color HeaderBg = Color.FromArgb(245, 225, 110);
        private const string NotesFilePath = "/root/notes.txt";

        private int activeColorIndex = 0;
        private readonly List<string> lines = new List<string>(16);
        private int cursorRow = 0;
        private readonly Action closeCallback;
        private string statusMessage = "Autozapis aktywny";
        private long lastSaveTicks = 0;

        public QuickNotesApp(int x, int y, Action onClose) : base("Szybkie Notatki")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 340, 280, "📌 Szybkie Notatki");
            Window.CloseAction = Close;

            LoadNotes();
        }

        public override void Close()
        {
            SaveNotes();
            base.Close();
            closeCallback?.Invoke();
        }

        private void LoadNotes()
        {
            lines.Clear();
            if (File.Exists(NotesFilePath))
            {
                try
                {
                    string[] saved = File.ReadAllLines(NotesFilePath);
                    for (int i = 0; i < saved.Length && i < 12; i++)
                    {
                        lines.Add(saved[i]);
                    }
                }
                catch { }
            }

            if (lines.Count == 0)
            {
                lines.Add("★ Nowa notatka");
                lines.Add("System zaktualizowany!");
                lines.Add("Kliknij i wpisuj tekst...");
            }

            cursorRow = lines.Count - 1;
        }

        private void SaveNotes()
        {
            try
            {
                File.WriteAllLines(NotesFilePath, lines.ToArray());
                statusMessage = "Zapisano " + DateTime.Now.ToString("HH:mm:ss");
            }
            catch
            {
                statusMessage = "Blad zapisu";
            }
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

            if (key.Key == ConsoleKeyEx.Enter)
            {
                if (lines.Count < 11)
                {
                    lines.Add("");
                    cursorRow = lines.Count - 1;
                    SaveNotes();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (cursorRow >= 0 && cursorRow < lines.Count)
                {
                    string cur = lines[cursorRow];
                    if (cur.Length > 0)
                    {
                        lines[cursorRow] = cur.Substring(0, cur.Length - 1);
                    }
                    else if (lines.Count > 1)
                    {
                        lines.RemoveAt(cursorRow);
                        cursorRow = Math.Max(0, lines.Count - 1);
                    }
                    SaveNotes();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (cursorRow > 0) cursorRow--;
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (cursorRow < lines.Count - 1) cursorRow++;
                return;
            }

            // Printable character
            if (key.KeyChar >= 32 && key.KeyChar <= 126)
            {
                if (cursorRow >= 0 && cursorRow < lines.Count)
                {
                    if (lines[cursorRow].Length < 38)
                    {
                        lines[cursorRow] += key.KeyChar;
                        SaveNotes();
                    }
                }
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized) return;

            if (leftClicked && !leftWasClicked)
            {
                int wx = Window.X;
                int wy = Window.Y + 38;

                // Color selector pills (wx + 10, wy + 6, 4 pills of 18x18)
                for (int c = 0; c < NoteColors.Length; c++)
                {
                    int px = wx + 10 + c * 24;
                    int py = wy + 6;
                    if (Hit(mouseX, mouseY, px, py, 18, 18))
                    {
                        activeColorIndex = c;
                        return;
                    }
                }

                // [WYCZYŚĆ] button (wx + 180, wy + 4, 65, 20)
                if (Hit(mouseX, mouseY, wx + 180, wy + 4, 65, 20))
                {
                    lines.Clear();
                    lines.Add("");
                    cursorRow = 0;
                    SaveNotes();
                    return;
                }

                // [ZAPISZ] button (wx + 252, wy + 4, 75, 20)
                if (Hit(mouseX, mouseY, wx + 252, wy + 4, 75, 20))
                {
                    SaveNotes();
                    return;
                }

                // Click line to set cursorRow
                int textStartY = wy + 36;
                for (int i = 0; i < lines.Count; i++)
                {
                    int ly = textStartY + i * 18;
                    if (Hit(mouseX, mouseY, wx + 10, ly, Window.Width - 20, 18))
                    {
                        cursorRow = i;
                        return;
                    }
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized) return;

            Window.Render(canvas);

            int wx = Window.X;
            int wy = Window.Y + 38;
            int ww = Window.Width;
            int wh = Window.Height - 38;

            Color noteBg = NoteColors[activeColorIndex];
            Color noteBorder = Color.FromArgb(
                Math.Max(0, noteBg.R - 40),
                Math.Max(0, noteBg.G - 40),
                Math.Max(0, noteBg.B - 40));

            // Note Background
            canvas.DrawFilledRectangle(noteBg, wx, wy, ww, wh);

            // Top Note Toolbar
            canvas.DrawFilledRectangle(noteBorder, wx, wy, ww, 28);
            
            // Color picker pills
            for (int c = 0; c < NoteColors.Length; c++)
            {
                int px = wx + 10 + c * 24;
                int py = wy + 5;
                canvas.DrawFilledRectangle(NoteColors[c], px, py, 18, 18);
                if (c == activeColorIndex)
                {
                    canvas.DrawRectangle(Color.Black, px, py, 18, 18);
                }
            }

            // Buttons
            UiPainter.RoundedPanel(canvas, wx + 180, wy + 4, 65, 20, 3, noteBg, noteBorder);
            SmallTextRenderer.DrawCentered(canvas, "WYCZYSC", wx + 180, wy + 8, 65, TextColor);

            UiPainter.RoundedPanel(canvas, wx + 252, wy + 4, 75, 20, 3, Color.FromArgb(40, 140, 230), Color.FromArgb(30, 110, 190));
            SmallTextRenderer.DrawCentered(canvas, "ZAPISZ", wx + 252, wy + 8, 75, Color.White);

            // Note lines & text
            int textY = wy + 36;
            for (int i = 0; i < lines.Count; i++)
            {
                // Ruled line
                canvas.DrawLine(noteBorder, wx + 8, textY + 16, wx + ww - 8, textY + 16);

                string lineText = lines[i];
                if (i == cursorRow)
                {
                    // Blinking or static cursor indicator
                    lineText += " |";
                }

                SmallTextRenderer.Draw(canvas, lineText, wx + 12, textY, TextColor);
                textY += 18;
            }

            // Footer info
            SmallTextRenderer.Draw(canvas, statusMessage, wx + 12, wy + wh - 18, Color.FromArgb(100, 100, 100));
            SmallTextRenderer.Draw(canvas, "Plik: /root/notes.txt", wx + ww - 150, wy + wh - 18, Color.FromArgb(100, 100, 100));
        }
    }
}
