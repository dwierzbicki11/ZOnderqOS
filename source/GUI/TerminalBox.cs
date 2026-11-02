using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
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
        // Detect unchanged idle frames before computing scrollbar geometry.
        // Widget layout is mutable, so track its raw coordinates and size;
        // the scrollbar's ValueChanged callback always updates scrollOffset.
        private bool scrollBarLayoutCached;
        private int cachedBoxX, cachedBoxY, cachedBoxWidth, cachedBoxHeight;
        private int cachedOutputCount, cachedScrollOffset;

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
            AppendOutputLine(line);
            FinishOutputUpdate();
        }

        // Batch terminal output without recalculating scrollbar geometry after
        // each wrapped line. The final range is published once per PrintLine.
        private void AppendOutputLine(string line)
        {
            if (OutputLines.Count >= MaxOutputLines)
            {
                int remove = Math.Min(TrimBatch, OutputLines.Count);
                OutputLines.RemoveRange(0, remove);
                scrollOffset = Math.Max(0, scrollOffset - remove);
            }

            OutputLines.Add(line ?? "");
        }

        private void FinishOutputUpdate()
        {
            if (!userScrolled)
                ScrollToBottom();
            else
                UpdateScrollBar();
        }

        public void PrintLine(string line)
        {
            PrintLineInternal(line, false);
        }

        // During command output, the final scrollbar range is sufficient for
        // auto-follow. Keep per-row updates when the user has scrolled up.
        private void PrintLineInternal(string line, bool deferAutoFollow)
        {
            if (line == null)
                line = "";
            PrintLineSlice(line, 0, line.Length, deferAutoFollow);
        }

        // Decode directly from the command output's original string. A
        // Substring for each CR/LF-delimited line used to allocate a second
        // string before the wrapped line was stored in OutputLines. Keep only
        // the actual retained, trimmed display rows.
        private void PrintLineSlice(string line, int start, int length, bool deferAutoFollow)
        {
            int maxChars = GetMaxChars();
            if (maxChars <= 0)
                return;

            if (length == 0)
            {
                AddOutputLine("");
                return;
            }

            int position = start;
            int end = start + length;
            while (position < end)
            {
                int remaining = end - position;
                int take = Math.Min(maxChars, remaining);

                if (take < remaining)
                {
                    int lastSpace = line.LastIndexOf(' ', position + take - 1, take);
                    if (lastSpace >= position)
                        take = lastSpace - position;
                }

                if (take <= 0)
                    take = Math.Min(maxChars, remaining);

                // Preserve per-row scrollbar callbacks for manual scrolling.
                string wrapped = line.Substring(position, take).TrimEnd();
                if (userScrolled)
                    AddOutputLine(wrapped);
                else
                    AppendOutputLine(wrapped);
                position += take;
                while (position < end && line[position] == ' ')
                    position++;
            }

            if (!userScrolled && !deferAutoFollow)
                FinishOutputUpdate();
        }

        /// <summary>
        /// Stream command output directly into terminal lines. Avoid allocating
        /// a normalized copy of the entire output and a Split() array, which
        /// can be large for directory listings and diagnostics.
        /// </summary>
        public void PrintCommandOutput(string output)
        {
            if (string.IsNullOrEmpty(output))
                return;

            int start = 0;
            bool deferredAutoFollow = false;
            for (int i = 0; i <= output.Length; i++)
            {
                if (i < output.Length && output[i] != '\r' && output[i] != '\n')
                    continue;

                int length = i - start;
                if (length > 0)
                {
                    // Compare the clear marker without allocating a line.
                    if (length == 11 &&
                        string.CompareOrdinal(output, start, "\u0001GUI_CLEAR\u0001", 0, length) == 0)
                        ClearOutput();
                    else
                    {
                        PrintLineSlice(output, start, length, true);
                        if (!userScrolled)
                            deferredAutoFollow = true;
                    }
                }

                // The legacy Replace("\r\n", "\n").Replace("\r", "\n")
                // treats CRLF as one separator, not two empty lines.
                if (i < output.Length && output[i] == '\r' &&
                    i + 1 < output.Length && output[i + 1] == '\n')
                    i++;
                start = i + 1;
            }

            // Avoid hundreds of redundant SetRange/Value operations for a
            // command listing. The final state matches per-line auto-follow.
            if (deferredAutoFollow && !userScrolled)
                FinishOutputUpdate();
        }

        public void ClearOutput()
        {
            OutputLines.Clear();
            userScrolled = false;
            scrollOffset = 0;
            UpdateScrollBar();
        }

        internal void ShowMonitor(IReadOnlyList<string> lines, int firstLine)
        {
            // Htop replaces the entire visible snapshot on each refresh.
            // Avoid ClearOutput plus a redundant scrollbar update per row.
            OutputLines.Clear();
            userScrolled = false;
            scrollOffset = 0;
            int width = GetMaxChars();
            int count = Math.Max(1, GetVisibleLines() - 1);
            for (int index = Math.Max(0, firstLine); index < lines.Count && count > 0; index++, count--)
            {
                string line = lines[index];
                OutputLines.Add(line.Length > width ? line.Substring(0, width) : line);
            }
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
            // This is called on every terminal render and mouse poll. The
            // common unchanged-frame path must do less work than SetRange,
            // including avoiding GetVisibleLines/GetMaxScroll calculations.
            int count = OutputLines.Count;
            if (scrollBarLayoutCached && cachedBoxX == X && cachedBoxY == Y &&
                cachedBoxWidth == Width && cachedBoxHeight == Height &&
                cachedOutputCount == count && cachedScrollOffset == scrollOffset)
                return;

            int visible = Math.Max(1, GetVisibleLines() - 1);
            scrollBar.X = X + Math.Max(1, Width - 12);
            scrollBar.Y = Y + HeaderHeight + 4;
            scrollBar.Width = 8;
            scrollBar.Height = Math.Max(20, Height - HeaderHeight - 8);
            scrollBar.SetRange(count + 1, visible);
            scrollBar.Value = Math.Max(0, Math.Min(scrollOffset, GetMaxScroll()));
            cachedBoxX = X;
            cachedBoxY = Y;
            cachedBoxWidth = Width;
            cachedBoxHeight = Height;
            cachedOutputCount = count;
            // SetRange/Value may clamp the offset through ValueChanged.
            cachedScrollOffset = scrollOffset;
            scrollBarLayoutCached = true;
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

        public void SetInput(string text)
        {
            string value = text ?? string.Empty;
            int maxChars = GetMaxChars();
            int promptChars = Prompt == null ? 0 : Prompt.Length;
            int capacity = Math.Max(0, maxChars - promptChars);
            Text = value.Length <= capacity ? value : value.Substring(0, capacity);
            ScrollToBottom();
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
