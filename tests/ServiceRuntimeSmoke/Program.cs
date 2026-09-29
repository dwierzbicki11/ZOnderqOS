#if ZSRV_HOST_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using ZonderqOS.SystemCore.Services;

internal static class ServiceRuntimeSmoke
{
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zservice-smoke-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ZSRV_TEST_ROOT", root);
        try
        {
            ServiceManager.Initialize();
            Check(Find("heartbeat").Running, "Enabled boot service did not start.");
            string config = Path.Combine(ServiceManager.ConfigRoot, "heartbeat.conf");
            string log = Path.Combine(root, "var", "log", "zservices", "heartbeat.log");
            WaitUntil(() => File.Exists(log), "Heartbeat did not write a log.");

            string error;
            Check(ServiceManager.SetEnabled("heartbeat", false, out error), error);
            WaitUntil(() => !Find("heartbeat").Running, "Disable did not stop the service.");
            Check(File.ReadAllText(config).Contains("enabled=false"), "Disable was not persisted.");
            Check(ServiceManager.Reload(out error) && !Find("heartbeat").Enabled,
                "Disabled state was lost on reload: " + error);
            ServiceManager.Initialize();
            Check(!Find("heartbeat").Running, "Disabled service started during another boot initialization.");

            File.Delete(config + ".bak");
            File.Move(config, config + ".bak");
            Check(ServiceManager.Reload(out error) && File.Exists(config),
                "Interrupted configuration write was not recovered: " + error);
            Check(ServiceManager.SetEnabled("HEARTBEAT", true, out error), error);
            int oldPid = Find("heartbeat").Pid;
            Check(ServiceManager.Reload(out error) && Find("heartbeat").Pid == oldPid,
                "Unchanged service was restarted on reload: " + error);
            File.WriteAllText(config, "type=memory\nenabled=true\ninterval_seconds=5\n");
            Check(ServiceManager.Reload(out error), "Changed service reload failed: " + error);
            Check(Find("heartbeat").Pid != oldPid, "Changed service was not restarted on reload.");
            WaitUntil(() => File.ReadAllText(log).Contains("free_pages="),
                "Changed service definition did not take effect.");
            oldPid = Find("heartbeat").Pid;
            Check(ServiceManager.Restart("heartbeat", out error), error);
            Check(Find("heartbeat").Pid != oldPid, "Restart did not replace the process.");

            File.WriteAllText(Path.Combine(ServiceManager.ConfigRoot, "custom.conf"),
                "type=memory\nenabled=false\ninterval_seconds=5\n");
            Check(ServiceManager.Reload(out error) && !Find("custom").Running,
                "Custom configuration was not loaded: " + error);
            ZonderqOS.SecurityContext.IsAuthenticated = false;
            Check(!ServiceManager.SetEnabled("custom", true, out error),
                "Unauthenticated user changed service configuration.");
            ZonderqOS.SecurityContext.IsAuthenticated = true;
            Check(ServiceManager.SetEnabled("custom", true, out error) && Find("custom").Running, error);
            Check(ServiceManager.SetEnabled("custom", false, out error), error);
            Check(ServiceManager.SetEnabled("heartbeat", false, out error), error);
            WaitUntil(() => !Find("custom").Running && !Find("heartbeat").Running,
                "Services remained running after disable.");
            Console.WriteLine("[SERVICE-RUNTIME] PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[SERVICE-RUNTIME] FAIL: " + ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    private static ServiceStatus Find(string name)
    {
        var items = new List<ServiceStatus>();
        ServiceManager.FillStatus(items);
        foreach (var item in items)
            if (item.Name == name)
                return item;
        throw new InvalidOperationException("Missing service: " + name);
    }

    private static void WaitUntil(Func<bool> condition, string message)
    {
        for (int i = 0; i < 200; i++)
        {
            if (condition())
                return;
            Thread.Sleep(10);
        }
        throw new InvalidOperationException(message);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}

namespace ZonderqOS
{
    public static class SecurityContext
    {
        public static bool IsAuthenticated = true;
        public static int CurrentUid = 0;
    }

    public static class PermissionManager
    {
        public static void SetPermission(string path, string owner, int mode) { }
    }

    public static class WriteMessage
    {
        public static void WriteError(string message, string category) { }
    }
}

namespace ZonderqOS.SystemCore
{
    public enum SystemLogLevel { Warning }

    public static class SystemLogger
    {
        public static void Log(SystemLogLevel level, string category, string message) { }
    }
}

namespace Cosmos.Kernel.Core.Memory
{
    public static class PageAllocator
    {
        public static ulong FreePageCount => 1234;
    }
}
#endif
