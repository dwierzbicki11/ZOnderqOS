global using Cosmos.Kernel.System.Input;
global using ConsoleKeyEx = Cosmos.Kernel.System.Input.Key;

using System;
using System.Drawing;
using System.Reflection;
using ZonderqOS;
using ZonderqOS.GUI;

namespace Cosmos.Kernel.System.Graphics
{
    public class Canvas
    {
        public virtual void DrawFilledRectangle(Color color, int x, int y, int width, int height) { }
        public virtual void DrawLine(Color color, int x1, int y1, int x2, int y2) { }
    }
}

namespace Cosmos.Kernel.System.Input
{
    public enum Key { Escape, LeftArrow, RightArrow, UpArrow, DownArrow, Enter, NumEnter, Delete }
    public sealed class KeyEvent
    {
        public Key Key { get; set; }
        public char KeyChar { get; set; }
        public ConsoleModifiers Modifiers { get; set; }
    }
}

namespace ZonderqOS
{
    public static class SystemSettings
    {
        public static int TimeZoneOffsetHours => 0;
    }
}

namespace ZonderqOS.GUI.Icons
{
    public enum IconType { About, Network, DiskManager, Close, Settings, Trash }
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
    }

    internal static class SystemTheme
    {
        public static readonly Color Accent = Color.Blue;
        public static readonly Color AccentSoft = Color.DarkBlue;
        public static readonly Color AccentBorder = Color.Blue;
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

    internal static class SmallTextRenderer
    {
        public static void Draw(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, Color color) { }
        public static void DrawClipped(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, int width, Color color) { }
        public static void DrawCentered(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, int width, Color color) { }
        public static int WidthUInt(ulong value) => value.ToString().Length * 6;
        public static void DrawUInt(Cosmos.Kernel.System.Graphics.Canvas canvas,
            ulong value, int x, int y, Color color) { }
    }
}

internal static class Program
{
    private static int assertions;

    private static void Check(bool condition, string message)
    {
        assertions++;
        if (!condition)
            throw new InvalidOperationException("Notification center regression: " + message);
    }

    private static ulong Post(string title, NotificationKind kind = NotificationKind.Info)
    {
        return NotificationService.Post(kind, "TEST", title, "Body");
    }

    private static NotificationEntry Entry(ulong id)
    {
        Check(NotificationService.TryGetById(id, out NotificationEntry entry),
            "notification " + id + " still exists");
        return entry;
    }

    private static NotificationCenter Open()
    {
        var center = new NotificationCenter(800, 600, 44);
        center.Toggle();
        Check(center.Visible, "notification center opens");
        return center;
    }

    private static void KeyPress(NotificationCenter center, Key key)
    {
        Check(center.HandleKeyboard(new KeyEvent { Key = key }), "keyboard event handled");
    }

    private static ulong SelectedId(NotificationCenter center)
    {
        FieldInfo? field = typeof(NotificationCenter).GetField("selectedNotificationId",
            BindingFlags.NonPublic | BindingFlags.Instance);
        Check(field != null, "selected notification identity is tracked");
        return (ulong)field!.GetValue(center)!;
    }

    private static void NewPostDoesNotRetargetDelete()
    {
        NotificationService.ResetForSession();
        ulong a = Post("A");
        ulong b = Post("B");
        var center = Open();
        KeyPress(center, Key.DownArrow);
        Check(SelectedId(center) == a, "older notification selected");
        ulong c = Post("C");
        KeyPress(center, Key.Delete);
        Check(Entry(a).IsDismissed, "Delete dismisses originally selected A");
        Check(!Entry(b).IsDismissed && !Entry(c).IsDismissed,
            "newest C and existing B survive keyboard Delete");
    }

    private static void NewPostDoesNotRetargetEnter()
    {
        NotificationService.ResetForSession();
        ulong a = Post("A");
        ulong b = Post("B");
        var center = Open();
        KeyPress(center, Key.DownArrow);
        ulong c = Post("C");
        KeyPress(center, Key.Enter);
        Check(Entry(a).IsRead, "Enter reads originally selected A");
        Check(!Entry(b).IsRead && !Entry(c).IsRead,
            "new arrival and other notification remain unread");
    }

    private static void FilterSwitchResetsIdentity()
    {
        NotificationService.ResetForSession();
        Post("A");
        ulong important = Post("B", NotificationKind.Warning);
        Post("C");
        var center = Open();
        KeyPress(center, Key.RightArrow); // Unread
        KeyPress(center, Key.RightArrow); // Important
        Check(SelectedId(center) == important, "important filter selects matching notification");
        ulong newcomer = Post("D", NotificationKind.Error);
        KeyPress(center, Key.Delete);
        Check(Entry(important).IsDismissed, "important selection survives new important alert");
        Check(!Entry(newcomer).IsDismissed, "new important alert is not dismissed");
    }

    private static void MouseSelectionSurvivesIncomingPost()
    {
        NotificationService.ResetForSession();
        Post("A");
        ulong b = Post("B");
        Post("C");
        var center = Open();
        int x = center.X + 80;
        int y = center.Y + 130 + 89 + 20; // second painted card
        Check(center.UpdateInteractions(x, y, true, false), "second card owns click");
        center.UpdateInteractions(x, y, false, true);
        Check(SelectedId(center) == b, "mouse selects B");
        ulong d = Post("D");
        KeyPress(center, Key.Delete);
        Check(Entry(b).IsDismissed, "mouse-selected B remains keyboard target");
        Check(!Entry(d).IsDismissed, "incoming D survives");
    }

    private static void EvictedSelectionFallsBackSafely()
    {
        NotificationService.ResetForSession();
        ulong old = Post("old");
        Post("next");
        var center = Open();
        KeyPress(center, Key.DownArrow);
        Check(SelectedId(center) == old, "old notification selected");
        for (int i = 0; i < NotificationService.Capacity + 4; i++)
            Post("new" + i);
        KeyPress(center, Key.Delete);
        Check(SelectedId(center) != old, "evicted ID cannot remain selected");
        Check(!NotificationService.TryGetById(old, out _), "evicted ID is absent");
    }

    public static int Main()
    {
        NewPostDoesNotRetargetDelete();
        NewPostDoesNotRetargetEnter();
        FilterSwitchResetsIdentity();
        MouseSelectionSurvivesIncomingPost();
        EvictedSelectionFallsBackSafely();
        Console.WriteLine("GUI NotificationCenter production C# PASS: " + assertions + " assertions");
        return 0;
    }
}
