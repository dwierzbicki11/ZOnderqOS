using System;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;
static class Program
{
    static int passed;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Write(string path, string value, bool success, bool append = false)
    {
        string cwd = "/";
        CommandIO.LastCommandSuccess = !success; WriteMessage.Error = null;
        (append ? (ICommand)new CmdAppend() : new CmdWrite()).Execute(new[] { "write", path, value }, ref cwd);
        Check(CommandIO.LastCommandSuccess == success, "status " + path + ": " + WriteMessage.Error);
        Check((WriteMessage.Error != null) != success, "diagnostic " + path);
        passed++;
    }
    static void Main()
    {
        foreach (bool tmp in new[] { true, false })
        {
            string root = tmp ? "/tmp/write-command" : "/run/write-command";
            bool made = tmp ? TmpFs.TryCreateDirectory(root, out _) : RunFs.TryCreateDirectory(root, out _);
            Check(made, "root");
            string file = root + "/file";
            Write(file, "hello\\nworld", true);
            Check(VirtualFs.TryRead(file, out string value) && value == "hello\nworld", "newline");
            Write(file, "tail", true, true);
            Check(VirtualFs.TryRead(file, out value) && value == "hello\nworldtail\n", "append contents");
            Write(root + "/missing/file", "x", false, true);
            Write(root, "x", false, true);
            Write(file, new string('x', 65537), false, true);
            Write(file, "replacement", true);
            Check(VirtualFs.TryRead(file, out value) && value == "replacement", "overwrite");
            Write(root, "directory", false);
            Write(root + "/missing/file", "missing", false);
            Write(file, new string('x', 65537), false);
            Check(VirtualFs.TryRead(file, out value) && value == "replacement", "oversize changed source");
            // Fill the actual aggregate quota, leaving the existing file intact.
            int capacity = tmp ? 2 * 1024 * 1024 : 512 * 1024;
            int remaining = capacity - "replacement".Length;
            int index = 0;
            while (remaining > 0)
            {
                int size = Math.Min(65536, remaining);
                Check(VirtualFs.TryWrite(root + "/fill" + index++, new string('x', size), false, out _), "fill");
                remaining -= size;
            }
            Write(root + "/extra", "x", false);
            Write(file, "replacement larger", false);
            Write(file, "x", false, true);
            Check(VirtualFs.TryRead(file, out value) && value == "replacement", "quota changed old data");
            Write(file, "", true);
            Check(VirtualFs.TryRead(file, out value) && value == "", "truncate to empty");
            Write(root + "/extra", "x", true);
            if (tmp) TmpFs.TryDeleteDirectory(root, true, out _); else RunFs.TryDeleteDirectory(root, true, out _);
        }
        Write("/proc/forbidden", "x", false);
        Write("/proc/forbidden", "x", false, true);
        Console.WriteLine(passed + " RAM write command checks passed");
    }
}
namespace ZonderqOS
{
    static class CommandIO { public static bool LastCommandSuccess; }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) => path; }
    static class WriteMessage { public static string Error; public static void WriteError(string text, string category) { Error = text; } public static void WriteOK(string text, string category) {} }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string type, string text) { throw new Exception("physical security log"); } }
    static class PermissionManager
    {
        public class Permission { public string Owner; }
        public static bool CanWrite(string path, string user) { throw new Exception("physical ACL"); }
        public static Permission GetPermission(string path) { throw new Exception("physical metadata read"); }
        public static void SetPermission(string path, string user, int mode) { throw new Exception("physical metadata write"); }
    }
}
namespace ZonderqOS.SystemCore
{
    static class VirtualFs
    {
        public static bool IsVirtualPath(string p) => TmpFs.IsTmpPath(p) || RunFs.IsRunPath(p) || p.StartsWith("/proc/");
        public static bool TryRead(string p, out string value) => TmpFs.IsTmpPath(p) ? TmpFs.TryRead(p, out value) : RunFs.TryRead(p, out value);
        public static bool TryWrite(string p, string value, bool append, out string error)
        {
            if (TmpFs.IsTmpPath(p)) return TmpFs.TryWrite(p, value, append, out error);
            if (RunFs.IsRunPath(p)) return RunFs.TryWrite(p, value, append, out error);
            error = "read only"; return false;
        }
    }
}
