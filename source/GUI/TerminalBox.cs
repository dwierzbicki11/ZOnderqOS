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

        // Keep the normal terminal slightly smaller. Maximized mode can use
        // the native font size without recreating bitmaps every frame.
        private float fontScale = 0.875f;
        public float FontScale
        {
            get { return fontScale; }
            set
            {
                float newScale = value <= 0f ? 1f : value;
                if (Math.Abs(fontScale - newScale) > 0.001f)
                {
                    fontScale = newScale;
                    ClearRenderCache();
                }
            }
        }

        public List<string> OutputLines { get; } = new List<string>();

        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 15);
        public Color TextColor { get; set; } = Color.GreenYellow;

        // The old implementation created a Canvas + Bitmap for every line on
        // every frame. A maximized window has many more visible pixels, making
        // that allocation pattern extremely expensive and able to lock up the
        // GUI. Cache rendered strings and reuse them between frames.
        private readonly Dictionary<string, Bitmap> renderCache = new Dictionary<string, Bitmap>();
        private float cachedScale;
        private int cachedFontWidth;
        private int cachedFontHeight;
        private Color cachedTextColor;

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
            cachedScale = FontScale;
            cachedFontWidth = Font.Width;
            cachedFontHeight = Font.Height;
            cachedTextColor = TextColor;
        }

        public void PrintLine(string line)
        {
            if (line == null)
            {
                OutputLines.Add("");
                ClearRenderCache();
                return;
            }

            int maxChars = GetMaxChars();
            if (maxChars <= 0)
                return;

            if (line.Length == 0)
            {
                OutputLines.Add("");
                ClearRenderCache();
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

                string chunk = line.Substring(position, take).TrimEnd();
                OutputLines.Add(chunk);

                position += take;
                while (position < line.Length && line[position] == ' ')
                    position++;
            }

            ClearRenderCache();
        }

        private int GetScaledCharWidth()
        {
            if (Font == null || Font.Width <= 0)
                return 1;

            return Math.Max(1, (int)(Font.Width * FontScale));
        }

        private int GetScaledLineHeight()
        {
            if (Font == null || Font.Height <= 0)
                return 1;

            return Math.Max(1, (int)(Font.Height * FontScale));
        }

        private int GetMaxChars()
        {
            return Math.Max(1, (Width - 16) / GetScaledCharWidth());
        }

        public void HandleKey(KeyEvent key)
        {
            if (!IsFocused || !Visible) return;

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (Text.Length > 0)
                {
                    Text = Text.Substring(0, Text.Length - 1);
                    ClearRenderCache();
                }
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                int maxChars = GetMaxChars();
                int promptChars = Prompt == null ? 0 : Prompt.Length;

                if (promptChars + Text.Length < maxChars)
                {
                    Text += key.KeyChar;
                    ClearRenderCache();
                }
            }
        }

        public void ClearInput()
        {
            Text = "";
            ClearRenderCache();
        }

        private void ClearRenderCache()
        {
            // Keep the dictionary bounded. Cosmos graphics bitmaps can be
            // expensive, so stale cached strings are discarded before a new
            // font size/scale is rendered.
            renderCache.Clear();
            cachedScale = FontScale;
            cachedFontWidth = Font == null ? 0 : Font.Width;
            cachedFontHeight = Font == null ? 0 : Font.Height;
            cachedTextColor = TextColor;
        }

        private Bitmap GetRenderedString(string text)
        {
            if (string.IsNullOrEmpty(text) || Font == null)
                return null;

            int currentWidth = Font.Width;
            int currentHeight = Font.Height;

            if (Math.Abs(cachedScale - FontScale) > 0.001f ||
                cachedFontWidth != currentWidth ||
                cachedFontHeight != currentHeight ||
                cachedTextColor != TextColor)
            {
                ClearRenderCache();
            }

            Bitmap cached;
            if (renderCache.TryGetValue(text, out cached))
                return cached;

            int sourceWidth = Math.Max(1, text.Length * Font.Width + 2);
            int sourceHeight = Math.Max(1, Font.Height + 2);
            Canvas textCanvas = new Canvas(sourceWidth, sourceHeight);
            textCanvas.Clear(Color.Transparent);
            textCanvas.DrawString(text, Font, TextColor, 0, 0);

            Bitmap image = textCanvas.GetImage(0, 0, sourceWidth, sourceHeight);
            renderCache[text] = image;
            return image;
        }

        private void DrawScaledString(Canvas canvas, string text, int x, int y)
        {
            Bitmap image = GetRenderedString(text);
            if (image == null)
                return;

            int targetWidth = Math.Max(1, (int)(image.Width * FontScale));
            int targetHeight = Math.Max(1, (int)(image.Height * FontScale));
            canvas.DrawImage(image, x, y, targetWidth, targetHeight);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            int lineHeight = GetScaledLineHeight();
            int maxVisibleLines = Math.Max(1, (Height - 16) / lineHeight);

            int startLine = 0;
            if (OutputLines.Count > maxVisibleLines - 1)
                startLine = OutputLines.Count - (maxVisibleLines - 1);

            int currentY = Y + 8;
            int maxChars = GetMaxChars();

            for (int i = startLine; i < OutputLines.Count; i++)
            {
                string line = OutputLines[i] ?? "";
                if (line.Length > maxChars)
                    line = line.Substring(0, maxChars);

                DrawScaledString(canvas, line, X + 8, currentY);
                currentY += lineHeight;
            }

            string prompt = Prompt ?? "> ";
            string displayLine = prompt + Text;

            if (displayLine.Length > maxChars)
                displayLine = displayLine.Substring(0, maxChars);

            if (IsFocused && displayLine.Length < maxChars)
                displayLine += "_";

            DrawScaledString(canvas, displayLine, X + 8, currentY);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= (X + Width) &&
                   mouseY >= Y && mouseY <= (Y + Height);
        }
    }
}