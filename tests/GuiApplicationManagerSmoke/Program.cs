using System;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Apps;

// Compile the production ApplicationManager with minimal host-only window
// and input stubs. No Cosmos device, framebuffer or filesystem is touched.
namespace Cosmos.Kernel.System.Graphics
{
    public abstract class Canvas { }
}

namespace ZonderqOS
{
    public static class SecurityContext
    {
        public static bool IsAuthenticated = true;
    }
}

namespace ZonderqOS.GUI
{
    public sealed class Window
    {
        public bool Visible = true;
        public bool IsMinimized;
        public bool IsActive;
        public int X;
        public int Y;
        public int Width = 100;
        public int Height = 100;
        public Action? CloseAction;
        public void RestoreFromMinimized() { IsMinimized = false; Visible = true; }
        public void Minimize() { IsMinimized = true; Visible = false; }
        public bool ContainsPoint(int x, int y) => Visible && !IsMinimized &&
            x >= X && x < X + Width && y >= Y && y < Y + Height;
    }
}

namespace ZonderqOS.GUI.Apps
{
    public sealed class KeyEvent { }

    public class Application
    {
        public bool IsRunning = true;
        public Window Window = new Window();
        public virtual void Close() { IsRunning = false; }
        public virtual void Update() { }
        public virtual void Render(Cosmos.Kernel.System.Graphics.Canvas canvas) { }
        public virtual void HandleKeyboard(KeyEvent key) { }
        public virtual void HandleMouse(int x, int y, bool left, bool oldLeft, bool right, bool oldRight) { }
    }

    public sealed class TaskManagerModernApp : Application { }
}

internal sealed class ClosingApp : Application
{
    public int CloseCount;
    public int UpdateCount;
    public int MouseCount;
    public bool LastRightPress;
    public bool LastLeftRelease;
    public Action? OnClose;
    public Action? OnUpdate;

    public override void Close()
    {
        CloseCount++;
        IsRunning = false;
        OnClose?.Invoke();
    }

    public override void Update()
    {
        UpdateCount++;
        OnUpdate?.Invoke();
    }

    public override void HandleMouse(int x, int y, bool left, bool oldLeft, bool right, bool oldRight)
    {
        MouseCount++;
        LastRightPress = right && !oldRight;
        LastLeftRelease = !left && oldLeft;
    }
}

internal static class Program
{
    private static int checks;
    private static void Check(bool value, string label)
    {
        checks++;
        if (!value)
            throw new InvalidOperationException("ApplicationManager close regression: " + label);
    }

