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
        // Keep GUI terminal history bounded so repeated commands cannot grow RAM forever.
        private const int MaxOutputLines = 400;
        private readonly ScrollBar scrollBar;
        private int scrollOffset;
        private bool userScrolled;

        public string Text { get; private set; } = "";
        public string Prompt { get; set; } = "> ";
        public bool IsFocused { get; set; } = false;
        public Font Font { get; set; }

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
            scrollBar = new ScrollBar(X + Width - 14, Y + 2, 12, Math.Max(20, Height - 4));
            scrollBar.ValueChanged = value => scrollOffset = value;
        }

        private void AddOutputLine(string line)
        {
            OutputLines.Add(line ?? "");
            while (OutputLines.Count > MaxOutputLines)
                OutputLines.RemoveAt(0);

            if (!userScrolled)
                ScrollToBottom();
            else
                UpdateScrollBar();
        }

        public void PrintLine(string line)
        {
            if (line == null) line = "";
            int maxChars = GetMaxChars();
            if (maxChars <= 0) return;

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

                if (take <= 0) take = Math.Min(maxChars, remaining);
                AddOutputLine(line.Substring(position, take).TrimEnd());
                position += take;
                while (position < line.Length && line[position] == ' ')
                    position++;
            }
        }

        public void ClearOutput()
        {
            OutputLines.Clear();
            userScrolled = false;
            scrollOffset = 0;
            UpdateScrollBar();
        }

        private int GetCharWidth()
        {
            if (Font == null || Font.Width <= 0) return 1;
            return Font.Width;
        }

        private int GetLineHeight()
        {
            if (Font == null || Font.Height <= 0) return 1;
            return Font.Height;
        }

        private int GetMaxChars()
        {
            return Math.Max(1, (Width - 28) / GetCharWidth());
        }

        private int GetVisibleLines()
        {
            return Math.Max(1, (Height - 16) / GetLineHeight());
        }

        private int GetMaxScroll()
        {
            return Math.Max(0, OutputLines.Count - Math.Max(1, GetVisibleLines() - 1));
        }

        private void UpdateScrollBar()
        {
            int visible = Math.Max(1, GetVisibleLines() - 1);
            scrollBar.X = X + Math.Max(1, Width - 14);
            scrollBar.Y = Y + 2;
            scrollBar.Width = 12;
            scrollBar.Height = Math.Max(20, Height - 4);
            scrollBar.SetRange(OutputLines.Count + 1, visible);
            scrollBar.Value = Math.Max(0, Math.Min(scrollOffset, GetMaxScroll()));
        }

        private void ScrollToBottom()
        {
            scrollOffset = GetMaxScroll();
            userScrolled = false;
            UpdateScrollBar();
        }

        public bool HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible) return false;

            UpdateScrollBar();
            if (scrollBar.HandleMouse(mouseX, mouseY, isClicked, wasClicked))
            {
                userScrolled = scrollOffset < GetMaxScroll();
                return true;
            }

            if (isClicked && !wasClicked && Contains(mouseX, mouseY))
            {
                IsFocused = true;
                return true;
            }

            return false;
        }

        public void HandleKey(KeyEvent key)
        {
            if (!IsFocused || !Visible) return;

            if (key.Key == ConsoleKeyEx.PageUp)
            {
                scrollOffset = Math.Max(0, scrollOffset - Math.Max(1, GetVisibleLines() - 2));
                userScrolled = true;
                UpdateScrollBar();
                return;
            }

            if (key.Key == ConsoleKeyEx.PageDown)
            {
                scrollOffset = Math.Min(GetMaxScroll(), scrollOffset + Math.Max(1, GetVisibleLines() - 2));
                userScrolled = scrollOffset < GetMaxScroll();
                UpdateScrollBar();
                return;
            }

            if (key.Key == ConsoleKeyEx.End)
            {
                ScrollToBottom();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (Text.Length > 0) Text = Text.Substring(0, Text.Length - 1);
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                int maxChars = GetMaxChars();
                int promptChars = Prompt == null ? 0 : Prompt.Length;
                if (promptChars + Text.Length < maxChars)
                    Text += key.KeyChar;
            }
        }

        public void ClearInput() { Text = ""; }

        private void DrawTerminalString(Canvas canvas, string text, int x, int y)
        {
            if (string.IsNullOrEmpty(text) || Font == null) return;
            canvas.DrawString(text, Font, TextColor, x, y);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            UpdateScrollBar();
            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            int lineHeight = GetLineHeight();
            int visibleLines = GetVisibleLines();
            int maxScroll = GetMaxScroll();
            int startLine = Math.Max(0, Math.Min(scrollOffset, maxScroll));
            int endLine = Math.Min(OutputLines.Count, startLine + Math.Max(0, visibleLines - 1));
            int currentY = Y + 8;
            int maxChars = GetMaxChars();

            for (int i = startLine; i < endLine; i++)
            {
                string line = OutputLines[i] ?? "";
                if (line.Length > maxChars) line = line.Substring(0, maxChars);
                DrawTerminalString(canvas, line, X + 8, currentY);
                currentY += lineHeight;
            }

            if (startLine >= maxScroll)
            {
                string prompt = Prompt ?? "> ";
                string displayLine = prompt + Text;
                if (displayLine.Length > maxChars) displayLine = displayLine.Substring(0, maxChars);
                if (IsFocused && displayLine.Length < maxChars) displayLine += "_";
                DrawTerminalString(canvas, displayLine, X + 8, currentY);
            }

            scrollBar.Render(canvas);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= X + Width && mouseY >= Y && mouseY <= Y + Height;
        }
    }
}