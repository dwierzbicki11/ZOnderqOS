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
        public int ScreenWidth { get; set; } = int.MaxValue;
        public int ScreenHeight { get; set; } = int.MaxValue;
        public void ValidateRect(int x, int y, int width, int height)
        {
            if (width <= 0 || height <= 0 || x < 0 || y < 0 ||
                (long)x + width > ScreenWidth || (long)y + height > ScreenHeight)
                throw new InvalidOperationException("Notification rectangle outside work area: " +
                    x + "," + y + " " + width + "x" + height +
                    " screen=" + ScreenWidth + "x" + ScreenHeight);
        }
        public virtual void DrawFilledRectangle(Color color, int x, int y, int width, int height)
            => ValidateRect(x, y, width, height);
        public virtual void DrawLine(Color color, int x1, int y1, int x2, int y2) { }
    }
}

namespace Cosmos.Kernel.System.Input
{
    public enum Key { Escape, LeftArrow, RightArrow, UpArrow, DownArrow, PageUp, PageDown, Home, End, Enter, NumEnter, Delete }
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
            int x, int y, int width, int height, int radius)
            => canvas.ValidateRect(x, y, width, height);
        public static void RoundedPanel(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill, Color border)
            => canvas.ValidateRect(x, y, width, height);
        public static void RoundedTop(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill)
            => canvas.ValidateRect(x, y, width, height);
        public static void RoundedFill(Cosmos.Kernel.System.Graphics.Canvas canvas,
            int x, int y, int width, int height, int radius, Color fill)
            => canvas.ValidateRect(x, y, width, height);
    }

    internal static class SmallTextRenderer
    {
        public static void Draw(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, Color color) { }
        public static void DrawClipped(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, int width, Color color)
        {
            if (width <= 0 || x < 0 || (long)x + width > canvas.ScreenWidth)
                throw new InvalidOperationException("Notification text clip outside screen: x=" +
                    x + " width=" + width + " screen=" + canvas.ScreenWidth);
        }
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

    private static void PageNavigationTracksVisibleRows()
    {
        foreach (int height in new[] { 600, 768, 1080 })
        {
            NotificationService.ResetForSession();
            ulong[] ids = new ulong[18];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = Post("row" + i);
            var center = new NotificationCenter(height == 600 ? 800 : height == 768 ? 1366 : 1920,
                height, 44);
            center.Toggle();
            int rows = height == 600 ? 3 : height == 768 ? 5 : 8;
            Check(SelectedId(center) == ids[17], "initial page selects newest at " + height);
            KeyPress(center, Key.PageDown);
            Check(SelectedId(center) == ids[17 - rows],
                "PageDown advances exactly one visible page at " + height);
            KeyPress(center, Key.End);
            Check(SelectedId(center) == ids[0], "End selects oldest at " + height);
            KeyPress(center, Key.PageDown);
            Check(SelectedId(center) == ids[0], "PageDown clamps at oldest");
            KeyPress(center, Key.Home);
            Check(SelectedId(center) == ids[17], "Home selects newest");
            KeyPress(center, Key.PageUp);
            Check(SelectedId(center) == ids[17], "PageUp clamps at newest");
            KeyPress(center, Key.PageDown);
            ulong original = SelectedId(center);
            ulong incoming = Post("incoming");
            KeyPress(center, Key.Delete);
            Check(Entry(original).IsDismissed, "page-selected entry survives incoming alert");
            Check(!Entry(incoming).IsDismissed, "new alert survives page-selected Delete");
        }
    }

    private static void TinyPanelFooterDoesNotTriggerHiddenFilters()
    {
        // At 200x200 the 136px panel places its footer at y=88, while
        // the former filter row covered y=82..118 and stole DND clicks.
        NotificationService.ResetForSession();
        NotificationService.DoNotDisturb = false;
        var center = new NotificationCenter(200, 200, 44);
        center.Toggle();
        Check(center.Width == 180 && center.Height == 136, "tiny panel geometry");
        int x = center.X + 132; // third footer button, shared bounds
        int y = center.Y + center.Height - 48 + 20;
        Check(center.UpdateInteractions(x, y, true, false),
            "tiny footer captures DND click");
        Check(NotificationService.DoNotDisturb,
            "tiny footer DND works instead of switching filter");
        center.UpdateInteractions(x, y, false, true);
        NotificationService.DoNotDisturb = false;
    }

    private static void ModalPanelOwnsOutsidePointerUntilDismissed()
    {
        NotificationService.ResetForSession();
        var center = Open();
        int outsideX = center.X - 5;
        int outsideY = center.Y + 20;
        Check(center.UpdateInteractions(outsideX, outsideY, false, false),
            "open panel captures outside pointer hover");
        Check(center.UpdateInteractions(outsideX, outsideY, true, true),
            "open panel captures held drag outside its bounds");
        Check(center.Visible, "held gesture does not dismiss modal panel");
        Check(center.UpdateInteractions(outsideX, outsideY, true, false),
            "fresh outside press is consumed");
        Check(!center.Visible, "fresh outside press dismisses panel");
        Check(!center.UpdateInteractions(outsideX, outsideY, false, false),
            "closed panel no longer captures pointer");
    }

    private static void CompactRenderingNeverUsesInvalidTextClips()
    {
        // Exercise the production renderer with a host Canvas: any
        // nonpositive clipped-text width throws in the stub above.
        foreach (int width in new[] { 24, 40, 60, 80, 100, 120, 140, 160, 180, 200, 240, 320, 800 })
        {
            foreach (int height in new[] { 90, 120, 160, 200, 240, 480, 600 })
            {
                NotificationService.ResetForSession();
                Post("Compact");
                var center = new NotificationCenter(width, height, 44);
                // All notification rectangles must fit above the 44px taskbar,
                // including the toast and header in very short firmware modes.
                var canvas = new Cosmos.Kernel.System.Graphics.Canvas
                {
                    ScreenWidth = width,
                    ScreenHeight = height - 44
                };
                center.Render(canvas); // active toast
                center.Toggle();
                center.Render(canvas); // notification cards
                NotificationService.ClearAll();
                center.Render(canvas); // empty state
                Check(true, "rendered notification at " + width + "x" + height);
            }
        }
    }

    private static void NarrowCardCannotDismissFromOutside()
    {
        // The old 24px close button began outside a 32px-wide card and
        // could dismiss an alert by clicking the blank panel margin.
        NotificationService.ResetForSession();
        ulong id = Post("keep");
        var center = new NotificationCenter(80, 600, 44);
        center.Toggle();
        int cardX = center.X + 14;
        int cardY = center.Y + 130;
        Check(center.UpdateInteractions(cardX - 3, cardY + 12, true, false),
            "narrow panel owns the margin click");
        Check(!Entry(id).IsDismissed, "off-card click never dismisses alert");
        center.UpdateInteractions(cardX - 3, cardY + 12, false, true);
        Check(center.UpdateInteractions(cardX + 6, cardY + 20, true, false),
            "narrow card remains selectable");
        Check(Entry(id).IsRead && !Entry(id).IsDismissed,
            "compact card selects without accidental dismissal");
    }

    private static void TinyToastUsesActualPaintedHitbox()
    {
        NotificationService.ResetForSession();
        Post("short");
        var center = new NotificationCenter(160, 120, 44);
        // Only 76px remain above the taskbar. The old 98px hitbox would
        // consume clicks in the taskbar area despite not fitting on screen.
        Check(!center.UpdateInteractions(50, 90, true, false),
            "toast cannot capture pointer below its visible 76px work area");
        Check(center.UpdateInteractions(50, 50, true, false),
            "visible toast opens notifications");
        Check(center.Visible, "short toast opens the center");
    }

    public static int Main()
    {
        CompactRenderingNeverUsesInvalidTextClips();
        NarrowCardCannotDismissFromOutside();
        TinyToastUsesActualPaintedHitbox();
        ModalPanelOwnsOutsidePointerUntilDismissed();
        TinyPanelFooterDoesNotTriggerHiddenFilters();
        PageNavigationTracksVisibleRows();
        NewPostDoesNotRetargetDelete();
        NewPostDoesNotRetargetEnter();
        FilterSwitchResetsIdentity();
        MouseSelectionSurvivesIncomingPost();
        EvictedSelectionFallsBackSafely();
        Console.WriteLine("GUI NotificationCenter production C# PASS: " + assertions + " assertions");
        return 0;
    }
}
