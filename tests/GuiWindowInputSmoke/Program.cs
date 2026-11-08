using System;
using System.Drawing;
using ZonderqOS.GUI;

// Compile the actual production Window and Widget; only rendering dependencies
// are stubbed. No framebuffer, guest filesystem or Cosmos device is accessed.
namespace Cosmos.Kernel.System.Graphics
{
    public abstract class Canvas
    {
        public virtual void DrawFilledRectangle(Color color, int x, int y, int width, int height) { }
        public virtual void DrawRectangle(Color color, int x, int y, int width, int height) { }
        public virtual void DrawLine(Color color, int x1, int y1, int x2, int y2) { }
    }
}

namespace Cosmos.Kernel.System.Graphics.Fonts { }

namespace ZonderqOS.GUI.Icons
{
    public enum IconType { Restore, Maximize, Close }

    public static class IconManager
    {
        public static void DrawScaled(Cosmos.Kernel.System.Graphics.Canvas canvas,
            IconType icon, int x, int y, int width, int height) { }
    }
}

namespace ZonderqOS.GUI
{
    internal static class UiAnimator
    {
        public static void RequestFrames() { }
        public static float Approach(float current, float target, float step) => target;
        public static Color Blend(Color from, Color to, float fraction) => to;
    }

    internal static class UiPalette
    {
        public static readonly Color Border = Color.Gray;
        public static readonly Color AccentBorder = Color.Blue;
        public static readonly Color SurfaceRaised = Color.DarkGray;
        public static readonly Color AccentSoft = Color.LightBlue;
        public static readonly Color TextMuted = Color.Gray;
        public static readonly Color Text = Color.White;
        public static readonly Color Surface = Color.Black;
        public static readonly Color Accent = Color.Blue;
    }

    internal static class SystemTheme
    {
        public static readonly Color Accent = Color.Blue;
        public static readonly Color AccentSoft = Color.LightBlue;
        public static readonly Color AccentBorder = Color.Blue;
    }

    internal static class UiMetrics
    {
        public const int WindowRadius = 8;
        public const int TileRadius = 4;
    }

    internal static class UiPainter
    {
        public static void Shadow(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius) { }
        public static void RoundedPanel(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill, Color border) { }
        public static void RoundedTop(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill) { }
    }

    internal static class UiTypography
    {
        public static void Heading(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string title, int x, int y, int width, Color color) { }
    }
}

internal sealed class TestWidget : Widget
{
    public TestWidget(int x, int y) : base(x, y, 20, 20) { }
    public override void Render(Cosmos.Kernel.System.Graphics.Canvas canvas) { }
}

internal static class Program
{
    private static int assertions;

    private static void Check(bool ok, string message)
    {
        assertions++;
        if (!ok)
            throw new InvalidOperationException("Window input regression: " + message);
    }

    private static void InterruptedDrag()
    {
        Window.ConfigureDesktop(800, 556);
        var window = new Window(100, 100, 500, 300, "drag");
        var child = new TestWidget(10, 40);
        window.AddChild(child);
        Check(child.X == 110 && child.Y == 140, "child starts relative to window");

        window.HandleMouse(130, 115, true, false); // press caption
        window.HandleMouse(170, 145, true, true);  // held drag
        Check(window.X == 140 && window.Y == 130, "held drag moves window");
        Check(child.X == 150 && child.Y == 170, "child follows dragged window");

        // A Start/menu overlay swallowed the release: Window never saw false.
        // The next fresh press is in the client, NOT another caption drag.
        window.HandleMouse(250, 240, true, false);
        Check(window.X == 140 && window.Y == 130, "fresh press discards stale drag");
        Check(child.X == 150 && child.Y == 170, "stale drag cannot move children");

        window.HandleMouse(250, 240, false, true);
        window.HandleMouse(170, 145, true, false);
        window.HandleMouse(190, 155, true, true);
        Check(window.X == 160 && window.Y == 140, "next genuine drag still works");
        Check(child.X == 170 && child.Y == 180, "child tracks next genuine drag");
        window.HandleMouse(190, 155, false, true);

        window.IsActive = true;
        window.HandleMouse(190, 155, true, false);
        window.IsActive = false;
        window.IsActive = true;
        window.HandleMouse(250, 240, true, true);
        Check(window.X == 160 && window.Y == 140, "focus loss cancels held drag");
    }

