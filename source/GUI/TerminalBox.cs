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
        private Font cachedScrollFont;

        // A terminal line can become wider than the viewport after a resize.
        // Cache only the prefixes actually drawn; otherwise Substring would
        // allocate again on every frame of a narrow window. Entries are keyed
        // by source string identity AND width, so external OutputLines edits
        // and dynamic window resizing cannot display stale text.
        private struct RenderClip
        {
            internal string Source;
            internal string Visible;
            internal int MaxChars;
        }

        private RenderClip[] renderClips;
        private int lastRenderedClipRows;
        private RenderClip promptRenderClip;
        private RenderClip inputRenderClip;

        // Resizing a window can also clip a long prompt or a command entered
        // at the old width. These otherwise allocate on every rendered frame.
        private static string ClipText(ref RenderClip cache, string text, int maxChars)
        {
            if (text.Length <= maxChars)
            {
                cache = default;
                return text;
            }
            if (!ReferenceEquals(cache.Source, text) || cache.MaxChars != maxChars)
            {
                cache.Source = text;
                cache.MaxChars = maxChars;
                cache.Visible = text.Substring(0, maxChars);
            }
            return cache.Visible;
        }

        internal string ClipPromptForRender(string prompt, int maxChars) =>
            ClipText(ref promptRenderClip, prompt, maxChars);

        internal string ClipInputForRender(string text, int maxChars) =>
            ClipText(ref inputRenderClip, text, maxChars);

        private void ClearRenderClips()
        {
            if (renderClips != null)
                Array.Clear(renderClips, 0, renderClips.Length);
            lastRenderedClipRows = 0;
        }

        // Kept internal for host A/B tests of the actual production path.
        internal string ClipLineForRender(string line, int visibleRow, int maxChars)
        {
            if (line.Length <= maxChars)
            {
                if (renderClips != null && (uint)visibleRow < (uint)renderClips.Length)
                    renderClips[visibleRow] = default;
                return line;
            }

            if (visibleRow < 0 || visibleRow >= MaxOutputLines)
                return line.Substring(0, maxChars);

            if (renderClips == null || visibleRow >= renderClips.Length)
            {
                // Allocate for the viewport, not for every history entry.
                int size = Math.Min(MaxOutputLines, Math.Max(visibleRow + 1, GetVisibleLines()));
                Array.Resize(ref renderClips, size);
            }

            ref RenderClip cached = ref renderClips[visibleRow];
            if (!ReferenceEquals(cached.Source, line) || cached.MaxChars != maxChars)
            {
                cached.Source = line;
                cached.MaxChars = maxChars;
                cached.Visible = line.Substring(0, maxChars);
            }
            return cached.Visible;
        }

        private void FinishRenderClips(int visibleRows)
        {
            if (renderClips == null)
                return;
            int count = Math.Min(visibleRows, renderClips.Length);
            if (count < lastRenderedClipRows)
                Array.Clear(renderClips, count, lastRenderedClipRows - count);
            lastRenderedClipRows = count;
        }

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
                ClearRenderClips();
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

        // Trim trailing whitespace directly in the source slice. The previous
        // Substring(...).TrimEnd() created two strings when a wrapped row ended
        // in whitespace. Char.IsWhiteSpace matches TrimEnd()'s Unicode rules;
        // a fully blank slice can reuse String.Empty.
        internal static string SliceTrimmedForOutput(string source, int start, int length)
        {
            // Most command output rows end in a printable ASCII character.
            // Skip span construction and Unicode whitespace scanning when
            // the last character cannot possibly be trimmed.
            if (length > 0)
            {
                char last = source[start + length - 1];
                if (last > ' ' && last <= '~')
                    return source.Substring(start, length);
            }

            // Span.TrimEnd uses the runtime's whitespace scanner without
            // first allocating the untrimmed intermediate substring.
            ReadOnlySpan<char> visible = source.AsSpan(start, length).TrimEnd();
            return visible.IsEmpty ? string.Empty : visible.ToString();
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

                // A single PrintLine can wrap into dozens of display rows.
                // Publish only after the final row, except when a history
                // trim reduces the scrollbar maximum. The old per-row path
                // clamps the scroll offset at that exact point; deferring
                // this clamp would jump the user's scrolled viewport.
                string wrapped = SliceTrimmedForOutput(line, position, take);
                bool trimsHistory = OutputLines.Count >= MaxOutputLines;
                AppendOutputLine(wrapped);
                if (userScrolled && trimsHistory)
                    UpdateScrollBar();
                position += take;
                while (position < end && line[position] == ' ')
                    position++;
            }

            if (!deferAutoFollow)
                FinishOutputUpdate();
        }

        // Return the absolute next CR/LF index, or Length when none exists.
        // Exposed to host smoke tests to compare against the original scalar
        // scanner; the production parser still handles CRLF as one separator.
        internal static int FindNextOutputBreak(string output, int start)
        {
            int relative = output.AsSpan(start).IndexOfAny('\r', '\n');
            return relative < 0 ? output.Length : start + relative;
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
            bool deferredScrollUpdate = false;
            while (start <= output.Length)
            {
                // IndexOfAny scans long non-delimiter runs in the runtime's
                // optimized span implementation. Do not allocate Split arrays,
                // normalized strings, or a per-line delimiter buffer.
                int end = FindNextOutputBreak(output, start);
                int length = end - start;
                if (length > 0)
                {
                    // Compare the clear marker without allocating a line.
                    if (length == 11 &&
                        string.CompareOrdinal(output, start, "\u0001GUI_CLEAR\u0001", 0, length) == 0)
                        ClearOutput();
                    else
                    {
                        PrintLineSlice(output, start, length, true);
                        deferredScrollUpdate = true;
                    }
                }

                // The legacy Replace("\r\n", "\n").Replace("\r", "\n")
                // treats CRLF as one separator, not two empty lines.
                if (end == output.Length)
                    break;
                start = end + 1;
                if (output[end] == '\r' && start < output.Length &&
                    output[start] == '\n')
                    start++;
            }

            // Publish the final scrollbar range once per command, including
            // when the user is reading earlier output. A clear marker may
            // switch back to auto-follow mid-command; FinishOutputUpdate
            // observes the final userScrolled state.
            if (deferredScrollUpdate)
                FinishOutputUpdate();
        }

        public void ClearOutput()
        {
            OutputLines.Clear();
            ClearRenderClips();
            userScrolled = false;
            scrollOffset = 0;
            UpdateScrollBar();
        }

        internal void ShowMonitor(IReadOnlyList<string> lines, int firstLine)
        {
            // Htop replaces the entire visible snapshot on each refresh.
            // Avoid ClearOutput plus a redundant scrollbar update per row.
            OutputLines.Clear();
            ClearRenderClips();
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
                cachedOutputCount == count && cachedScrollOffset == scrollOffset &&
                ReferenceEquals(cachedScrollFont, Font))
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
            // Font is replaceable; a new glyph height changes visible rows
            // even when the window bounds and output count stay unchanged.
            cachedScrollFont = Font;
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
                {
                    Text = Text.Substring(0, Text.Length - 1);
                    inputRenderClip = default;
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                int maxChars = GetMaxChars();
                int promptChars = Prompt == null ? 0 : Prompt.Length;
                if (promptChars + Text.Length < maxChars)
                {
                    Text += key.KeyChar;
                    inputRenderClip = default;
                }
            }
        }

        public void ClearInput()
        {
            Text = "";
            inputRenderClip = default;
        }

        public void SetInput(string text)
        {
            string value = text ?? string.Empty;
            int maxChars = GetMaxChars();
            int promptChars = Prompt == null ? 0 : Prompt.Length;
            int capacity = Math.Max(0, maxChars - promptChars);
            Text = value.Length <= capacity ? value : value.Substring(0, capacity);
            inputRenderClip = default;
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
                // The usual unresized history already fits: retain its old
                // direct draw path with no cache lookup or helper call.
                // Once a clip cache exists, short rows still clear stale refs.
                string visibleLine = line;
                if (line.Length > maxChars || renderClips != null)
                    visibleLine = ClipLineForRender(line, i - startLine, maxChars);
                DrawTerminalString(canvas, visibleLine,
                    X + ContentPadding, currentY, TextColor);
                currentY += lineHeight;
            }
            FinishRenderClips(endLine - startLine);

            if (startLine >= maxScroll)
            {
                string prompt = Prompt ?? "> ";
                int charWidth = GetCharWidth();
                int promptChars = Math.Min(prompt.Length, maxChars);
                int textChars = Math.Min(Text.Length, Math.Max(0, maxChars - promptChars));

                DrawTerminalString(canvas, ClipPromptForRender(prompt, promptChars),
                    X + ContentPadding, currentY, PromptColor);

                if (textChars > 0)
                    DrawTerminalString(canvas, ClipInputForRender(Text, textChars),
                        X + ContentPadding + promptChars * charWidth, currentY, TextColor);
                else
                    inputRenderClip = default;

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
