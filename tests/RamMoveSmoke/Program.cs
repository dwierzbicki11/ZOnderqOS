// Real command and RAM filesystems; other storage boundaries must not be used.
using System;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

internal static class Program
{
    static bool tmp;
    static int passed;
    static string root;
    static bool Write(string p, string value) => tmp ? TmpFs.TryWrite(p, value, false, out _) : RunFs.TryWrite(p, value, false, out _);
    static bool Read(string p, out string value) => tmp ? TmpFs.TryRead(p, out value) : RunFs.TryRead(p, out value);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Move(string source, string target, bool success)
    {
        string cwd = "/";
        CommandIO.LastCommandSuccess = !success;
        WriteMessage.LastError = null;
        new CmdMv().Execute(new[] { "mv", source, target }, ref cwd);
        Check(CommandIO.LastCommandSuccess == success, "unexpected mv status: " + source + " -> " + target + ": " + WriteMessage.LastError);
        if (!success) Check(!string.IsNullOrEmpty(WriteMessage.LastError), "missing failure reason");
        passed++;
    }
    static void Reset()
    {
        if (tmp) { TmpFs.TryDeleteDirectory(root, true, out _); Check(TmpFs.TryCreateDirectory(root, out _), "mkdir"); }
        else { RunFs.TryDeleteDirectory(root, true, out _); Check(RunFs.TryCreateDirectory(root, out _), "mkdir"); }
    }
    static void Main()
    {
        foreach (bool isTmp in new[] { true, false })
        {
            tmp = isTmp;
            root = tmp ? "/tmp/move-test" : "/run/move-test";
            Reset();
            int fileChars = tmp ? 128 * 1024 : 64 * 1024;
            int totalChars = tmp ? 2 * 1024 * 1024 : 512 * 1024;
            string payload = new string('x', fileChars);
            for (int i = 0; i < totalChars / fileChars; i++) Check(Write(root + "/f" + i, payload), "fill memory");
            Check(!Write(root + "/extra", "x"), "quota not full");
            Move(root + "/f0", root + "/renamed", true);
            Check(!Read(root + "/f0", out _) && Read(root + "/renamed", out string value) && value == payload, "full-volume rename lost data");
            Check(!Write(root + "/extra", "x"), "rename corrupted quota");
            Move(root + "/renamed", root + "/f1", false);
            Check(Read(root + "/renamed", out value) && value == payload && Read(root + "/f1", out value) && value == payload, "collision changed data");
            Reset();
            int count = 0;
            while (Write(root + "/f" + count, "")) { count++; Check(count <= 512, "unbounded entries"); }
            Check(count > 200, "unexpected capacity");
            Move(root + "/f0", root + "/renamed", true);
            Check(!Read(root + "/f0", out _) && Read(root + "/renamed", out value) && value == "", "full-entry rename lost data");
            Check(!Write(root + "/extra", ""), "rename changed entry accounting");
            Reset();
            Check(Write(root + "/source", "original"), "source write");
            Move(root + "/source", root + "/missing/target", false);
            Move(root + "/source", root + "/" + new string('x', 193), false);
            Move(root + "/missing", root + "/target", false);
            Move(root + "/source", root + "/source", false);
            Check(Read(root + "/source", out value) && value == "original", "rejected rename lost source");
            Move(root + "/source", root + "/target", true);
            Check(!Read(root + "/source", out _) && Read(root + "/target", out value) && value == "original", "ordinary rename failed");
            Reset();
        }
        Console.WriteLine(passed + " RAM command behavior checks passed");
    }
}
namespace ZonderqOS.SystemCore
{
    static class CommandIO { public static bool LastCommandSuccess; }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string level, string text) {} }
    static class PermissionManager
    {
        public static bool CanRead(string p, string u) => true;
        public static bool CanWrite(string p, string u) => true;
        public static void RemovePermission(string p) { throw new Exception("unexpected metadata removal"); }
        public static void MovePermissionsUnder(string a, string b) { throw new Exception("unexpected metadata move"); }
    }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) => path; }
    static class VirtualFs
    {
        public static bool IsVirtualPath(string p) => TmpFs.IsTmpPath(p) || RunFs.IsRunPath(p);
        public static bool TryRead(string p, out string value) => TmpFs.IsTmpPath(p) ? TmpFs.TryRead(p, out value) : RunFs.TryRead(p, out value);
    }
}
namespace ZonderqOS
{
    static class WriteMessage { public static string LastError; public static void WriteError(string text, string category) { LastError = text; } }
    static class Disk
    {
        public static void CreateFile(string p, string text)
        {
            CommandIO.LastCommandSuccess = TmpFs.IsTmpPath(p)
                ? TmpFs.TryWrite(p, text, false, out _)
                : RunFs.TryWrite(p, text, false, out _);
        }
        public static void DeleteFile(string p) { throw new Exception("same-volume rename tried separate delete"); }
        public static void MoveFile(string a, string b) { throw new Exception("unexpected physical move"); }
    }
}
namespace ZonderqOS.Commands { interface ICommand { void Execute(string[] args, ref string cwd); } }
