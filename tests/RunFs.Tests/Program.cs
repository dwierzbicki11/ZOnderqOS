using System;
using ZonderqOS.SystemCore;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static int Main()
    {
        try
        {
            Require(RunFs.DirectoryExists("/run"), "run root missing");
            Require(RunFs.DirectoryExists("/run/lock"), "run/lock missing");
            Require(RunFs.DirectoryExists("/run/services"), "run/services missing");
            Require(RunFs.DirectoryExists("/run/user"), "run/user missing");

            Require(RunFs.TryCreateDirectory("/run/test", out string error), error);
            Require(RunFs.TryWrite("/run/test/state", "ready", false, out error), error);
            Require(RunFs.TryWrite("/run/test/state", "\nok", true, out error), error);
            Require(RunFs.TryRead("/run/test/state", out string content), "read failed");
            Require(content == "ready\nok", "append semantics mismatch");

            Require(RunFs.TryCopyFile("/run/test/state", "/run/test/copy", out error), error);
            Require(RunFs.TryMoveFile("/run/test/copy", "/run/test/moved", out error), error);
            Require(!RunFs.FileExists("/run/test/copy"), "move left source behind");
            Require(RunFs.FileExists("/run/test/moved"), "move lost destination");

            Require(!RunFs.TryCreateDirectory("/run/missing/child", out error), "missing parent accepted");
            Require(!RunFs.TryWrite("/run/test/too-big", new string('x', 64 * 1024 + 1), false, out error), "oversized file accepted");
            Require(!RunFs.TryDeleteDirectory("/run/test", false, out error), "non-empty directory removed without recursion");
            Require(RunFs.TryDeleteDirectory("/run/test", true, out error), error);
            Require(!RunFs.DirectoryExists("/run/test"), "recursive delete failed");
            Require(!RunFs.TryDeleteDirectory("/run", true, out error), "runtime root removal accepted");

            Console.WriteLine("RunFs deterministic tests passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
