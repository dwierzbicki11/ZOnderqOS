using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class TerminalBox : Widget
    {
        private const int MaxOutputLines = 400;
        private const int TrimBatch = 24;
        private const int HeaderHeight = 26;
        private const int ContentPadding = 10;

        private readonly ScrollBar scrollBar;
        private int scrollOffset;
        private bool userScrolled;

        private static readonly Color Surface = Color.FromArgb(14, 18, 23);
        private static readonly Color Header = Color.FromArgb(25, 31, 38);
        private static readonly Color Border = Color.FromArgb(58, 69, 80);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color Muted = Color.FromArgb(129, 145, 160);

        public string Text { get; private set; } = "";
        public string Prompt { get; set; } = "> ";
        public bool IsFocused { get; set; }
        public Font Font { get; set; }

        private float fontScale = 1.0f;
        public float FontScale
        {
            get { return fontScale; }
            set { fontScale = value <= 0f ? 1f : value; }
        }

        public List<string> OutputLines { get; } = new List<string>();
        public Color BackgroundColor { get; set; } = Surface;
        public Color TextColor { get; set; } = Color.FromArgb(213, 222, 230);
        public Color PromptColor { get; set; } = Color.FromArgb(96, 181, 238);

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
            scrollBar = new ScrollBar(X + Width - 12, Y + HeaderHeight + 4, 8,
                Math.Max(20, Height - HeaderHeight - 8));
            scrollBar.ValueChanged = value => scrollOffset = value;
        }

        private void AddOutputLine(string line)
        {
            if (OutputLines.Count >= MaxOutputLines)
            {
                int remove = Math.Min(TrimBatch, OutputLines.Count);
                OutputLines.RemoveRange(0, remove);
                scrollOffset = Math.Max(0, scrollOffset - remove);
            }

            OutputLines.Add(line ?? "");

            if (!userScrolled)
                ScrollToBottom();
            else
                UpdateScrollBar();
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
            userScrolled = false;
            scrollOffset = 0;
            UpdateScrollBar();
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
            return Math.Max(1, (Width - ContentPadding * 2 - 14) / GetCharWidth());
        }

        private int GetVisibleLines()
        {
            int contentHeight = Height - HeaderHeight - ContentPadding - 8;
            return Math.Max(1, contentHeight / GetLineHeight());
        }

        private int GetMaxScroll()
        {
            return Math.Max(0, OutputLines.Count - Math.Max(1, GetVisibleLines() - 1));
        }

        private void UpdateScrollBar()
        {
            int visible = Math.Max(1, GetVisibleLines() - 1);
            scrollBar.X = X + Math.Max(1, Width - 12);
            scrollBar.Y = Y + HeaderHeight + 4;
            scrollBar.Width = 8;
            scrollBar.Height = Math.Max(20, Height - HeaderHeight - 8);
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
            if (!Visible)
                return false;

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
            if (!IsFocused || !Visible)
                return;

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

        private void DrawTerminalString(Canvas canvas, string text, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text) || Font == null)
                return;
            canvas.DrawString(text, Font, color, x, y);
        }

        private void RenderHeader(Canvas canvas)
        {
            canvas.DrawFilledRectangle(Header, X + 1, Y + 1, Width - 2, HeaderHeight - 1);
            canvas.DrawFilledRectangle(Accent, X + 1, Y + 1, 3, HeaderHeight - 1);
            IconManager.DrawScaled(canvas, IconType.Terminal, X + 10, Y + 5, 16, 16);
            SmallTextRenderer.Draw(canvas, "SHELL", X + 34, Y + 10, Color.FromArgb(200, 211, 221));

            string state = userScrolled ? "SCROLLED" : (IsFocused ? "READY" : "IDLE");
            int stateWidth = SmallTextRenderer.Width(state);
            SmallTextRenderer.Draw(canvas, state, X + Width - 18 - stateWidth, Y + 10,
                userScrolled ? Color.FromArgb(210, 174, 95) : Muted);

            canvas.DrawLine(Color.FromArgb(45, 54, 64), X + 1, Y + HeaderHeight,
                X + Width - 2, Y + HeaderHeight);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateScrollBar();
            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Accent : Border, X, Y, Width, Height);
            RenderHeader(canvas);

            int lineHeight = GetLineHeight();
            int visibleLines = GetVisibleLines();
            int maxScroll = GetMaxScroll();
            int startLine = Math.Max(0, Math.Min(scrollOffset, maxScroll));
            int endLine = Math.Min(OutputLines.Count, startLine + Math.Max(0, visibleLines - 1));
            int currentY = Y + HeaderHeight + 8;
            int maxChars = GetMaxChars();

            for (int i = startLine; i < endLine; i++)
            {
                string line = OutputLines[i] ?? "";
                if (line.Length <= maxChars)
                    DrawTerminalString(canvas, line, X + ContentPadding, currentY, TextColor);
                else
                    DrawTerminalString(canvas, line.Substring(0, maxChars), X + ContentPadding, currentY, TextColor);
                currentY += lineHeight;
            }

            if (startLine >= maxScroll)
            {
                string prompt = Prompt ?? "> ";
                int charWidth = GetCharWidth();
                int promptChars = Math.Min(prompt.Length, maxChars);
                int textChars = Math.Min(Text.Length, Math.Max(0, maxChars - promptChars));

                if (promptChars == prompt.Length)
                    DrawTerminalString(canvas, prompt, X + ContentPadding, currentY, PromptColor);
                else
                    DrawTerminalString(canvas, prompt.Substring(0, promptChars), X + ContentPadding, currentY, PromptColor);

                if (textChars > 0)
                {
                    if (textChars == Text.Length)
                        DrawTerminalString(canvas, Text, X + ContentPadding + promptChars * charWidth, currentY, TextColor);
                    else
                        DrawTerminalString(canvas, Text.Substring(0, textChars), X + ContentPadding + promptChars * charWidth, currentY, TextColor);
                }

                if (IsFocused && promptChars + textChars < maxChars)
                {
                    int cursorX = X + ContentPadding + (promptChars + textChars) * charWidth;
                    canvas.DrawFilledRectangle(PromptColor, cursorX, currentY + 3, 2,
                        Math.Max(8, lineHeight - 7));
                }
            }

            scrollBar.Render(canvas);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;
        }
    }
}
