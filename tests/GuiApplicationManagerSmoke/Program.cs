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
    public Action? OnMouse;

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
        OnMouse?.Invoke();
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

        // Desktop owns the whole gesture, including motion and release.
        manager.HandleMouse(220, 20, true, true, false, false);
        manager.HandleMouse(500, 500, false, true, false, false);
        Check(right.MouseCount == 1,
            "desktop gesture cannot leak motion or release into active app");

        // A genuine window drag must still deliver release outside its bounds.
        manager.HandleMouse(220, 20, true, false, false, false);
        manager.HandleMouse(500, 500, false, true, false, false);
        Check(right.MouseCount == 3 && right.LastLeftRelease,
            "window-owned drag release reaches owner outside bounds");

        right.Window.Minimize();
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(right.MouseCount == 3, "minimized window cannot receive new press");
        Check(manager.ActiveApplication == left, "minimized window is excluded from active focus");
    }

    private static void WindowGestureSurvivesFocusAndSecondButton()
    {
        var manager = new ApplicationManager();
        var first = new ClosingApp();
        var second = new ClosingApp();
        second.Window.X = 200;
        manager.Launch(first);
        manager.Launch(second);

        manager.HandleMouse(10, 20, true, false, false, false);
        Check(manager.ActiveApplication == first && first.MouseCount == 1,
            "initial press captures first window");
        // A keyboard shortcut may change focus while the left button is held.
        manager.Activate(second);
        manager.HandleMouse(220, 20, true, true, true, false);
        Check(first.MouseCount == 2 && first.LastRightPress,
            "second button must stay with original gesture owner");
        Check(second.MouseCount == 0 && manager.ActiveApplication == second,
            "second button cannot steal focus or click another window");
        manager.HandleMouse(220, 20, false, true, false, true);
        Check(first.MouseCount == 3 && first.LastLeftRelease,
            "release goes to captured window after focus changes");
        Check(second.MouseCount == 0, "release never reaches newly focused window");
        manager.HandleMouse(220, 20, false, false, false, false);
        Check(second.MouseCount == 1, "ordinary hover resumes after release");
        manager.CloseAll();
    }

    private static void FilteredChordKeepsPhysicalGestureOwner()
    {
        var manager = new ApplicationManager();
        var first = new ClosingApp();
        var second = new ClosingApp();
        second.Window.X = 200;
        manager.Launch(first);
        manager.Launch(second);
        manager.HandleMouse(10, 20, true, false, false, false);
        manager.Activate(second);

        // GuiManager suppresses leftClicked/leftWasClicked while right is
        // down. The seventh argument preserves the physical held-left state.
        manager.HandleMouse(220, 20, false, false, true, false, true);
        Check(first.MouseCount == 2 && first.LastRightPress,
            "filtered right chord stays with original window");
        Check(second.MouseCount == 0 && manager.ActiveApplication == second,
            "filtered chord cannot click newly focused window");
        manager.HandleMouse(220, 20, false, false, false, true, true);
        Check(first.MouseCount == 3 && second.MouseCount == 0,
            "filtered chord release stays with original window");
        manager.HandleMouse(220, 20, false, false, false, false);
        Check(second.MouseCount == 1,
            "hover resumes on active window after filtered chord");
        manager.CloseAll();
    }

    private static void ClosingCapturedWindowDoesNotLeakRelease()
    {
        var manager = new ApplicationManager();
        var first = new ClosingApp();
        var second = new ClosingApp();
        second.Window.X = 200;
        manager.Launch(second);
        manager.Launch(first);
        manager.HandleMouse(10, 20, true, false, false, false);
        Check(first.MouseCount == 1, "first window owns initial held press");
        manager.Close(first);
        manager.HandleMouse(220, 20, true, true, false, false);
        manager.HandleMouse(220, 20, false, true, false, false);
        Check(second.MouseCount == 0,
            "closing captured window consumes held motion and release");
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(second.MouseCount == 1,
            "fresh press after captured close reaches surviving window");
        manager.CloseAll();
    }

    private static void MouseCallbackClosingOwnerDoesNotLeakRelease()
    {
        var manager = new ApplicationManager();
        var owner = new ClosingApp();
        var exposed = new ClosingApp();
        exposed.Window.X = 200;
        manager.Launch(exposed);
        manager.Launch(owner);
        owner.OnMouse = () => manager.Close(owner);
        // Model a title-bar close action executed synchronously inside
        // HandleMouse, before the button has been released.
        manager.HandleMouse(10, 20, true, false, false, false);
        Check(owner.CloseCount == 1 && owner.MouseCount == 1,
            "title-bar callback closes captured owner exactly once");
        manager.HandleMouse(220, 20, true, true, false, false);
        manager.HandleMouse(220, 20, false, true, false, false);
        Check(exposed.MouseCount == 0,
            "same close-button gesture cannot click exposed window");
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(exposed.MouseCount == 1, "next fresh click works after callback close");
        manager.CloseAll();
    }

    private static void MinimizingCapturedWindowDoesNotLeakRelease()
    {
        var manager = new ApplicationManager();
        var first = new ClosingApp();
        var second = new ClosingApp();
        second.Window.X = 200;
        manager.Launch(second);
        manager.Launch(first);
        manager.HandleMouse(10, 20, true, false, false, false);
        first.Window.Minimize();
        manager.HandleMouse(220, 20, true, true, false, false);
        manager.HandleMouse(220, 20, false, true, false, false);
        Check(second.MouseCount == 0,
            "minimizing captured window cannot redirect held motion or release");
        manager.HandleMouse(220, 20, true, false, false, false);
        Check(second.MouseCount == 1,
            "new press works after minimizing gesture owner");
        manager.CloseAll();
    }

    private static void FocusReorderDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnUpdate = () => manager.Activate(a);
        manager.Update();
        Check(a.UpdateCount == 1, "MRU activation cannot skip earlier window");
        Check(b.UpdateCount == 1, "MRU activation preserves other updates");
        Check(c.UpdateCount == 1, "MRU activation cannot double-update current window");
        Check(manager.ActiveApplication == a, "focus change survives update");
        manager.CloseAll();
    }

    private static void LaunchDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var launched = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        b.OnUpdate = () =>
        {
            if (!manager.Applications.Contains(launched))
                manager.Launch(launched);
        };
        manager.Update();
        Check(a.UpdateCount == 1 && b.UpdateCount == 1,
            "existing windows updated once when new window launches");
        Check(launched.UpdateCount == 0, "new window waits for next frame");
        manager.Update();
        Check(launched.UpdateCount == 1, "new window updates next frame");
        Check(a.UpdateCount == 2 && b.UpdateCount == 2,
            "reused snapshot preserves next frame's windows");
        manager.CloseAll();
    }

    private static void CloseAnotherDuringUpdate()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        var c = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        manager.Launch(c);
        c.OnUpdate = () => manager.Close(b);
        manager.Update();
        Check(b.UpdateCount == 0 && b.CloseCount == 1,
            "closed window cannot update from snapshot");
        Check(a.UpdateCount == 1 && c.UpdateCount == 1,
            "other windows update exactly once");
        manager.CloseAll();
    }

    private static void ReentrantUpdateIsIgnored()
    {
        var manager = new ApplicationManager();
        var a = new ClosingApp();
        var b = new ClosingApp();
        manager.Launch(a);
        manager.Launch(b);
        b.OnUpdate = () => manager.Update();
        manager.Update();
        Check(a.UpdateCount == 1 && b.UpdateCount == 1,
            "nested Update cannot invalidate outer snapshot");
        manager.CloseAll();
    }

    private static void DesktopRightPressDoesNotLeak()
    {
        var manager = new ApplicationManager();
        var app = new ClosingApp();
        manager.Launch(app);
        manager.HandleMouse(500, 500, false, false, true, false);
        manager.HandleMouse(10, 10, false, false, true, true);
        // Adding left while holding the desktop-owned right button must
        // not focus or click the window beneath the pointer.
        manager.HandleMouse(10, 10, true, false, true, true);
        manager.HandleMouse(10, 10, false, true, false, true);
        Check(app.MouseCount == 0,
            "desktop right/left chord and release cannot click active app");
        manager.HandleMouse(10, 10, false, false, true, false);
        Check(app.MouseCount == 1 && app.LastRightPress,
            "new right press inside window works after desktop gesture");
        manager.CloseAll();
    }

    private static void ActivationRequiresRegisteredSession()
    {
        var manager = new ApplicationManager();
        var registered = new ClosingApp();
        var rogue = new ClosingApp();
        manager.Launch(registered);
        manager.Activate(rogue);
        Check(manager.Applications.Count == 1 && manager.ActiveApplication == registered,
            "Activate cannot register a never-launched app");
        manager.ToggleMinimize(rogue);
        Check(!rogue.Window.IsMinimized, "taskbar cannot minimize an unregistered app");

        ZonderqOS.SecurityContext.IsAuthenticated = false;
        manager.Activate(registered);
        manager.ToggleMinimize(registered);
        Check(!registered.Window.IsMinimized, "logged-out session cannot mutate window focus");
        ZonderqOS.SecurityContext.IsAuthenticated = true;

        manager.Close(registered);
        manager.Activate(registered);
        Check(manager.Applications.Count == 0, "stale activation cannot resurrect closed app");
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
        WindowGestureSurvivesFocusAndSecondButton();
        FilteredChordKeepsPhysicalGestureOwner();
        ClosingCapturedWindowDoesNotLeakRelease();
        MouseCallbackClosingOwnerDoesNotLeakRelease();
        MinimizingCapturedWindowDoesNotLeakRelease();
        FocusReorderDuringUpdate();
        LaunchDuringUpdate();
        CloseAnotherDuringUpdate();
        ReentrantUpdateIsIgnored();
        ActivationRequiresRegisteredSession();
        DesktopRightPressDoesNotLeak();
        Console.WriteLine("PASS: " + checks + " production ApplicationManager C# assertions");
        return 0;
    }
}
