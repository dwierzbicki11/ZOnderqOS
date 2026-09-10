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
        private int hoveredControl;

        private const int TitleBarHeight = 38;
        private const int ButtonSize = 30;
        private const int ButtonTop = 4;
        private const int RightPadding = 4;

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
            hoveredControl = 0;
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
                restoreX = X;
                restoreY = Y;
                restoreWidth = Width;
                restoreHeight = Height;
                MoveTo(0, 0);
                Width = desktopWidth;
                Height = desktopHeight;
                IsMaximized = true;
            }
            else
            {
                MoveTo(restoreX, restoreY);
                Width = restoreWidth;
                Height = restoreHeight;
                IsMaximized = false;
            }
        }

        public bool ContainsPoint(int mouseX, int mouseY)
        {
            return Visible && !IsMinimized && mouseX >= X && mouseX < X + Width &&
                mouseY >= Y && mouseY < Y + Height;
        }

        public void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible || IsMinimized)
                return;

            int closeX = X + Width - RightPadding - ButtonSize;
            int maximizeX = closeX - ButtonSize;
            int minimizeX = maximizeX - ButtonSize;
            bool inControlsY = mouseY >= Y + ButtonTop && mouseY < Y + ButtonTop + ButtonSize;

            hoveredControl = 0;
            if (inControlsY)
            {
                if (mouseX >= closeX && mouseX < closeX + ButtonSize) hoveredControl = 3;
                else if (mouseX >= maximizeX && mouseX < maximizeX + ButtonSize) hoveredControl = 2;
                else if (mouseX >= minimizeX && mouseX < minimizeX + ButtonSize) hoveredControl = 1;
            }

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
                    MoveTo(System.Math.Max(0, System.Math.Min(newX, maxX)),
                        System.Math.Max(0, System.Math.Min(newY, maxY)));
                }
                return;
            }

            if (!wasClicked && inTitle)
            {
                if (hoveredControl == 1)
                {
                    Minimize();
                    return;
                }
                if (hoveredControl == 2)
                {
                    ToggleMaximize();
                    return;
                }
                if (hoveredControl == 3)
                {
                    CloseAction?.Invoke();
                    return;
                }

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
            if (!Visible || IsMinimized)
                return;

            Color surface = Color.FromArgb(29, 34, 40);
            Color border = IsActive ? Color.FromArgb(72, 145, 205) : Color.FromArgb(58, 66, 75);
            Color titleBar = IsActive ? Color.FromArgb(27, 34, 42) : Color.FromArgb(31, 36, 42);
            Color titleText = IsActive ? Color.FromArgb(242, 246, 250) : Color.FromArgb(196, 203, 210);

            if (!IsMaximized)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(16, 19, 23), X + 5, Y + 6, Width, Height);
                canvas.DrawFilledRectangle(Color.FromArgb(20, 23, 28), X + 3, Y + 4, Width, Height);
            }

            canvas.DrawFilledRectangle(surface, X, Y, Width, Height);
            canvas.DrawRectangle(border, X, Y, Width, Height);
            canvas.DrawFilledRectangle(titleBar, X + 1, Y + 1, Width - 2, TitleBarHeight - 1);

            if (IsActive)
                canvas.DrawFilledRectangle(Color.FromArgb(65, 142, 205), X + 1, Y + 1, Width - 2, 2);

            canvas.DrawLine(Color.FromArgb(48, 57, 67), X + 1, Y + TitleBarHeight,
                X + Width - 2, Y + TitleBarHeight);
            canvas.DrawString(Title, PCScreenFont.DefaultFont, titleText, X + 12, Y + 4);

            int closeX = X + Width - RightPadding - ButtonSize;
            int maximizeX = closeX - ButtonSize;
            int minimizeX = maximizeX - ButtonSize;
            int buttonY = Y + ButtonTop;

            DrawCaptionButton(canvas, minimizeX, buttonY, 1);
            canvas.DrawLine(Color.FromArgb(220, 226, 232), minimizeX + 9, buttonY + 18,
                minimizeX + 21, buttonY + 18);

            DrawCaptionButton(canvas, maximizeX, buttonY, 2);
            IconManager.DrawScaled(canvas, IsMaximized ? IconType.Restore : IconType.Maximize,
                maximizeX + 7, buttonY + 6, 16, 16);

            DrawCaptionButton(canvas, closeX, buttonY, 3);
            IconManager.DrawScaled(canvas, IconType.Close, closeX + 7, buttonY + 6, 16, 16);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);
        }

        private void DrawCaptionButton(Canvas canvas, int x, int y, int control)
        {
            if (hoveredControl != control)
                return;

            Color hover = control == 3
                ? Color.FromArgb(174, 50, 58)
                : Color.FromArgb(49, 62, 74);
            Color edge = control == 3
                ? Color.FromArgb(215, 78, 84)
                : Color.FromArgb(73, 91, 108);

            canvas.DrawFilledRectangle(hover, x, y, ButtonSize, ButtonSize);
            canvas.DrawRectangle(edge, x, y, ButtonSize, ButtonSize);
        }
    }
}
