global using Cosmos.Kernel.System.Input;
global using ConsoleKeyEx = Cosmos.Kernel.System.Input.Key;

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Reflection;
using ZonderqOS.GUI;

namespace Cosmos.Kernel.System.Graphics
{
    public class Canvas
    {
        public virtual void DrawFilledRectangle(Color color, int x, int y, int width, int height) { }
        public virtual void DrawLine(Color color, int x1, int y1, int x2, int y2) { }
        public virtual void DrawString(string text, Cosmos.Kernel.System.Graphics.Fonts.Font font,
            Color color, int x, int y) { }
    }
}

namespace Cosmos.Kernel.System.Graphics.Fonts
{
    public class Font { }
    public static class PCScreenFont
    {
        public static readonly Font DefaultFont = new Font();
    }
}

namespace Cosmos.Kernel.System.Input
{
    public enum Key { None, Backspace, UpArrow, DownArrow, Home, End, Enter, NumEnter, PageUp, PageDown }
    public sealed class KeyEvent
    {
        public Key Key { get; set; }
        public char KeyChar { get; set; }
        public ConsoleModifiers Modifiers { get; set; }
    }
}

namespace ZonderqOS
{
    public static class SecurityContext
    {
        public static string CurrentUser => "test";
    }
    public static class SystemSettings
    {
        public static int AutoLockMinutes => 0;
    }
}

namespace ZonderqOS.GUI.Icons
{
    public enum IconType { File, Start, Settings, Close, Reboot, Shutdown, Search }
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
        public static void RequestFrames(int frames = 1) { }
        public static float Approach(float current, float target, float step) => target;
        public static Color Blend(Color from, Color to, float fraction) => to;
    }

    internal static class UiPalette
    {
        public static readonly Color SurfaceRaised = Color.DarkGray;
        public static readonly Color SurfaceHover = Color.Gray;
        public static readonly Color SurfacePressed = Color.LightGray;
        public static readonly Color Surface = Color.Black;
        public static readonly Color Border = Color.Gray;
        public static readonly Color Text = Color.White;
        public static readonly Color AccentBorder = Color.Blue;
        public static readonly Color Accent = Color.Blue;
        public static readonly Color DangerBorder = Color.Red;
    }

    internal static class SystemTheme
    {
        public static readonly Color Accent = Color.Blue;
        public static readonly Color AccentBorder = Color.Blue;
        public static readonly Color AccentSoft = Color.DarkBlue;
    }

    internal static class UiMetrics
    {
        public const int MenuRadius = 8;
        public const int ButtonRadius = 5;
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
        public static void RoundedFill(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill) { }
    }

    public static class TextHelper
    {
        public static int GetTextWidth(string text, Cosmos.Kernel.System.Graphics.Fonts.Font font) =>
            (text ?? "").Length * 8;
        public static void DrawCenteredString(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, Cosmos.Kernel.System.Graphics.Fonts.Font font, Color color,
            int x, int y, int width, int height) { }
    }

    internal static class SmallTextRenderer
    {
        public static void Draw(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, Color color) { }
        public static void DrawClipped(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, int width, Color color) { }
        public static void DrawCentered(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, int width, Color color) { }
        public static int Width(string text) => (text ?? "").Length * 6;
        public static int WidthUInt(ulong value) => value.ToString().Length * 6;
        public static void DrawUInt(Cosmos.Kernel.System.Graphics.Canvas canvas,
            ulong value, int x, int y, Color color) { }
    }
}

