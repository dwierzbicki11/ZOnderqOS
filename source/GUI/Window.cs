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

        private const int TitleBarHeight = 32;
        private const int ButtonSize = 24;
        private const int ButtonGap = 4;

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

            if (mouseY < Y || mouseY > Y + TitleBarHeight || mouseX < X || mouseX > X + Width)
                return;

            int closeX = X + Width - ButtonSize - 5;
            int maximizeX = closeX - ButtonGap - ButtonSize;

            if (mouseX >= maximizeX && mouseX < maximizeX + ButtonSize && mouseY >= Y + 4 && mouseY < Y + 4 + ButtonSize)
            {
                ToggleMaximize();
                return;
            }

            if (mouseX >= closeX && mouseX <= closeX + ButtonSize && mouseY >= Y + 4 && mouseY < Y + 4 + ButtonSize)
            {
                CloseAction?.Invoke();
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            // Subtle shadow and clean window body.
            canvas.DrawFilledRectangle(Color.FromArgb(35, 35, 35), X + 3, Y + 3, Width, Height);
            canvas.DrawFilledRectangle(Color.WhiteSmoke, X, Y, Width, Height);
            canvas.DrawRectangle(Color.FromArgb(90, 90, 90), X, Y, Width, Height);

            // Modern two-tone title bar.
            canvas.DrawFilledRectangle(Color.FromArgb(24, 48, 78), X + 1, Y + 1, Width - 2, TitleBarHeight);
            canvas.DrawLine(Color.FromArgb(70, 105, 145), X + 1, Y + TitleBarHeight, X + Width - 2, Y + TitleBarHeight);

            canvas.DrawString(Title, PCScreenFont.DefaultFont, Color.White, X + 12, Y + 5);

            int closeX = X + Width - ButtonSize - 5;
            int maximizeX = closeX - ButtonGap - ButtonSize;

            // Maximize / restore button.
            canvas.DrawFilledRectangle(IsMaximized ? Color.FromArgb(55, 90, 125) : Color.FromArgb(45, 75, 105), maximizeX, Y + 4, ButtonSize, ButtonSize);
            canvas.DrawRectangle(Color.FromArgb(120, 150, 180), maximizeX, Y + 4, ButtonSize, ButtonSize);
            if (IsMaximized)
            {
                canvas.DrawRectangle(Color.WhiteSmoke, maximizeX + 6, Y + 10, 12, 10);
                canvas.DrawLine(Color.WhiteSmoke, maximizeX + 8, Y + 8, maximizeX + 19, Y + 8);
            }
            else
            {
                canvas.DrawRectangle(Color.WhiteSmoke, maximizeX + 6, Y + 9, 12, 12);
            }

            // Close button: the only close control in the window.
            canvas.DrawFilledRectangle(Color.Firebrick, closeX, Y + 4, ButtonSize, ButtonSize);
            canvas.DrawRectangle(Color.FromArgb(230, 120, 120), closeX, Y + 4, ButtonSize, ButtonSize);
            canvas.DrawLine(Color.White, closeX + 6, Y + 9, closeX + 18, Y + 21);
            canvas.DrawLine(Color.White, closeX + 18, Y + 9, closeX + 6, Y + 21);

            foreach (var child in Children)
            {
                child.Render(canvas);
            }
        }
    }
}