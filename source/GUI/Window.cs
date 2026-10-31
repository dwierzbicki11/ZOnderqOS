using System.Collections.Generic;
using System.Diagnostics;
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
                if (!value)
                {
                    // A release can be delivered to a different window after
                    // focus switches. Do not resume an old drag/resize when
                    // this window is activated again.
                    lastTitleClickTimestamp = 0;
                    dragging = false;
                    resizing = false;
                    resizeMode = 0;
                    hoveredResizeMode = 0;
                    hoveredControl = 0;
                }
                UiAnimator.RequestFrames();
            }
        }
        public System.Action CloseAction { get; set; }

        private int restoreX, restoreY, restoreWidth, restoreHeight;
        private static int desktopWidth, desktopHeight;
        private int observedDesktopWidth, observedDesktopHeight;
        private bool dragging;
        private int dragOffsetX, dragOffsetY;
        private int hoveredControl;

        private bool resizing;
        private int resizeMode;
        private int hoveredResizeMode;
        private int resizeStartMouseX, resizeStartMouseY;
        private int resizeStartX, resizeStartY, resizeStartWidth, resizeStartHeight;
        private long lastTitleClickTimestamp;
        private int lastTitleClickX, lastTitleClickY;

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
            ConstrainToDesktop();
            observedDesktopWidth = desktopWidth;
            observedDesktopHeight = desktopHeight;
            UiAnimator.RequestFrames();
        }

        // Keep the whole caption and client area on the usable desktop.
        // Also used when restoring a window after the display mode changes.
        private void ConstrainToDesktop()
        {
            if (desktopWidth <= 0 || desktopHeight <= 0)
                return;

            // Leave breathing room on HD+; on 800x600 use all available pixels.
            int margin = desktopWidth >= 1000 && desktopHeight >= 700 ? 12 : 0;
            int availableWidth = System.Math.Max(1, desktopWidth - margin * 2);
            int availableHeight = System.Math.Max(1, desktopHeight - margin * 2);
            int minW = System.Math.Min(MinWidth, availableWidth);
            int minH = System.Math.Min(MinHeight, availableHeight);

            Width = System.Math.Max(minW, System.Math.Min(Width, availableWidth));
            Height = System.Math.Max(minH, System.Math.Min(Height, availableHeight));
            int x = System.Math.Max(margin, System.Math.Min(X, desktopWidth - Width - margin));
            int y = System.Math.Max(margin, System.Math.Min(Y, desktopHeight - Height - margin));
            MoveTo(x, y);
        }

        // Reflow existing windows only when the real desktop dimensions change.
        // Normal windows are clamped; maximized windows follow the new bounds.
        private void SyncDesktopBounds()
        {
            if (observedDesktopWidth == desktopWidth && observedDesktopHeight == desktopHeight)
                return;

            observedDesktopWidth = desktopWidth;
            observedDesktopHeight = desktopHeight;

            // A resize/drag started in the previous display mode holds old
            // screen coordinates. Cancel it before applying new bounds;
            // otherwise the next mouse-move can put the window off-screen.
            dragging = false;
            resizing = false;
            resizeMode = 0;
            hoveredResizeMode = 0;
            hoveredControl = 0;
            lastTitleClickTimestamp = 0;
            if (desktopWidth <= 0 || desktopHeight <= 0)
                return;

            if (IsMaximized)
            {
                MoveTo(0, 0);
                Width = desktopWidth;
                Height = desktopHeight;
            }
            else
            {
                ConstrainToDesktop();
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
            if (IsMaximized && desktopWidth > 0 && desktopHeight > 0)
            {
                MoveTo(0, 0);
                Width = desktopWidth;
                Height = desktopHeight;
            }
            else
            {
                ConstrainToDesktop();
            }
            observedDesktopWidth = desktopWidth;
            observedDesktopHeight = desktopHeight;
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

            SyncDesktopBounds();
            dragging = false;
            resizing = false;
            hoveredResizeMode = 0;

            if (!IsMaximized)
            {
                if (desktopWidth <= 0 || desktopHeight <= 0)
                    return;

                ConstrainToDesktop();
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
                ConstrainToDesktop();
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

            SyncDesktopBounds();

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
                    // Moving the window is a drag, not the first click
                    // of a future title-bar double-click.
                    if (newX != X || newY != Y)
                        lastTitleClickTimestamp = 0;
                    // Dragging must respect the same desktop inset as
                    // construction and restore, including the taskbar-free
                    // work area. Otherwise a window jumps into the margin
                    // and is moved back on the next desktop reflow.
                    int margin = desktopWidth >= 1000 && desktopHeight >= 700 ? 12 : 0;
                    int maxX = System.Math.Max(margin, desktopWidth - Width - margin);
                    int maxY = System.Math.Max(margin, desktopHeight - Height - margin);
                    MoveTo(System.Math.Max(margin, System.Math.Min(newX, maxX)),
                        System.Math.Max(margin, System.Math.Min(newY, maxY)));
                }
                return;
            }

            if (wasClicked)
                return;

            // Caption buttons and resize handles are separate gestures.
            // They must not prime a later title-bar double-click.
            if (hoveredControl != 0 || hoveredResizeMode != 0)
                lastTitleClickTimestamp = 0;

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
            {
                lastTitleClickTimestamp = 0;
                return;
            }

            // Frame counts are not elapsed time: a slow disk operation or
            // rendering stall must not turn two distant clicks into a double-click.
            long now = Stopwatch.GetTimestamp();
            long elapsed = now - lastTitleClickTimestamp;
            bool doubleClick = lastTitleClickTimestamp > 0 && elapsed >= 0 &&
                elapsed <= Stopwatch.Frequency / 2 &&
                System.Math.Abs(mouseX - lastTitleClickX) <= 12 &&
                System.Math.Abs(mouseY - lastTitleClickY) <= 12;
            lastTitleClickTimestamp = now;
            lastTitleClickX = mouseX;
            lastTitleClickY = mouseY;
            if (doubleClick)
            {
                lastTitleClickTimestamp = 0;
                ToggleMaximize();
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
                int right = System.Math.Min(desktopWidth, resizeStartX + resizeStartWidth);
                int minWidth = System.Math.Min(MinWidth, System.Math.Max(1, right));
                newX = System.Math.Max(0, System.Math.Min(resizeStartX + dx, right - minWidth));
                newWidth = right - newX;
            }
            else if ((resizeMode & ResizeRight) != 0)
            {
                int maxWidth = System.Math.Max(1, desktopWidth - resizeStartX);
                int minWidth = System.Math.Min(MinWidth, maxWidth);
                newWidth = System.Math.Max(minWidth, System.Math.Min(resizeStartWidth + dx, maxWidth));
            }

            if ((resizeMode & ResizeTop) != 0)
            {
                int bottom = System.Math.Min(desktopHeight, resizeStartY + resizeStartHeight);
                int minHeight = System.Math.Min(MinHeight, System.Math.Max(1, bottom));
                newY = System.Math.Max(0, System.Math.Min(resizeStartY + dy, bottom - minHeight));
                newHeight = bottom - newY;
            }
            else if ((resizeMode & ResizeBottom) != 0)
            {
                int maxHeight = System.Math.Max(1, desktopHeight - resizeStartY);
                int minHeight = System.Math.Min(MinHeight, maxHeight);
                newHeight = System.Math.Max(minHeight, System.Math.Min(resizeStartHeight + dy, maxHeight));
            }

            MoveTo(newX, newY);
            Width = newWidth;
            Height = newHeight;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || IsMinimized)
                return;

            SyncDesktopBounds();
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
            UiTypography.Heading(canvas, Title, X + 12, Y + 4,
                System.Math.Max(30, Width - 3 * ButtonSize - 28), titleText);

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