internal static class Program
{
    private static int assertions;
    private static readonly Cosmos.Kernel.System.Graphics.Canvas Canvas = new();
    private static int[] pinned = new int[6];
    private static int[] tools = new int[6];
    private static int[] power = new int[4];

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Start menu regression: " + message);
    }

    private static List<Button> Buttons(StartMenu menu, string name)
    {
        FieldInfo? field = typeof(StartMenu).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field != null, "test can inspect " + name);
        return (List<Button>)field!.GetValue(menu)!;
    }

    private static Button Power(StartMenu menu, string name)
    {
        FieldInfo? field = typeof(StartMenu).GetField(name,
            BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field != null, "test can inspect " + name);
        return (Button)field!.GetValue(menu)!;
    }

    private static StartMenu MakeMenu(int width, int height)
    {
        pinned = new int[6];
        tools = new int[6];
        power = new int[4];
        var menu = new StartMenu(8, 8, width, height);
        for (int i = 0; i < 6; i++)
        {
            int index = i;
            menu.AddPinned("Pinned " + index, ZonderqOS.GUI.Icons.IconType.File,
                () => pinned[index]++);
            menu.AddTool("Tool " + index, ZonderqOS.GUI.Icons.IconType.Settings,
                () => tools[index]++);
        }
        menu.SetPowerActions(() => power[2]++, () => power[3]++, () => power[1]++);
        menu.SetLockAction(() => power[0]++);
        menu.Visible = true;
        menu.Render(Canvas);
        return menu;
    }

    private static void Click(StartMenu menu, int x, int y)
    {
        menu.UpdateInteractions(x, y, true, false);
        menu.UpdateInteractions(x, y, false, true);
    }

    private static void CheckSingleAction(int expectedPinned, int expectedTool, int expectedPower)
    {
        int p = 0, t = 0, s = 0;
        foreach (int n in pinned) p += n;
        foreach (int n in tools) t += n;
        foreach (int n in power) s += n;
        Check(p == expectedPinned && t == expectedTool && s == expectedPower,
            "one press performs exactly the intended action");
    }

    private static void Tiny320x240()
    {
        // GUI manager gives Start a 304x180 client on a 320x240 display.
        var menu = MakeMenu(304, 180);
        var pins = Buttons(menu, "pinnedButtons");
        var extras = Buttons(menu, "toolButtons");
        Button lockButton = Power(menu, "lockButton");
        int footerTop = menu.Y + menu.Height - 48;
        int visible = 0;
        foreach (Button button in pins)
        {
            if (!button.Visible) continue;
            visible++;
            Check(button.X >= menu.X && button.X + button.Width <= menu.X + menu.Width,
                "pinned tile is horizontally clipped to menu");
            Check(button.Y >= menu.Y + 70 && button.Y + button.Height <= footerTop,
                "pinned tile stays above power controls");
        }
        foreach (Button button in extras)
            Check(!button.Visible, "tools beyond tiny first row are hidden, not clickable");
        Check(visible == 3, "tiny layout displays three searchable tiles");
        Check(lockButton.Y >= footerTop && lockButton.Y + lockButton.Height <= menu.Y + menu.Height,
            "power button stays in footer");
        Click(menu, pins[0].X + 8, pins[0].Y + 8);
        CheckSingleAction(1, 0, 0);

        menu.Visible = true;
        menu.Render(Canvas);
        Click(menu, lockButton.X + 8, lockButton.Y + 8);
        CheckSingleAction(1, 0, 1);

        // Filtering reveals tool actions without an offscreen/overlapping hitbox.
        menu.ResetSearch();
        menu.Visible = true;
        foreach (char ch in "Tool")
            menu.HandleKeyboard(new KeyEvent { KeyChar = ch });
        menu.Render(Canvas);
        Check(extras[0].Visible, "search exposes a matching tool on tiny screen");
        menu.HandleKeyboard(new KeyEvent { Key = ConsoleKeyEx.Enter });
        CheckSingleAction(1, 1, 1);

        // Restore the unfiltered list and page past the first three entries.
        menu.Visible = true;
        menu.ResetSearch();
        menu.Render(Canvas);
        menu.HandleKeyboard(new KeyEvent { Key = ConsoleKeyEx.PageDown });
        menu.Render(Canvas);
        Check(!pins[0].Visible && pins[3].Visible,
            "PageDown exposes the next three pinned results");
        Click(menu, pins[3].X + 8, pins[3].Y + 8);
        CheckSingleAction(2, 1, 1);
        Check(pinned[3] == 1, "second page click opens the intended app");

        menu.Visible = true;
        menu.HandleKeyboard(new KeyEvent { Key = ConsoleKeyEx.PageDown });
        menu.Render(Canvas);
        Check(extras[0].Visible, "third page contains tool actions");
        Click(menu, extras[0].X + 8, extras[0].Y + 8);
        CheckSingleAction(2, 2, 1);
        Check(tools[0] == 2, "tool can be launched from search or paging");

        menu.Visible = true;
        menu.ResetSearch();
        menu.Render(Canvas);
        Click(menu, menu.X + menu.Width - 30, menu.Y + 12);
        Check(!pins[0].Visible && pins[3].Visible,
            "mouse next-page control advances without launching an app");
        CheckSingleAction(2, 2, 1);

        // A mode change can increase the number of visible columns/rows.
        // The old page offset must realign to the new page capacity.
        menu.Visible = true;
        menu.HandleKeyboard(new KeyEvent { Key = ConsoleKeyEx.PageDown });
        menu.Height = 260;
        menu.Render(Canvas);
        Check(pins[0].Visible, "mode change realigns the tiny page to its new capacity");
        foreach (Button button in pins)
            Check(!button.Visible || button.Y + button.Height <= menu.Y + menu.Height - 48,
                "mode change keeps results outside power footer");
    }

    private static void Tiny200x200()
    {
        var menu = MakeMenu(184, 140);
        foreach (Button button in Buttons(menu, "pinnedButtons"))
            Check(!button.Visible, "140px menu hides clipped app tiles");
        foreach (Button button in Buttons(menu, "toolButtons"))
            Check(!button.Visible, "140px menu hides clipped tool tiles");
        Button lockButton = Power(menu, "lockButton");
        Check(lockButton.Visible, "safe power controls remain accessible at 184px width");
        Click(menu, lockButton.X + 2, lockButton.Y + 5);
        CheckSingleAction(0, 0, 1);
    }

    private static void NarrowTall()
    {
        var menu = MakeMenu(184, 540);
        int footerTop = menu.Y + menu.Height - 48;
        foreach (Button button in Buttons(menu, "pinnedButtons"))
            Check(!button.Visible || (button.X >= menu.X &&
                button.X + button.Width <= menu.X + menu.Width &&
                button.Y + button.Height <= footerTop),
                "tall narrow menu does not paint offscreen pinned buttons");
        foreach (Button button in Buttons(menu, "toolButtons"))
            Check(!button.Visible || (button.X >= menu.X &&
                button.X + button.Width <= menu.X + menu.Width &&
                button.Y + button.Height <= footerTop),
                "tall narrow menu does not paint offscreen tool buttons");
    }

    private static void Normal640x480()
    {
        var menu = MakeMenu(480, 420);
        int footerTop = menu.Y + menu.Height - 76;
        foreach (Button button in Buttons(menu, "pinnedButtons"))
            Check(button.Visible && button.Y + button.Height <= footerTop,
                "normal 640x480 pinned grid fits above footer");
        foreach (Button button in Buttons(menu, "toolButtons"))
            Check(button.Visible && button.Y + button.Height <= footerTop,
                "normal 640x480 tools fit above footer");
        Click(menu, Power(menu, "shutdownButton").X + 5,
            Power(menu, "shutdownButton").Y + 5);
        CheckSingleAction(0, 0, 1);
        Check(power[3] == 1, "shutdown only fires from its own button");
    }

    public static int Main()
    {
        Tiny320x240();
        Tiny200x200();
        NarrowTall();
        Normal640x480();
        Console.WriteLine("PASS: " + assertions + " production StartMenu/Button C# assertions");
        return 0;
    }
}
