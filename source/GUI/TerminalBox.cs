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
        private const int MaxOutputLines = 3000;

        public string Text { get; private set; } = "";
        public string Prompt { get; set; } = "> ";
        public bool IsFocused { get; set; } = false;
        public Font Font { get; set; }

        // PCScreenFont.DefaultFont is rendered directly. Do not create Canvas/Bitmap
        // objects while drawing the terminal: those native graphics allocations are
        // expensive and can accumulate in a long-running GUI process.
        private float fontScale = 1.0f;
        public float FontScale
        {
            get { return fontScale; }
            set { fontScale = value <= 0f ? 1f : value; }
        }

        public List<string> OutputLines { get; } = new List<string>();

        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 15);
        public Color TextColor { get; set; } = Color.GreenYellow;

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
        }

        private void AddOutputLine(string line)
        {
            OutputLines.Add(line ?? "");

            // Terminal history must remain bounded. Otherwise commands producing lots
            // of output eventually consume all managed memory even without graphics.
            while (OutputLines.Count > MaxOutputLines)
                OutputLines.RemoveAt(0);
        }

        public void PrintLine(string line)
        {
            if (line == null)
                line = "";

            int maxChars = GetMaxChars();
            if (maxChars <= 0)
                return;

            if (line.Length == 0)
            {
                AddOutputLine("");
                return;
            }

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

                AddOutputLine(line.Substring(position, take).TrimEnd());
                position += take;
                while (position < line.Length && line[position] == ' ')
                    position++;
            }
        }

        public void ClearOutput()
        {
            OutputLines.Clear();
        }

        private int GetCharWidth()
        {
            if (Font == null || Font.Width <= 0)
                return 1;
            return Font.Width;
        }

        private int GetLineHeight()
        {
            if (Font == null || Font.Height <= 0)
                return 1;
            return Font.Height;
        }

        private int GetMaxChars()
        {
            return Math.Max(1, (Width - 16) / GetCharWidth());
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

        private void DrawTerminalString(Canvas canvas, string text, int x, int y)
        {
            if (string.IsNullOrEmpty(text) || Font == null)
                return;

            // IMPORTANT: draw directly to the framebuffer-backed Canvas.
            // Never use GetImage()/DrawImage() here. GetImage creates a native Bitmap
            // and doing that once per line/frame caused the terminal's RAM usage to
            // grow continuously during normal GUI operation.
            canvas.DrawString(text, Font, TextColor, x, y);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            int lineHeight = GetLineHeight();
            int maxVisibleLines = Math.Max(1, (Height - 16) / lineHeight);
            int startLine = Math.Max(0, OutputLines.Count - (maxVisibleLines - 1));
            int currentY = Y + 8;
            int maxChars = GetMaxChars();

            for (int i = startLine; i < OutputLines.Count; i++)
            {
                string line = OutputLines[i] ?? "";
                if (line.Length > maxChars)
                    line = line.Substring(0, maxChars);

                DrawTerminalString(canvas, line, X + 8, currentY);
                currentY += lineHeight;
            }

            string prompt = Prompt ?? "> ";
            string displayLine = prompt + Text;
            if (displayLine.Length > maxChars)
                displayLine = displayLine.Substring(0, maxChars);
            if (IsFocused && displayLine.Length < maxChars)
                displayLine += "_";

            DrawTerminalString(canvas, displayLine, X + 8, currentY);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= X + Width &&
                   mouseY >= Y && mouseY <= Y + Height;
        }
    }
}