    private static void OrdinaryLogout()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        manager.CloseAll();
        Check(manager.Applications.Count == 0, "ordinary logout clears windows");
        Check(a.CloseCount == 1 && b.CloseCount == 1 && c.CloseCount == 1,
            "ordinary logout closes each exactly once");
        Check(manager.ActiveApplication == null, "no active window after logout");
    }

    private static void CallbackRemovesAnotherWindow()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnClose = () => manager.Close(a);
        manager.CloseAll();
        Check(manager.Applications.Count == 0, "callback removal leaves no windows");
        Check(a.CloseCount == 1 && b.CloseCount == 1 && c.CloseCount == 1,
            "callback removal cannot double-close or skip a window");
    }

    private static void ReentrantLogout()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnClose = manager.CloseAll;
        manager.CloseAll();
        Check(manager.Applications.Count == 0, "nested logout clears windows");
        Check(a.CloseCount == 1 && b.CloseCount == 1 && c.CloseCount == 1,
            "nested logout closes each exactly once");
        manager.CloseAll();
        Check(a.CloseCount == 1 && b.CloseCount == 1 && c.CloseCount == 1,
            "repeated logout is idempotent");
    }

    private static void LogoutDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnUpdate = () => ZonderqOS.SecurityContext.IsAuthenticated = false;
        manager.Update();
        Check(c.UpdateCount == 1, "logout initiator updated once");
        Check(a.UpdateCount == 0 && b.UpdateCount == 0,
            "no more application updates after authentication ends");
        Check(manager.Applications.Count == 0, "logout closes all windows in same frame");
        Check(a.CloseCount == 1 && b.CloseCount == 1 && c.CloseCount == 1,
            "logout callback closes each window once");
        ZonderqOS.SecurityContext.IsAuthenticated = true;
    }

    private static void CloseAllDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnUpdate = manager.CloseAll;
        manager.Update();
        Check(manager.Applications.Count == 0, "CloseAll from Update leaves no stale index");
        Check(a.UpdateCount == 0 && b.UpdateCount == 0 && c.UpdateCount == 1,
            "session teardown stops remaining frame updates");
    }

    private static void RemoveEarlierDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnUpdate = () => manager.Close(a);
        manager.Update();
        Check(c.UpdateCount == 1, "list shift does not update current window twice");
        Check(b.UpdateCount == 1, "earlier surviving window is still updated");
        Check(a.UpdateCount == 0, "closed window is not updated");
        Check(manager.Applications.Count == 2, "surviving windows remain registered");
    }

    private static void CloseCallbackReentrancy()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        a.OnClose = () =>
        {
            manager.Close(a); // Closing the same instance must be idempotent.
            manager.Close(b); // Nested close must not skip the other instance.
        };
        manager.Close(a);
        Check(a.CloseCount == 1, "reentrant Close invokes callback once");
        Check(b.CloseCount == 1, "nested Close invokes other callback once");
        Check(manager.Applications.Count == 0, "nested Close empties manager");
        manager.Close(a);
        Check(a.CloseCount == 1, "closing detached app is a no-op");
    }

    private static void LogoutCallbackCannotReopenWindows()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var attempted = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        b.OnClose = () =>
        {
            manager.Launch(attempted);
            manager.Activate(b);
            manager.CloseAll(); // Nested teardown must not restart the loop.
        };
        manager.CloseAll();
        Check(manager.Applications.Count == 0, "logout cannot reopen windows");
        Check(a.CloseCount == 1 && b.CloseCount == 1, "logout closes originals once");
        Check(attempted.CloseCount == 0, "rejected launch never starts closing callback");
        Check(manager.ActiveApplication == null, "logout has no foreground window");
        manager.Launch(attempted);
        Check(manager.Applications.Count == 1, "new session can launch after teardown");
        manager.CloseAll();
        Check(attempted.CloseCount == 1, "subsequent teardown closes new session");
    }

    private static void MouseFocusAndCapture()
    {
        var manager = new ApplicationManager();
        var left = new ClosingApp();
        var right = new ClosingApp();
        right.Window.X = 200;
        manager.Launch(left);
        manager.Launch(right);

        // Right-clicking a background window must focus and dispatch to it.
        manager.HandleMouse(10, 20, false, false, true, false);
        Check(manager.ActiveApplication == left, "right press focuses background window");
        Check(left.MouseCount == 1 && left.LastRightPress, "right press reaches focused app");
        Check(right.MouseCount == 0, "foreground app does not receive background right press");

        // A left press on another window should change focus and target.
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(manager.ActiveApplication == right, "left press focuses other window");
        Check(right.MouseCount == 1 && left.MouseCount == 1,
            "left press reaches only the hit window");

        // Clicking empty desktop must not trigger a foreground control.
        manager.HandleMouse(500, 500, true, false, false, false);
        Check(right.MouseCount == 1, "desktop press is not sent to app");

        // A held gesture can end outside the window: forward the release.
        manager.HandleMouse(500, 500, false, true, false, false);
        Check(right.MouseCount == 2 && right.LastLeftRelease,
            "drag release reaches active window outside its bounds");

        right.Window.Minimize();
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(right.MouseCount == 2, "minimized window cannot receive new press");
        Check(manager.ActiveApplication == left, "minimized window is excluded from active focus");
    }

    public static int Main()
    {
        OrdinaryLogout();
        CallbackRemovesAnotherWindow();
        ReentrantLogout();
        LogoutDuringUpdate();
        CloseAllDuringUpdate();
        RemoveEarlierDuringUpdate();
        CloseCallbackReentrancy();
        LogoutCallbackCannotReopenWindows();
        MouseFocusAndCapture();
        Console.WriteLine("PASS: " + checks + " production ApplicationManager C# assertions");
        return 0;
    }
}
