using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.Commands;
static class Program
{
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Main()
    {
        string dir = Path.Combine(Path.GetTempPath(), "zonderq-write-command-" + Guid.NewGuid());
        Directory.CreateDirectory(dir); int count = 0;
        try
        {
            foreach (bool touch in new[] { false, true })
            foreach (string fault in new[] { "path", "backend", "partial", "metadata", "" })
            {
                string file = Path.Combine(dir, "file");
                Faults.Stage = fault; Faults.MetadataCalls = 0; CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
                string cwd = dir; ICommand command = touch ? new CmdTouch() : new CmdWrite();
                command.Execute(new[] { command.Name, file, "hello" }, ref cwd);
                Check(CommandIO.LastCommandSuccess == (fault == ""), "wrong status " + touch + "/" + fault);
                Check((WriteMessage.Error != null) == (fault != ""), "missing diagnostic");
                bool exists = fault != "path" && fault != "backend";
                Check(File.Exists(file) == exists, "unexpected file state");
                if (exists) Check(File.ReadAllText(file) == (fault == "partial" ? "partial" : touch ? "" : "hello"), "wrong data");
                Check(Faults.MetadataCalls == (fault == "" || fault == "metadata" ? 1 : 0), "metadata after backend failure");
                if (exists) File.Delete(file); count++;
            }
            foreach (bool touch in new[] { false, true })
            foreach (string fault in new[] { "permission", "denied", "log", "backend", "partial", "" })
            {
                string file = Path.Combine(dir, "file"); File.WriteAllText(file, "original");
                Faults.Stage = fault; Faults.MetadataCalls = 0; CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
                string cwd = dir; ICommand command = touch ? new CmdTouch() : new CmdWrite();
                command.Execute(new[] { command.Name, file, "hello" }, ref cwd);
                bool success = fault == "" || (touch && (fault == "backend" || fault == "partial"));
                Check(CommandIO.LastCommandSuccess == success, "existing status");
                Check((WriteMessage.Error != null) != success, "existing diagnostic");
                string expected = touch || fault == "permission" || fault == "denied" || fault == "log" || fault == "backend" ? "original" : fault == "partial" ? "partial" : "hello";
                Check(File.ReadAllText(file) == expected, "existing bytes");
                Check(Faults.MetadataCalls == 0, "unexpected metadata");
                File.Delete(file); count++;
            }
            Console.WriteLine(count + " write command checks passed");
        }
        finally { Directory.Delete(dir, true); }
    }
}
namespace ZonderqOS
{
    static class Faults { public static string Stage; public static int MetadataCalls; public static void At(string stage) { if (Stage == stage) throw new IOException("Injected " + stage); } }
    static class CommandIO { public static bool LastCommandSuccess; }
    static class WriteMessage { public static string Error; public static void WriteError(string text, string category) { Error = text; } }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) { Faults.At("path"); return path; } }
    static class Disk
    {
        public static void CreateFile(string path, string content)
        {
            CommandIO.LastCommandSuccess = false;
            Faults.At("backend");
            File.WriteAllText(path, Faults.Stage == "partial" ? "partial" : content);
            if (Faults.Stage == "partial") { WriteMessage.WriteError("Injected write failure", "FS"); return; }
            CommandIO.LastCommandSuccess = true;
        }
    }
    static class PermissionManager
    {
        public static bool CanWrite(string path, string user) { Faults.At("permission"); return Faults.Stage != "denied" && Faults.Stage != "log"; }
        public static void SetPermission(string path, string user, int mode) { Faults.MetadataCalls++; Faults.At("metadata"); }
    }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string type, string message) { Faults.At("log"); } }
}
namespace ZonderqOS.SystemCore
{
    static class RunFs { public static bool IsRunPath(string p) => false; public static bool DirectoryExists(string p) => false; public static bool FileExists(string p) => false; }
    static class TmpFs { public static bool IsTmpPath(string p) => false; public static bool DirectoryExists(string p) => false; public static bool FileExists(string p) => false; }
    static class VirtualFs { public static bool IsVirtualPath(string p) => false; }
}
