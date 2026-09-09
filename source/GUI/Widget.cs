using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public abstract class Widget
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Visible { get; set; } = true;

        protected Widget(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public abstract void Render(Canvas canvas);
    }
}