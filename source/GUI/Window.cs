using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public class Window : Widget
    {
        public string Title { get; set; }
        public List<Widget> Children { get; } = new List<Widget>();

        public Window(int x, int y, int width, int height, string title) : base(x, y, width, height)
        {
            Title = title;
        }

        public void AddChild(Widget widget)
        {
            // Przesunięcie współrzędnych kontrolki względnie do okna
            widget.X += X;
            widget.Y += Y;
            Children.Add(widget);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            // 1. Tło okna
            canvas.DrawFilledRectangle(Color.WhiteSmoke, X, Y, Width, Height);
            // 2. Obramowanie okna
            canvas.DrawRectangle(Color.DimGray, X, Y, Width, Height);
            // 3. Pasek tytułowy
            canvas.DrawFilledRectangle(Color.MidnightBlue, X, Y, Width, 30);
            canvas.DrawString(Title, PCScreenFont.DefaultFont, Color.White, X + 10, Y + 6);

            // 4. Renderowanie dzieci (kontrolek wewnątrz okna)
            foreach (var child in Children)
            {
                child.Render(canvas);
            }
        }
    }
}