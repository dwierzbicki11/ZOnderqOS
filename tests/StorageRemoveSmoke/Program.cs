using System;
using System.IO;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

static class Program
{
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        string folder = Path.Combine(Path.GetTempPath(), "zonderq-remove-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        string file = Path.Combine(folder, "file");
        try
        {
            foreach (string fault in new[] { "path", "dispatch", "permission", "denied", "backend", "deleted-failure", "metadata", "" })
            {
                File.WriteAllText(file, "preserve");
                Faults.Stage = fault; Faults.MetadataCalls = 0;
                CommandIO.LastCommandSuccess = true; ZonderqOS.WriteMessage.Error = null;
                string cwd = "/";
                new CmdRm().Execute(new[] { "rm", file }, ref cwd);
                Check(CommandIO.LastCommandSuccess == (fault == ""), fault + ": wrong status");
                bool deleted = fault == "" || fault == "metadata" || fault == "deleted-failure";
                Check(File.Exists(file) != deleted, fault + ": wrong file state");
                if (!deleted) Check(File.ReadAllText(file) == "preserve", "source modified");
                Check(Faults.MetadataCalls == (fault == "" || fault == "metadata" ? 1 : 0), fault + ": unexpected metadata deletion");
                if (fault != "") Check(ZonderqOS.WriteMessage.Error != null, fault + ": missing error");
                passed++;
            }
            foreach (string path in new[] { "/virtual", "/ram", file + ".missing", null })
            {
                Faults.Stage = ""; Faults.MetadataCalls = 0; CommandIO.LastCommandSuccess = true;
                ZonderqOS.WriteMessage.Error = null; string cwd = "/";
                new CmdRm().Execute(path == null ? new[] { "rm" } : new[] { "rm", path }, ref cwd);
                Check(!CommandIO.LastCommandSuccess && ZonderqOS.WriteMessage.Error != null, "rejected path status");
                Check(Faults.MetadataCalls == 0, "metadata touched on rejected path"); passed++;
            }
            Console.WriteLine(passed + " remove command checks passed");
        }
        finally { Directory.Delete(folder, true); }
    }
}
namespace ZonderqOS.Commands { interface ICommand { void Execute(string[] args, ref string path); } }
namespace ZonderqOS
{
    static class WriteMessage { public static string Error; public static void WriteError(string text, string category) { Error = text; } }
    static class Disk
    {
        public static void DeleteFile(string path)
        {
            CommandIO.LastCommandSuccess = false;
            Faults.At("backend");
            if (path != "/ram") File.Delete(path);
            if (Faults.Stage == "deleted-failure" || path == "/ram")
            { WriteMessage.WriteError("Backend reported deletion failure", "FS"); return; }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
namespace ZonderqOS.SystemCore
{
    static class Faults { public static string Stage; public static int MetadataCalls; public static void At(string stage) { if (Stage == stage) throw new IOException("Injected " + stage); } }
    static class PathResolver { public static string GetAbsolutePath(string cwd, string path) { Faults.At("path"); return path; } }
    static class PermissionManager
    {
        public static bool CanWrite(string p, string u) { Faults.At("permission"); return Faults.Stage != "denied"; }
        public static void RemovePermission(string p) { Faults.MetadataCalls++; Faults.At("metadata"); }
    }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string kind, string text) { } }
    static class RunFs { public static bool IsRunPath(string p) { return p == "/ram"; } }
    static class TmpFs { public static bool IsTmpPath(string p) { return false; } }
    static class VirtualFs { public static bool IsVirtualPath(string p) { Faults.At("dispatch"); return p == "/virtual"; } }
    static class CommandIO { public static bool LastCommandSuccess; }
}
