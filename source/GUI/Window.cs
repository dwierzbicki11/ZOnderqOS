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
        private bool isActive;
        private float focusProgress;
        private int openingFrames = 8;
        public bool IsActive
        {
            get => isActive;
            set
            {
                if (isActive == value)
                    return;
                isActive = value;
                UiAnimator.RequestFrames();
            }
        }
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
            // Application defaults can be larger than the actual framebuffer
            // (Settings 1080x720, Calculator 500x690). Fit the initial window
            // before creating child controls so click coordinates stay aligned.
            // Never invent a display resolution: this is only a GUI layout cap.
            if (desktopWidth > 0 && desktopHeight > 0)
            {
                int minW = System.Math.Min(MinWidth, desktopWidth);
                int minH = System.Math.Min(MinHeight, desktopHeight);
                Width = System.Math.Max(minW, System.Math.Min(Width, desktopWidth));
                Height = System.Math.Max(minH, System.Math.Min(Height, desktopHeight));
                X = System.Math.Max(0, System.Math.Min(X, desktopWidth - Width));
                Y = System.Math.Max(0, System.Math.Min(Y, desktopHeight - Height));
            }
            UiAnimator.RequestFrames();
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
            openingFrames = 8;
            UiAnimator.RequestFrames();
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

            int previousHoveredControl = hoveredControl;
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

            if (previousHoveredControl != hoveredControl)
                UiAnimator.RequestFrames();
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

            focusProgress = UiAnimator.Approach(focusProgress, IsActive ? 1f : 0f, 0.22f);
            Color border = UiAnimator.Blend(UiPalette.Border, UiPalette.AccentBorder, focusProgress);
            Color titleBar = UiAnimator.Blend(UiPalette.SurfaceRaised, UiPalette.AccentSoft, focusProgress * 0.45f);
            Color titleText = UiAnimator.Blend(UiPalette.TextMuted, UiPalette.Text, focusProgress);

            if (!IsMaximized)
                UiPainter.Shadow(canvas, X, Y, Width, Height, UiMetrics.WindowRadius);

            UiPainter.RoundedPanel(canvas, X, Y, Width, Height,
                IsMaximized ? 0 : UiMetrics.WindowRadius, UiPalette.Surface, border);
            if (IsMaximized)
                canvas.DrawFilledRectangle(titleBar, X + 1, Y + 1, Width - 2, TitleBarHeight - 1);
            else
                UiPainter.RoundedTop(canvas, X + 1, Y + 1, Width - 2,
                    TitleBarHeight - 1, UiMetrics.WindowRadius - 1, titleBar);

            if (openingFrames > 0)
                openingFrames--;
            if (focusProgress > 0f)
            {
                int width = Width - 24;
                int reveal = openingFrames > 0 ? width * (8 - openingFrames) / 8 : width;
                canvas.DrawFilledRectangle(UiPalette.Accent, X + 12, Y + 2,
                    System.Math.Max(1, reveal), 2);
            }

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
#if ZONDERQ_READONLY_HARDWARE_PROBE
            // The Gen3 PNG icon cache uses File.WriteAllBytes. Never invoke it
            // from a diskless read-only desktop on bare-metal 3.0.94.
            canvas.DrawRectangle(Color.White, maximizeX + 8, buttonY + 7, 14, 13);
#else
            IconManager.DrawScaled(canvas, IsMaximized ? IconType.Restore : IconType.Maximize,
                maximizeX + 7, buttonY + 6, 16, 16);
#endif

            DrawCaptionButton(canvas, closeX, buttonY, 3);
#if ZONDERQ_READONLY_HARDWARE_PROBE
            canvas.DrawLine(Color.White, closeX + 9, buttonY + 9,
                closeX + 21, buttonY + 21);
            canvas.DrawLine(Color.White, closeX + 21, buttonY + 9,
                closeX + 9, buttonY + 21);
#else
            IconManager.DrawScaled(canvas, IconType.Close, closeX + 7, buttonY + 6, 16, 16);
#endif

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

            UiPainter.RoundedPanel(canvas, x, y, ButtonSize, ButtonSize,
                UiMetrics.TileRadius, hover, edge);
        }
    }
}
