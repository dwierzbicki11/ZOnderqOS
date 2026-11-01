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
        public Action? CloseAction;
        public void RestoreFromMinimized() { IsMinimized = false; Visible = true; }
        public void Minimize() { IsMinimized = true; Visible = false; }
        public bool ContainsPoint(int x, int y) => Visible && !IsMinimized;
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
    public Action? OnClose;

    public override void Close()
    {
        CloseCount++;
        IsRunning = false;
        OnClose?.Invoke();
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

    public static int Main()
    {
        OrdinaryLogout();
        CallbackRemovesAnotherWindow();
        ReentrantLogout();
        Console.WriteLine("PASS: " + checks + " production ApplicationManager C# assertions");
        return 0;
    }
}
