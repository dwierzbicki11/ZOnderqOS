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

        public List<string> OutputLines { get; } = new List<string>();

        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 15);
        public Color TextColor { get; set; } = Color.GreenYellow;

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
        }

        public void PrintLine(string line)
        {
            if (line == null)
            {
                OutputLines.Add("");
                return;
            }

            int maxChars = GetMaxChars();
            if (maxChars <= 0)
                return;

            // Długie komunikaty są dzielone na linie, żeby nigdy nie wyjechały
            // poza szerokość terminala.
            if (line.Length == 0)
            {
                OutputLines.Add("");
                return;
            }

            for (int start = 0; start < line.Length; start += maxChars)
            {
                int count = Math.Min(maxChars, line.Length - start);
                OutputLines.Add(line.Substring(start, count));
            }
        }

        private int GetMaxChars()
        {
            if (Font == null || Font.Width <= 0)
                return 1;

            return Math.Max(1, (Width - 16) / Font.Width);
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
                int maxChars = GetMaxChars();
                int promptChars = Prompt == null ? 0 : Prompt.Length;

                // Zostaw miejsce na prompt. Sam tekst wejściowy nie może
                // przekroczyć prawej krawędzi TerminalBox.
                if (promptChars + Text.Length < maxChars)
                    Text += key.KeyChar;
            }
        }

        public void ClearInput()
        {
            Text = "";
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            int lineHeight = Font.Height;
            int maxVisibleLines = Math.Max(1, (Height - 16) / lineHeight);

            int startLine = 0;
            if (OutputLines.Count > maxVisibleLines - 1)
                startLine = OutputLines.Count - (maxVisibleLines - 1);

            int currentY = Y + 8;

            for (int i = startLine; i < OutputLines.Count; i++)
            {
                canvas.DrawString(OutputLines[i], Font, TextColor, X + 8, currentY);
                currentY += lineHeight;
            }

            // Linia wejściowa zawsze mieści się w szerokości terminala.
            string prompt = Prompt ?? "> ";
            int maxChars = GetMaxChars();
            string displayLine = prompt + Text;

            if (displayLine.Length > maxChars)
                displayLine = displayLine.Substring(0, maxChars);

            if (IsFocused && displayLine.Length < maxChars)
                displayLine += "_";

            canvas.DrawString(displayLine, Font, TextColor, X + 8, currentY);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= (X + Width) &&
                   mouseY >= Y && mouseY <= (Y + Height);
        }
    }
}