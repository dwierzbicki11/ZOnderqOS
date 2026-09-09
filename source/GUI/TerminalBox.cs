using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI
{
    public class TerminalBox : Widget
    {
        public string Text { get; private set; } = "";
        public string Prompt { get; set; } = "> ";
        public bool IsFocused { get; set; } = false;
        public Font Font { get; set; }
        
        // Bufor historii (to co terminal wypisuje wyżej)
        public List<string> OutputLines { get; } = new List<string>();

        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 15);
        public Color TextColor { get; set; } = Color.GreenYellow;

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
        }

        // Dodawanie nowej linii do historii widocznej w oknie
        public void PrintLine(string line)
        {
            OutputLines.Add(line);
        }

        public void HandleKey(KeyEvent key)
        {
            if (!IsFocused || !Visible) return;

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (Text.Length > 0)
                    Text = Text.Substring(0, Text.Length - 1);
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                // Zabezpieczenie szerokości
                int maxChars = (Width - 24) / Font.Width;
                if ((Prompt.Length + Text.Length) < maxChars)
                {
                    Text += key.KeyChar;
                }
            }
        }

        public void ClearInput()
        {
            Text = "";
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            // 1. Tło
            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            // 2. Obliczanie pojemności (ile linijek zmieści się w pionie)
            int lineHeight = Font.Height;
            int maxVisibleLines = (Height - 16) / lineHeight; // 16px marginesu

            // 3. Ustalenie, od którego wiersza w historii zacząć rysować (przewijanie)
            int startLine = 0;
            // Zostawiamy 1 linię na wpisywany tekst (Prompt + Text)
            if (OutputLines.Count > maxVisibleLines - 1)
            {
                startLine = OutputLines.Count - (maxVisibleLines - 1);
            }

            int currentY = Y + 8; // Margines górny

            // 4. Rysowanie linijek z historii
            for (int i = startLine; i < OutputLines.Count; i++)
            {
                canvas.DrawString(OutputLines[i], Font, TextColor, X + 8, currentY);
                currentY += lineHeight;
            }

            // 5. Rysowanie aktualnego paska wejścia na samym dole
            string cursorChar = IsFocused ? "_" : "";
            string displayLine = Prompt + Text + cursorChar;
            canvas.DrawString(displayLine, Font, TextColor, X + 8, currentY);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= (X + Width) && mouseY >= Y && mouseY <= (Y + Height);
        }
    }
}