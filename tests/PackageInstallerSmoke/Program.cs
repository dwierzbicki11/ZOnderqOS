using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.SystemCore.Packages;

internal static class Program
{
    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zpkg-smoke-" + Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("ZPKG_TEST_ROOT", root);
        try
        {
            string source = Path.Combine(root, "source");
            string payload = Path.Combine(source, "payload");
            Directory.CreateDirectory(payload);
            File.WriteAllText(Path.Combine(source, "package.zpkg"), "name=demo\nversion=1.0\ndescription=Data\n");
            File.WriteAllText(Path.Combine(payload, "data.txt"), "hello");

            PackageManifest manifest;
            int files;
            ulong bytes;
            string error;
            Check(PackageManager.VerifySource(source, out manifest, out files, out bytes, out error), error);
            Check(files == 1 && bytes == 5, "Incorrect payload totals.");

            InstalledPackage installed;
            Check(PackageManager.Install(source, out installed, out error), error);
            Check(File.ReadAllText(Path.Combine(installed.InstallPath, "data.txt")) == "hello", "Payload was not installed.");
            Check(!PackageManager.Install(source, out installed, out error), "Duplicate install accepted.");
            Check(!PackageManager.RepairInterruptedInstall("demo", "1.0", out error), "Repair removed an installed package.");
            Check(PackageManager.TryGetInstalled("demo", out installed, out error), error);

            var packages = new List<InstalledPackage>();
            Check(PackageManager.ListInstalled(packages, out error) && packages.Count == 1, "Registry listing failed.");
            Check(PackageManager.Remove("demo", out error), error);
            Check(!PackageManager.TryGetInstalled("demo", out installed, out error), "Removed package remains registered.");

            string orphan = Path.Combine(PackageManager.StoreRoot, "demo", "1.0");
            Directory.CreateDirectory(orphan);
            File.WriteAllText(Path.Combine(orphan, "orphan.txt"), "x");
            Check(PackageManager.RepairInterruptedInstall("demo", "1.0", out error), error);
            Check(!Directory.Exists(orphan), "Repair left an orphaned payload.");

            File.WriteAllText(Path.Combine(source, "package.zpkg"), new string('x', 4097));
            Check(!PackageManager.VerifySource(source, out manifest, out files, out bytes, out error), "Oversized manifest accepted.");
            File.WriteAllText(Path.Combine(source, "package.zpkg"), "name=demo\nversion=1.0\n");
            Check(!PackageManager.VerifySource(source + "/../source", out manifest, out files, out bytes, out error), "Parent path accepted.");

            string many = Path.Combine(root, "many");
            Directory.CreateDirectory(Path.Combine(many, "payload"));
            File.WriteAllText(Path.Combine(many, "package.zpkg"), "name=many\nversion=1.0\n");
            for (int i = 0; i < 4097; i++)
                Directory.CreateDirectory(Path.Combine(many, "payload", "d" + i));
            Check(!PackageManager.VerifySource(many, out manifest, out files, out bytes, out error), "Excessive empty directories accepted.");

            ZonderqOS.SecurityContext.IsAuthenticated = false;
            Check(!PackageManager.Install(source, out installed, out error), "Unauthenticated install accepted.");
            Console.WriteLine("[ZPKG-INSTALLER] PASS");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[ZPKG-INSTALLER] FAIL: " + ex);
            return 1;
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
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
        public static string CurrentUser = "root";
    }

    public static class SecurityLogger
    {
        public static void LogEvent(string level, string message) { }
    }

    public static class PermissionManager
    {
        public static void SetPermission(string path, string owner, int permissions) { }
        public static void RemovePermission(string path) { }
        public static void RemovePermissionsUnder(string path) { }
    }
}
