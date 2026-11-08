using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public sealed class DesktopShortcut
    {
        private const int WidthValue = 92;
        private const int HeightValue = 86;
        private const int IconSize = 46;

        public int X { get; }
        public int Y { get; }
        public int Width => WidthValue;
        public int Height => HeightValue;
        public string Label { get; }
        public IconType Icon { get; }
        public Action OpenAction { get; }
        public bool IsSelected { get; set; }
        public bool IsHovered { get; set; }

        public DesktopShortcut(int x, int y, string label, IconType icon, Action openAction)
        {
            X = x;
            Y = y;
            Label = label ?? string.Empty;
            Icon = icon;
            OpenAction = openAction;
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + WidthValue && mouseY >= Y && mouseY < Y + HeightValue;
        }

        public void Open()
        {
            OpenAction?.Invoke();
        }

        public void Render(Canvas canvas)
        {
            Color panel;
            Color border;

            if (IsSelected)
            {
                panel = Color.FromArgb(34, 65, 91);
                border = Color.FromArgb(78, 156, 218);
            }
            else if (IsHovered)
            {
                panel = Color.FromArgb(28, 42, 54);
                border = Color.FromArgb(72, 108, 136);
            }
            else
            {
                // A permanent dark plate keeps PNG icons and captions readable on
                // both bright and dark parts of the wallpaper.
                panel = Color.FromArgb(19, 26, 33);
                border = Color.FromArgb(48, 59, 70);
            }

            // Small offset shadow separates the shortcut from detailed wallpapers.
            canvas.DrawFilledRectangle(Color.FromArgb(10, 14, 18), X + 4, Y + 4, WidthValue - 4, HeightValue - 4);
            canvas.DrawFilledRectangle(panel, X + 2, Y + 2, WidthValue - 4, HeightValue - 4);
            canvas.DrawRectangle(border, X + 2, Y + 2, WidthValue - 4, HeightValue - 4);

            int iconX = X + (WidthValue - IconSize) / 2;
            int iconY = Y + 7;

            // Dedicated icon well gives transparent PNGs a stable background.
            canvas.DrawFilledRectangle(Color.FromArgb(24, 31, 39), iconX - 5, iconY - 3, IconSize + 10, IconSize + 8);
            canvas.DrawRectangle(IsSelected ? Color.FromArgb(62, 128, 180) : Color.FromArgb(43, 54, 65),
                iconX - 5, iconY - 3, IconSize + 10, IconSize + 8);
            IconManager.DrawScaled(canvas, Icon, iconX, iconY, IconSize, IconSize);

            // Caption strip stays readable regardless of wallpaper brightness.
            canvas.DrawFilledRectangle(Color.FromArgb(13, 18, 24), X + 6, Y + 59, WidthValue - 12, 17);
            DrawCenteredLabel(canvas, Label, X + 8, Y + 64, WidthValue - 16);
        }

        private static void DrawCenteredLabel(Canvas canvas, string text, int x, int y, int width)
        {
            if (string.IsNullOrEmpty(text))
                return;

            const int glyphStep = 6;
            int maxChars = Math.Max(1, width / glyphStep);
            int count = Math.Min(text.Length, maxChars);
            bool clipped = count < text.Length;
            if (clipped && count > 3)
                count -= 3;

            int totalChars = count + (clipped ? 3 : 0);
            int textWidth = totalChars * glyphStep - 1;
            int startX = x + Math.Max(0, (width - textWidth) / 2);

            DrawTinyRange(canvas, text, 0, count, startX + 1, y + 1, Color.FromArgb(8, 11, 15));
            if (clipped)
                DrawTiny(canvas, "...", startX + count * glyphStep + 1, y + 1, Color.FromArgb(8, 11, 15));

            DrawTinyRange(canvas, text, 0, count, startX, y, Color.FromArgb(238, 243, 248));
            if (clipped)
                DrawTiny(canvas, "...", startX + count * glyphStep, y, Color.FromArgb(238, 243, 248));
        }

        private static void DrawTinyRange(Canvas canvas, string text, int start, int count, int x, int y, Color color)
        {
            SmallTextRenderer.DrawRange(canvas, text, start, count, x, y, color);
        }

        private static void DrawTiny(Canvas canvas, string text, int x, int y, Color color)
        {
            SmallTextRenderer.Draw(canvas, text, x, y, color);
        }
    }
}
