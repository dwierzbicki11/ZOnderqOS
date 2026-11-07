using System;
using System.IO;
using System.Text;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

static class Program
{
    static int passed;
    static void Check(bool ok, string error) { if (!ok) throw new Exception(error); }
    static void Run(string label, string path, Action setup, bool success, string output)
    {
        Faults.Stage = "";
        PermissionManager.Allowed = true;
        CommandIO.Text.Clear();
        CommandIO.LastCommandSuccess = true;
        ZonderqOS.WriteMessage.Error = null;
        setup();
        string cwd = "/";
        new CmdCat().Execute(path == null ? new[] { "cat" } : new[] { "cat", path }, ref cwd);
        Check(CommandIO.LastCommandSuccess == success, label + ": stale status");
        Check(CommandIO.Text.ToString() == output, label + ": unexpected output");
        if (!success) Check(!string.IsNullOrEmpty(ZonderqOS.WriteMessage.Error), label + ": no error");
        Console.WriteLine("PASS " + label); passed++;
    }
    static void Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "zonderq-read-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "file");
        try
        {
            File.WriteAllText(file, "hello\r\nworld");
            Run("permission backend failure", file, () => Faults.Stage = "permission", false, "");
            Run("path resolution failure", file, () => Faults.Stage = "path", false, "");
            Run("virtual dispatch failure", "/virtual", () => Faults.Stage = "dispatch", false, "");
            Run("virtual read failure", "/virtual", () => Faults.Stage = "read", false, "");
            Run("virtual output failure", "/virtual", () => Faults.Stage = "output", false, "");
            Run("denied read", file, () => PermissionManager.Allowed = false, false, "");
            Run("missing file", file + ".missing", () => {}, false, "");
            Run("usage", null, () => {}, false, "");
            Run("physical text", file, () => {}, true, "hello\nworld");
            Run("virtual text", "/virtual", () => {}, true, "virtual data");
            Run("physical output failure", file, () => Faults.Stage = "output", false, "");
            File.WriteAllText(file, new string('x', 16385));
            Run("overlong line", file, () => {}, false, "");
            File.WriteAllText(file, "");
            Run("empty file", file, () => {}, true, "");
            Console.WriteLine(passed + " storage read checks passed");
        }
        finally { Directory.Delete(folder, true); }
    }
}
namespace ZonderqOS.Commands { interface ICommand { void Execute(string[] args, ref string path); } }
namespace ZonderqOS { static class WriteMessage { public static string Error; public static void WriteError(string text, string category) { Error = text; } } }
namespace ZonderqOS.SystemCore
{
    static class Faults { public static string Stage; public static void At(string stage) { if (Stage == stage) throw new IOException("Injected " + stage + " failure"); } }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) { Faults.At("path"); return path; } }
    static class PermissionManager { public static bool Allowed; public static bool CanRead(string p, string u) { Faults.At("permission"); return Allowed; } }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string kind, string text) {} }
    static class VirtualFs
    {
        public static bool IsVirtualPath(string p) { Faults.At("dispatch"); return p == "/virtual"; }
        public static bool TryRead(string p, out string content) { Faults.At("read"); content = "virtual data"; return true; }
    }
    static class CommandIO
    {
        public static bool LastCommandSuccess;
        public static StringBuilder Text = new StringBuilder();
        public static void Write(string text) { Faults.At("output"); Text.Append(text); }
        public static void WriteLine(string text) { Write(text + "\n"); }
    }
}
