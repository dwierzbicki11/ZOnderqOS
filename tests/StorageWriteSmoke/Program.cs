using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.SystemCore;

static class Program
{
    static int passed;
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zonderq-write-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        string file = Path.Combine(dir, "file");
        try
        {
            foreach (bool append in new[] { false, true })
            {
                Action<string> write = p => { if (append) Disk.AppendFile(p, "new"); else Disk.CreateFile(p, "new"); };
                foreach (string fault in new[] { "dispatch", "permission", "denied", "log", "virtual", "quota", "" })
                {
                    File.WriteAllText(file, "original");
                    Faults.Stage = fault; WriteMessage.Error = null; CommandIO.LastCommandSuccess = true;
                    bool virtualPath = fault == "virtual" || fault == "quota";
                    write(virtualPath ? "/virtual" : file);
                    bool success = fault == "";
                    Check(CommandIO.LastCommandSuccess == success, fault + ": status");
                    Check(success || !string.IsNullOrEmpty(WriteMessage.Error), fault + ": missing error");
                    Check(File.ReadAllText(file) == (success ? (append ? "originalnew\n" : "new") : "original"), fault + ": bytes changed");
                    passed++;
                }
                Faults.Stage = ""; WriteMessage.Error = null; CommandIO.LastCommandSuccess = true;
                write(Path.Combine(dir, "missing", "file"));
                Check(!CommandIO.LastCommandSuccess && WriteMessage.Error != null, "physical I/O failure"); passed++;
                CommandIO.LastCommandSuccess = false;
                write("/virtual"); Check(CommandIO.LastCommandSuccess, "virtual success"); passed++;
                if (!append)
                {
                    File.WriteAllText(file, "original"); Faults.Stage = "metadata"; WriteMessage.Error = null;
                    write(file);
                    Check(!CommandIO.LastCommandSuccess && WriteMessage.Error != null && File.ReadAllText(file) == "new",
                          "metadata failure must retain written data and report failure"); passed++;
                }
            }
            Console.WriteLine(passed + " storage write checks passed");
        }
        finally { Directory.Delete(dir, true); }
    }
}
namespace ZonderqOS
{
    static class WriteMessage
    {
        public static string Error;
        public static void WriteError(string text, string kind) { Error = text; }
        public static void WriteOK(string text, string kind) { }
    }
}
namespace ZonderqOS.SystemCore
{
    static class Faults
    {
        public static string Stage;
        public static void At(string stage) { if (Stage == stage) throw new IOException("Injected " + stage); }
    }
    static class CommandIO { public static bool LastCommandSuccess; }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string level, string text) { Faults.At("log"); } }
    sealed class Permission { public string Owner = "test"; }
    static class PermissionManager
    {
        public static bool CanWrite(string path, string user) { Faults.At("permission"); return Faults.Stage != "denied" && Faults.Stage != "log"; }
        public static Permission GetPermission(string path) { Faults.At("metadata"); return new Permission(); }
        public static void SetPermission(string path, string user, int mode) { }
    }
    static class VirtualFs
    {
        public static bool IsVirtualPath(string path) { Faults.At("dispatch"); return path == "/virtual"; }
        public static bool TryWrite(string path, string text, bool append, out string error)
        { Faults.At("virtual"); error = Faults.Stage == "quota" ? "No space" : null; return error == null; }
    }
}
