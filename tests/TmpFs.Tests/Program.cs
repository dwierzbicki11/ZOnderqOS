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
            Require(TmpFs.DirectoryExists("/tmp"), "tmp root missing");

            Require(TmpFs.TryCreateDirectory("/tmp/test", out string error), error);
            Require(TmpFs.TryWrite("/tmp/test/state", "ready", false, out error), error);
            Require(TmpFs.TryWrite("/tmp/test/state", "\nok", true, out error), error);
            Require(TmpFs.TryRead("/tmp/test/state", out string content), "read failed");
            Require(content == "ready\nok", "append semantics mismatch");

            Require(TmpFs.TryCopyFile("/tmp/test/state", "/tmp/test/copy", out error), error);
            Require(TmpFs.TryMoveFile("/tmp/test/copy", "/tmp/test/moved", out error), error);
            Require(!TmpFs.FileExists("/tmp/test/copy"), "move left source behind");
            Require(TmpFs.FileExists("/tmp/test/moved"), "move lost destination");

            Require(!TmpFs.TryCreateDirectory("/tmp/missing/child", out error), "missing parent accepted");
            Require(!TmpFs.TryWrite("/tmp/test/too-big", new string('x', 128 * 1024 + 1), false, out error), "oversized file accepted");
            Require(!TmpFs.TryDeleteDirectory("/tmp/test", false, out error), "non-empty directory removed without recursion");
            Require(TmpFs.TryDeleteDirectory("/tmp/test", true, out error), error);
            Require(!TmpFs.DirectoryExists("/tmp/test"), "recursive delete failed");
            Require(!TmpFs.TryDeleteDirectory("/tmp", true, out error), "tmp root removal accepted");

            Console.WriteLine("TmpFs deterministic tests passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
