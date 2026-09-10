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

        private bool resizing;
        private int resizeMode;
        private int hoveredResizeMode;
        private int resizeStartMouseX, resizeStartMouseY;
        private int resizeStartX, resizeStartY, resizeStartWidth, resizeStartHeight;
        private int interactionFrame;
        private int lastTitleClickFrame = -1000;

        private const int TitleBarHeight = 38;
        private const int ButtonSize = 30;
        private const int ButtonTop = 4;
        private const int RightPadding = 4;
        private const int ResizeBorder = 6;
        private const int MinWidth = 320;
        private const int MinHeight = 220;

        private const int ResizeLeft = 1;
        private const int ResizeRight = 2;
        private const int ResizeTop = 4;
        private const int ResizeBottom = 8;

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
            if (dx == 0 && dy == 0)
                return;

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
            resizing = false;
            hoveredControl = 0;
            hoveredResizeMode = 0;
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
            if (IsMinimized)
                RestoreFromMinimized();
            else
                Minimize();
        }

        public void ToggleMaximize()
        {
            if (IsMinimized)
            {
                RestoreFromMinimized();
                return;
            }

            dragging = false;
            resizing = false;
            hoveredResizeMode = 0;

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

            interactionFrame++;

            int closeX = X + Width - RightPadding - ButtonSize;
            int maximizeX = closeX - ButtonSize;
            int minimizeX = maximizeX - ButtonSize;
            bool inControlsY = mouseY >= Y + ButtonTop && mouseY < Y + ButtonTop + ButtonSize;

            hoveredControl = 0;
            if (inControlsY)
            {
                if (mouseX >= closeX && mouseX < closeX + ButtonSize)
                    hoveredControl = 3;
                else if (mouseX >= maximizeX && mouseX < maximizeX + ButtonSize)
                    hoveredControl = 2;
                else if (mouseX >= minimizeX && mouseX < minimizeX + ButtonSize)
                    hoveredControl = 1;
            }

            hoveredResizeMode = hoveredControl == 0 && !IsMaximized
                ? GetResizeMode(mouseX, mouseY)
                : 0;

            if (!isClicked)
            {
                dragging = false;
                resizing = false;
                resizeMode = 0;
                return;
            }

            if (resizing)
            {
                ApplyResize(mouseX, mouseY);
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

            if (wasClicked)
                return;

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

            if (hoveredResizeMode != 0)
            {
                BeginResize(mouseX, mouseY, hoveredResizeMode);
                return;
            }

            bool inTitle = mouseY >= Y && mouseY < Y + TitleBarHeight && mouseX >= X && mouseX < X + Width;
            if (!inTitle || mouseX >= minimizeX)
                return;

            bool doubleClick = interactionFrame - lastTitleClickFrame <= 28;
            lastTitleClickFrame = interactionFrame;
            if (doubleClick)
            {
                ToggleMaximize();
                lastTitleClickFrame = -1000;
                return;
            }

            if (!IsMaximized)
            {
                dragging = true;
                dragOffsetX = mouseX - X;
                dragOffsetY = mouseY - Y;
            }
        }

        private int GetResizeMode(int mouseX, int mouseY)
        {
            if (mouseX < X || mouseX >= X + Width || mouseY < Y || mouseY >= Y + Height)
                return 0;

            int mode = 0;
            if (mouseX < X + ResizeBorder)
                mode |= ResizeLeft;
            else if (mouseX >= X + Width - ResizeBorder)
                mode |= ResizeRight;

            if (mouseY < Y + ResizeBorder)
                mode |= ResizeTop;
            else if (mouseY >= Y + Height - ResizeBorder)
                mode |= ResizeBottom;

            return mode;
        }

        private void BeginResize(int mouseX, int mouseY, int mode)
        {
            resizing = true;
            resizeMode = mode;
            resizeStartMouseX = mouseX;
            resizeStartMouseY = mouseY;
            resizeStartX = X;
            resizeStartY = Y;
            resizeStartWidth = Width;
            resizeStartHeight = Height;
        }

        private void ApplyResize(int mouseX, int mouseY)
        {
            int dx = mouseX - resizeStartMouseX;
            int dy = mouseY - resizeStartMouseY;
            int newX = resizeStartX;
            int newY = resizeStartY;
            int newWidth = resizeStartWidth;
            int newHeight = resizeStartHeight;

            if ((resizeMode & ResizeLeft) != 0)
            {
                int right = resizeStartX + resizeStartWidth;
                newX = System.Math.Max(0, System.Math.Min(resizeStartX + dx, right - MinWidth));
                newWidth = right - newX;
            }
            else if ((resizeMode & ResizeRight) != 0)
            {
                int maxWidth = System.Math.Max(MinWidth, desktopWidth - resizeStartX);
                newWidth = System.Math.Max(MinWidth, System.Math.Min(resizeStartWidth + dx, maxWidth));
            }

            if ((resizeMode & ResizeTop) != 0)
            {
                int bottom = resizeStartY + resizeStartHeight;
                newY = System.Math.Max(0, System.Math.Min(resizeStartY + dy, bottom - MinHeight));
                newHeight = bottom - newY;
            }
            else if ((resizeMode & ResizeBottom) != 0)
            {
                int maxHeight = System.Math.Max(MinHeight, desktopHeight - resizeStartY);
                newHeight = System.Math.Max(MinHeight, System.Math.Min(resizeStartHeight + dy, maxHeight));
            }

            MoveTo(newX, newY);
            Width = newWidth;
            Height = newHeight;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || IsMinimized)
                return;

            Color surface = Color.FromArgb(29, 34, 40);
            Color border = IsActive ? SystemTheme.AccentBorder : Color.FromArgb(58, 66, 75);
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
                canvas.DrawFilledRectangle(SystemTheme.Accent, X + 1, Y + 1, Width - 2, 2);

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

            if (!IsMaximized && hoveredResizeMode != 0)
                DrawResizeHint(canvas, hoveredResizeMode);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);
        }

        private void DrawResizeHint(Canvas canvas, int mode)
        {
            Color hint = SystemTheme.Accent;
            if ((mode & ResizeLeft) != 0)
                canvas.DrawFilledRectangle(hint, X, Y + 8, 2, System.Math.Max(1, Height - 16));
            if ((mode & ResizeRight) != 0)
                canvas.DrawFilledRectangle(hint, X + Width - 2, Y + 8, 2, System.Math.Max(1, Height - 16));
            if ((mode & ResizeTop) != 0)
                canvas.DrawFilledRectangle(hint, X + 8, Y, System.Math.Max(1, Width - 16), 2);
            if ((mode & ResizeBottom) != 0)
                canvas.DrawFilledRectangle(hint, X + 8, Y + Height - 2, System.Math.Max(1, Width - 16), 2);
        }

        private void DrawCaptionButton(Canvas canvas, int x, int y, int control)
        {
            if (hoveredControl != control)
                return;

            Color hover = control == 3
                ? Color.FromArgb(174, 50, 58)
                : SystemTheme.AccentSoft;
            Color edge = control == 3
                ? Color.FromArgb(215, 78, 84)
                : SystemTheme.AccentBorder;

            canvas.DrawFilledRectangle(hover, x, y, ButtonSize, ButtonSize);
            canvas.DrawRectangle(edge, x, y, ButtonSize, ButtonSize);
        }
    }
}
