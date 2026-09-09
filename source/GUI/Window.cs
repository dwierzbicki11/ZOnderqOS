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
        public bool IsMaximized { get; private set; }
        public System.Action CloseAction { get; set; }

        private int restoreX;
        private int restoreY;
        private int restoreWidth;
        private int restoreHeight;

        private static int desktopWidth;
        private static int desktopHeight;

        public static void ConfigureDesktop(int width, int height)
        {
            desktopWidth = width;
            desktopHeight = height;
        }

        public Window(int x, int y, int width, int height, string title) : base(x, y, width, height)
        {
            Title = title;
        }

        public void AddChild(Widget widget)
        {
            widget.X += X;
            widget.Y += Y;
            Children.Add(widget);
        }

        public void ToggleMaximize()
        {
            if (!IsMaximized)
            {
                restoreX = X;
                restoreY = Y;
                restoreWidth = Width;
                restoreHeight = Height;

                X = 0;
                Y = 0;
                Width = desktopWidth;
                Height = desktopHeight;
                IsMaximized = true;
            }
            else
            {
                X = restoreX;
                Y = restoreY;
                Width = restoreWidth;
                Height = restoreHeight;
                IsMaximized = false;
            }
        }

        public void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible || !isClicked || wasClicked)
                return;

            if (mouseY < Y || mouseY > Y + 30 || mouseX < X || mouseX > X + Width)
                return;

            // [□] maximize / restore
            if (mouseX >= X + Width - 55 && mouseX < X + Width - 30)
            {
                ToggleMaximize();
                return;
            }

            // [X] close
            if (mouseX >= X + Width - 30 && mouseX <= X + Width)
            {
                CloseAction?.Invoke();
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(Color.WhiteSmoke, X, Y, Width, Height);
            canvas.DrawRectangle(Color.DimGray, X, Y, Width, Height);
            canvas.DrawFilledRectangle(Color.MidnightBlue, X, Y, Width, 30);
            canvas.DrawString(Title, PCScreenFont.DefaultFont, Color.White, X + 10, Y + 6);

            // Maximize button
            canvas.DrawFilledRectangle(Color.SteelBlue, X + Width - 55, Y + 4, 25, 22);
            canvas.DrawRectangle(Color.WhiteSmoke, X + Width - 49, Y + 9, 13, 12);

            // Close button
            canvas.DrawFilledRectangle(Color.Firebrick, X + Width - 30, Y + 4, 25, 22);
            canvas.DrawLine(Color.White, X + Width - 24, Y + 9, X + Width - 11, Y + 21);
            canvas.DrawLine(Color.White, X + Width - 11, Y + 9, X + Width - 24, Y + 21);

            foreach (var child in Children)
            {
                child.Render(canvas);
            }
        }
    }
}