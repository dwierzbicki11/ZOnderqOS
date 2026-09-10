using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class Window : Widget
    {
        public string Title { get; set; }
        public List<Widget> Children { get; } = new List<Widget>();
        public bool IsMaximized { get; private set; }
        public bool IsMinimized { get; private set; }
        public bool IsActive { get; set; }
        public System.Action CloseAction { get; set; }

        private int restoreX, restoreY, restoreWidth, restoreHeight;
        private static int desktopWidth, desktopHeight;
        private bool dragging;
        private int dragOffsetX, dragOffsetY;

        private const int TitleBarHeight = 36;
        private const int ButtonSize = 26;
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

        private void MoveTo(int newX, int newY)
        {
            int dx = newX - X;
            int dy = newY - Y;
            if (dx == 0 && dy == 0) return;
            X = newX;
            Y = newY;
            for (int i = 0; i < Children.Count; i++)
            {
                Children[i].X += dx;
                Children[i].Y += dy;
            }
        }

        public void Minimize()
        {
            dragging = false;
            IsMinimized = true;
            Visible = false;
            IsActive = false;
        }

        public void RestoreFromMinimized()
        {
            IsMinimized = false;
            Visible = true;
        }

        public void ToggleMinimize()
        {
            if (IsMinimized) RestoreFromMinimized();
            else Minimize();
        }

        public void ToggleMaximize()
        {
            if (IsMinimized)
            {
                RestoreFromMinimized();
                return;
            }
            dragging = false;
            if (!IsMaximized)
            {
                restoreX = X; restoreY = Y; restoreWidth = Width; restoreHeight = Height;
                MoveTo(0, 0);
                Width = desktopWidth; Height = desktopHeight; IsMaximized = true;
            }
            else
            {
                MoveTo(restoreX, restoreY);
                Width = restoreWidth; Height = restoreHeight; IsMaximized = false;
            }
        }

        public bool ContainsPoint(int mouseX, int mouseY)
        {
            return Visible && !IsMinimized && mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;
        }

        public void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible || IsMinimized) return;

            int closeX = X + Width - ButtonSize - 6;
            int maximizeX = closeX - ButtonGap - ButtonSize;
            int minimizeX = maximizeX - ButtonGap - ButtonSize;
            bool inTitle = mouseY >= Y && mouseY < Y + TitleBarHeight && mouseX >= X && mouseX < X + Width;

            if (!isClicked)
            {
                dragging = false;
                return;
            }
            if (dragging)
            {
                if (!IsMaximized)
                {
                    int newX = mouseX - dragOffsetX;
                    int newY = mouseY - dragOffsetY;
                    int maxX = desktopWidth > Width ? desktopWidth - Width : 0;
                    int maxY = desktopHeight > Height ? desktopHeight - Height : 0;
                    MoveTo(System.Math.Max(0, System.Math.Min(newX, maxX)), System.Math.Max(0, System.Math.Min(newY, maxY)));
                }
                return;
            }
            if (!wasClicked && inTitle)
            {
                if (mouseX >= minimizeX && mouseX < minimizeX + ButtonSize && mouseY >= Y + 5 && mouseY < Y + 5 + ButtonSize)
                { Minimize(); return; }
                if (mouseX >= maximizeX && mouseX < maximizeX + ButtonSize && mouseY >= Y + 5 && mouseY < Y + 5 + ButtonSize)
                { ToggleMaximize(); return; }
                if (mouseX >= closeX && mouseX < closeX + ButtonSize && mouseY >= Y + 5 && mouseY < Y + 5 + ButtonSize)
                { CloseAction?.Invoke(); return; }
                if (!IsMaximized && mouseX < minimizeX)
                {
                    dragging = true;
                    dragOffsetX = mouseX - X;
                    dragOffsetY = mouseY - Y;
                }
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || IsMinimized) return;

            Color shadow = Color.FromArgb(24, 24, 28);
            Color surface = Color.FromArgb(31, 36, 42);
            Color border = IsActive ? Color.FromArgb(65, 140, 200) : Color.FromArgb(70, 76, 84);
            Color title = IsActive ? Color.FromArgb(31, 65, 94) : Color.FromArgb(38, 43, 49);

            canvas.DrawFilledRectangle(shadow, X + 5, Y + 5, Width, Height);
            canvas.DrawFilledRectangle(surface, X, Y, Width, Height);
            canvas.DrawRectangle(border, X, Y, Width, Height);
            canvas.DrawFilledRectangle(title, X + 1, Y + 1, Width - 2, TitleBarHeight);
            canvas.DrawLine(IsActive ? Color.FromArgb(55, 125, 185) : Color.FromArgb(65, 70, 78), X + 1, Y + TitleBarHeight, X + Width - 2, Y + TitleBarHeight);
            canvas.DrawString(Title, PCScreenFont.DefaultFont, Color.FromArgb(235, 239, 243), X + 12, Y + 7);

            int closeX = X + Width - ButtonSize - 6;
            int maximizeX = closeX - ButtonGap - ButtonSize;
            int minimizeX = maximizeX - ButtonGap - ButtonSize;

            canvas.DrawFilledRectangle(Color.FromArgb(45, 55, 65), minimizeX, Y + 5, ButtonSize, ButtonSize);
            canvas.DrawRectangle(Color.FromArgb(80, 95, 110), minimizeX, Y + 5, ButtonSize, ButtonSize);
            canvas.DrawLine(Color.LightGray, minimizeX + 7, Y + 18, minimizeX + 19, Y + 18);

            canvas.DrawFilledRectangle(Color.FromArgb(45, 65, 84), maximizeX, Y + 5, ButtonSize, ButtonSize);
            canvas.DrawRectangle(Color.FromArgb(80, 115, 145), maximizeX, Y + 5, ButtonSize, ButtonSize);
            IconManager.Draw(canvas, IsMaximized ? IconType.Restore : IconType.Maximize, maximizeX + 4, Y + 9, Color.WhiteSmoke);

            canvas.DrawFilledRectangle(Color.FromArgb(150, 48, 55), closeX, Y + 5, ButtonSize, ButtonSize);
            canvas.DrawRectangle(Color.FromArgb(205, 90, 95), closeX, Y + 5, ButtonSize, ButtonSize);
            IconManager.Draw(canvas, IconType.Close, closeX + 4, Y + 9, Color.White);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);
        }
    }
}