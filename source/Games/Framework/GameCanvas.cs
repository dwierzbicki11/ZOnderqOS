using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

namespace ZonderqOS.Games.Framework
{
    /// <summary>
    /// Resolution-independent drawing surface. Logical coordinates are scaled
    /// uniformly with letterboxing; filled rectangles are clipped to the game area.
    /// No per-frame objects are allocated by the framework renderer.
    /// </summary>
    public sealed class GameCanvas
    {
        private Canvas canvas;
        private readonly int logicalWidth;
        private readonly int logicalHeight;
        private int originX;
        private int originY;
        private int viewportWidth;
        private int viewportHeight;
        private float scale;

        public int Width { get { return logicalWidth; } }
        public int Height { get { return logicalHeight; } }

        internal GameCanvas(int logicalWidth, int logicalHeight)
        {
            this.logicalWidth = logicalWidth;
            this.logicalHeight = logicalHeight;
        }

        internal void BeginFrame(Canvas target, int x, int y, int width, int height)
        {
            canvas = target;
            scale = Math.Min(width / (float)logicalWidth, height / (float)logicalHeight);
            if (scale <= 0)
                scale = 0.001f;
            viewportWidth = Math.Max(1, (int)(logicalWidth * scale));
            viewportHeight = Math.Max(1, (int)(logicalHeight * scale));
            originX = x + (width - viewportWidth) / 2;
            originY = y + (height - viewportHeight) / 2;
        }

        public void FillRect(Color color, int x, int y, int width, int height)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;

            // Clip in logical coordinates before converting to screen pixels.
            long left = Math.Max(0L, (long)x);
            long top = Math.Max(0L, (long)y);
            long right = Math.Min((long)logicalWidth, (long)x + width);
            long bottom = Math.Min((long)logicalHeight, (long)y + height);
            if (right <= left || bottom <= top)
                return;

            int x0 = originX + (int)(left * scale);
            int y0 = originY + (int)(top * scale);
            int x1 = originX + (int)(right * scale);
            int y1 = originY + (int)(bottom * scale);
            // Clamp rounding to the physical viewport as well.
            x1 = Math.Min(originX + viewportWidth, Math.Max(x1, x0 + 1));
            y1 = Math.Min(originY + viewportHeight, Math.Max(y1, y0 + 1));
            canvas.DrawFilledRectangle(color, x0, y0, x1 - x0, y1 - y0);
        }

        public void Clear(Color color)
        {
            FillRect(color, 0, 0, logicalWidth, logicalHeight);
        }

        // The small GUI bitmap font stays readable even when the game scales down.
        public void DrawText(string text, int x, int y, Color color)
        {
            if (canvas == null || string.IsNullOrEmpty(text))
                return;
            if (x < 0 || y < 0 || x >= logicalWidth || y >= logicalHeight)
                return;

            int pixelX = originX + (int)(x * scale);
            int pixelY = originY + (int)(y * scale);
            if (pixelY + 8 >= originY + viewportHeight)
                return;
            int available = originX + viewportWidth - pixelX;
            if (available <= 0)
                return;
            SmallTextRenderer.DrawClipped(canvas, text, pixelX, pixelY, available, color);
        }

        internal bool TryToLogical(int screenX, int screenY, out int x, out int y)
        {
            x = 0;
            y = 0;
            if (canvas == null || scale <= 0 ||
                screenX < originX || screenY < originY ||
                screenX >= originX + viewportWidth || screenY >= originY + viewportHeight)
                return false;

            x = Math.Min(logicalWidth - 1, (int)((screenX - originX) / scale));
            y = Math.Min(logicalHeight - 1, (int)((screenY - originY) / scale));
            return true;
        }
    }
}
