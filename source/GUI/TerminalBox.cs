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

        private float fontScale = 0.875f;
        public float FontScale
        {
            get { return fontScale; }
            set { fontScale = value <= 0f ? 1f : value; }
        }

        public List<string> OutputLines { get; } = new List<string>();

        public Color BackgroundColor { get; set; } = Color.FromArgb(15, 15, 15);
        public Color TextColor { get; set; } = Color.GreenYellow;

        // Keep only one reusable source canvas. The previous implementation created
        // a Canvas + Bitmap for every new output string and kept every Bitmap forever.
        // In a long-running GUI terminal this caused unbounded native framebuffer/GDI
        // memory growth. Rendering is now bounded to the current frame.
        private Canvas textCanvas;
        private int textCanvasWidth;
        private int textCanvasHeight;
        private string cachedText;
        private float cachedScale;
        private int cachedFontWidth;
        private int cachedFontHeight;
        private Color cachedTextColor;

        public TerminalBox(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Font = PCScreenFont.DefaultFont;
            cachedScale = -1f;
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
                OutputLines.Add("");
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

                OutputLines.Add(line.Substring(position, take).TrimEnd());
                position += take;
                while (position < line.Length && line[position] == ' ')
                    position++;
            }
        }

        public void ClearOutput()
        {
            OutputLines.Clear();
            cachedText = null;
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

        private void EnsureTextCanvas(int width, int height)
        {
            width = Math.Max(1, width);
            height = Math.Max(1, height);

            if (textCanvas != null && textCanvasWidth >= width && textCanvasHeight >= height)
                return;

            textCanvasWidth = Math.Max(width, textCanvasWidth);
            textCanvasHeight = Math.Max(height, textCanvasHeight);
            textCanvas = new Canvas(textCanvasWidth, textCanvasHeight);
        }

        private void DrawScaledString(Canvas canvas, string text, int x, int y)
        {
            if (string.IsNullOrEmpty(text) || Font == null)
                return;

            int currentFontWidth = Font.Width;
            int currentFontHeight = Font.Height;
            if (cachedText == text &&
                Math.Abs(cachedScale - FontScale) < 0.001f &&
                cachedFontWidth == currentFontWidth &&
                cachedFontHeight == currentFontHeight &&
                cachedTextColor == TextColor)
            {
                // The source canvas still contains the exact same text. Reuse it.
            }
            else
            {
                int sourceWidth = Math.Max(1, text.Length * currentFontWidth + 2);
                int sourceHeight = Math.Max(1, currentFontHeight + 2);
                EnsureTextCanvas(sourceWidth, sourceHeight);
                textCanvas.Clear(Color.Transparent);
                textCanvas.DrawString(text, Font, TextColor, 0, 0);
                cachedText = text;
                cachedScale = FontScale;
                cachedFontWidth = currentFontWidth;
                cachedFontHeight = currentFontHeight;
                cachedTextColor = TextColor;
                textCanvasWidth = Math.Max(textCanvasWidth, sourceWidth);
                textCanvasHeight = Math.Max(textCanvasHeight, sourceHeight);
            }

            int sourceWidthForText = Math.Max(1, text.Length * currentFontWidth + 2);
            int sourceHeightForText = Math.Max(1, currentFontHeight + 2);
            int targetWidth = Math.Max(1, (int)(sourceWidthForText * FontScale));
            int targetHeight = Math.Max(1, (int)(sourceHeightForText * FontScale));
            Bitmap image = textCanvas.GetImage(0, 0, sourceWidthForText, sourceHeightForText);
            canvas.DrawImage(image, x, y, targetWidth, targetHeight);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawRectangle(IsFocused ? Color.DeepSkyBlue : Color.DimGray, X, Y, Width, Height);

            int lineHeight = GetScaledLineHeight();
            int maxVisibleLines = Math.Max(1, (Height - 16) / lineHeight);
            int startLine = Math.Max(0, OutputLines.Count - (maxVisibleLines - 1));
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
            return mouseX >= X && mouseX <= X + Width &&
                   mouseY >= Y && mouseY <= Y + Height;
        }
    }
}