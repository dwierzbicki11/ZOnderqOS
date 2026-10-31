using System;
using System.Drawing;

namespace Cosmos.Kernel.System.Graphics
{
    // Test-only stand-in. Never compiled into the production kernel.
    public sealed class Canvas
    {
        private readonly int[] pixels;
        public int Width { get; }
        public int Height { get; }
        public int DrawCalls { get; private set; }
        public int[] Pixels => pixels;

        public Canvas(int width, int height)
        {
            Width = width;
            Height = height;
            pixels = new int[checked(width * height)];
        }

        public void DrawFilledRectangle(Color color, int x, int y, int width, int height)
        {
            DrawCalls++;
            for (int row = Math.Max(0, y); row < Math.Min(Height, y + height); row++)
                for (int col = Math.Max(0, x); col < Math.Min(Width, x + width); col++)
                    pixels[row * Width + col] = color.ToArgb();
        }
    }
}
