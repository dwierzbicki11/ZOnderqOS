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

            // Copy/move must enforce the same destination rules as TryWrite.
            string[] invalidTargets = {
                "/run/test/.", "/run/test/..", "/run/test/" + new string('x', 193),
                "/outside/file", "/run", "/run/missing/file"
            };
            foreach (string target in invalidTargets)
            {
                Require(!RunFs.TryCopyFile("/run/test/state", target, out error), "invalid copy target accepted: " + target);
                Require(!string.IsNullOrEmpty(error), "copy failure omitted reason");
                Require(!RunFs.TryMoveFile("/run/test/state", target, out error), "invalid move target accepted: " + target);
                Require(!string.IsNullOrEmpty(error), "move failure omitted reason");
                Require(RunFs.TryRead("/run/test/state", out content) && content == "ready\nok", "rejected transfer changed source");
                Require(!RunFs.FileExists(target), "rejected transfer created destination");
            }
            Require(RunFs.TryCopyFile("/run/test/state", "\\run\\test\\normalized", out error), error);
            Require(RunFs.TryRead("/run/test/normalized", out content) && content == "ready\nok", "normalized copy failed");

            string longestAllowed = "/run/test/" + new string('b', 192 - "/run/test/".Length);
            Require(RunFs.TryCopyFile("/run/test/state", longestAllowed, out error), error);
            Require(!RunFs.TryMoveFile("/run/test/state", longestAllowed + "x", out error), "193-character target accepted");
            Require(RunFs.TryRead(longestAllowed, out content) && content == "ready\nok", "boundary copy changed content");
            Require(RunFs.TryMoveFile(longestAllowed, "/run//test//renamed/", out error), error);
            Require(!RunFs.FileExists(longestAllowed), "normalized move left source");
            Require(RunFs.TryRead("/run/test/renamed", out content) && content == "ready\nok", "normalized move lost content");

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
