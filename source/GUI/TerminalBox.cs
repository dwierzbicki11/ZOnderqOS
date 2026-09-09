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

            if (line.Length == 0)
            {
                OutputLines.Add("");
                return;
            }

            // Zawijaj po słowach, a bardzo długie słowa dziel na części.
            int position = 0;
            while (position < line.Length)
            {
                int remaining = line.Length - position;
                int take = Math.Min(maxChars, remaining);

                if (take < remaining)
                {
                    int lastSpace = line.LastIndexOf(' ', position + take - 1, take);
                    if (lastSpace >= position)
                        take = lastSpace - position;
                }

                if (take <= 0)
                    take = Math.Min(maxChars, remaining);

                string chunk = line.Substring(position, take).TrimEnd();
                OutputLines.Add(chunk);

                position += take;
                while (position < line.Length && line[position] == ' ')
                    position++;
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
                string line = OutputLines[i] ?? "";
                int maxChars = GetMaxChars();
                if (line.Length > maxChars)
                    line = line.Substring(0, maxChars);

                canvas.DrawString(line, Font, TextColor, X + 8, currentY);
                currentY += lineHeight;
            }

            string prompt = Prompt ?? "> ";
            int maxInputChars = GetMaxChars();
            string displayLine = prompt + Text;

            if (displayLine.Length > maxInputChars)
                displayLine = displayLine.Substring(0, maxInputChars);

            if (IsFocused && displayLine.Length < maxInputChars)
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