    private static void InterruptedResize()
    {
        Window.ConfigureDesktop(800, 556);
        var window = new Window(100, 100, 500, 300, "resize");

        window.HandleMouse(598, 250, true, false); // right resize border
        window.HandleMouse(640, 250, true, true);  // held resize
        Check(window.Width == 542, "held resize changes width");
        Check(window.X == 100, "right resize does not move left edge");

        // Modal overlay swallowed the release. Next press is in client.
        window.HandleMouse(320, 250, true, false);
        Check(window.Width == 542, "fresh press discards stale resize");
        Check(window.X == 100, "stale resize cannot shift window");

        window.HandleMouse(320, 250, false, true);
        window.HandleMouse(640, 200, true, false);
        window.HandleMouse(662, 200, true, true);
        Check(window.Width == 564, "next genuine resize still works");
        window.HandleMouse(662, 200, false, true);
    }

    private static void ResolutionAndCaption()
    {
        Window.ConfigureDesktop(1366, 724); // 1366x768 minus 44px taskbar
        var window = new Window(1200, 700, 900, 650, "bounds");
        Check(window.X == 454 && window.Y == 62, "HD window constrained to work area");

        window.ToggleMaximize();
        Check(window.IsMaximized && window.Width == 1366 && window.Height == 724,
            "maximize fills HD work area");

        Window.ConfigureDesktop(800, 556);
        window.ContainsPoint(1, 1); // synchronize to physical mode change
        Check(window.IsMaximized && window.Width == 800 && window.Height == 556,
            "maximized window follows 800x600 framebuffer");
        window.ToggleMaximize();
        Check(!window.IsMaximized && window.X >= 0 && window.Y >= 0 &&
            window.X + window.Width <= 800 && window.Y + window.Height <= 556,
            "restore stays inside 800x600 work area");

        int closes = 0;
        window.CloseAction = () => closes++;
        window.HandleMouse(window.X + window.Width - 15, window.Y + 15, true, false);
        Check(closes == 1, "caption close invokes owner action exactly once");
        window.HandleMouse(window.X + window.Width - 15, window.Y + 15, true, true);
        Check(closes == 1, "held caption click does not close twice");

        Window.ConfigureDesktop(1920, 1036);
        window.ContainsPoint(1, 1);
        Check(window.X >= 12 && window.Y >= 12 &&
            window.X + window.Width <= 1908 && window.Y + window.Height <= 1024,
            "window respects Full HD inset after mode change");
    }

    private static void KeyboardFirstResolutionReflow()
    {
        Window.ConfigureDesktop(1366, 724);
        var window = new Window(1000, 600, 500, 300, "keyboard-first");
        var child = new TestWidget(10, 40);
        window.AddChild(child);

        // No Render(), ContainsPoint() or mouse event has occurred since
        // the mode change. File Manager's keyboard path explicitly calls
        // EnsureDesktopBounds before computing visible rows/columns.
        Window.ConfigureDesktop(800, 556);
        window.EnsureDesktopBounds();
        Check(window.X >= 0 && window.Y >= 0 &&
            window.X + window.Width <= 800 && window.Y + window.Height <= 556,
            "keyboard-first reflow clamps the window to the new work area");
        Check(child.X == window.X + 10 && child.Y == window.Y + 40,
            "keyboard-first reflow keeps child coordinates aligned");

        window.ToggleMaximize();
        Window.ConfigureDesktop(1920, 1036);
        window.EnsureDesktopBounds();
        Check(window.IsMaximized && window.X == 0 && window.Y == 0 &&
            window.Width == 1920 && window.Height == 1036,
            "keyboard-first reflow expands maximized window to Full HD");
        Check(child.X == window.X + 10 && child.Y == window.Y + 40,
            "maximized reflow preserves child offsets");
    }

    public static int Main()
    {
        KeyboardFirstResolutionReflow();
        InterruptedDrag();
        InterruptedResize();
        ResolutionAndCaption();
        Console.WriteLine("PASS: " + assertions + " production Window C# assertions");
        return 0;
    }
}
