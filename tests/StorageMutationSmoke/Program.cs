using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.SystemCore;

static class Program
{
    static int passed;
    static void Check(bool value, string message) { if (!value) throw new Exception(message); }
    static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "zonderq-mutation-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            foreach (string operation in new[] { "mkdir", "delete", "rmdir" })
            foreach (string fault in new[] { "dispatch", "guard", "virtual", "virtual-reject", "virtual-ok", "reject", "permission", "denied", "log", "" })
            {
                if (operation == "mkdir" && (fault == "permission" || fault == "denied" || fault == "log")) continue;
                string path = Path.Combine(root, "item");
                if (operation == "delete") File.WriteAllText(path, "preserve");
                if (operation == "rmdir") { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "child"), "preserve"); }
                Faults.Stage = fault;
                CommandIO.LastCommandSuccess = true;
                WriteMessage.Error = null;
                if (operation == "mkdir") Disk.CreateDir(path);
                else if (operation == "delete") Disk.DeleteFile(path);
                else Disk.DeleteDir(path);
                bool success = fault == "" || fault == "virtual-ok";
                Check(CommandIO.LastCommandSuccess == success, operation + "/" + fault + ": status");
                Check((WriteMessage.Error != null) == !success, operation + "/" + fault + ": diagnostic");
                bool mutated = fault == "";
                if (operation == "mkdir") Check(Directory.Exists(path) == mutated, "unexpected directory creation");
                else if (operation == "delete") {
                    Check(File.Exists(path) != mutated, "unexpected deletion");
                    if (!mutated) Check(File.ReadAllText(path) == "preserve", "changed bytes");
                } else {
                    Check(Directory.Exists(path) != mutated, "unexpected recursive deletion");
                    if (!mutated) Check(File.ReadAllText(Path.Combine(path, "child")) == "preserve", "changed child bytes");
                }
                if (File.Exists(path)) File.Delete(path);
                if (Directory.Exists(path)) Directory.Delete(path, true);
                passed++;
            }
            // Real filesystem errors, not injected backend results.
            Faults.Stage = "";
            string dir = Path.Combine(root, "nonempty");
            Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "child"), "preserve");
            CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
            Disk.DeleteDir(dir, false);
            Check(!CommandIO.LastCommandSuccess && WriteMessage.Error != null && File.ReadAllText(Path.Combine(dir, "child")) == "preserve", "nonrecursive delete");
            passed++;
            string file = Path.Combine(root, "collision"); File.WriteAllText(file, "preserve");
            CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
            Disk.CreateDir(file);
            Check(!CommandIO.LastCommandSuccess && WriteMessage.Error != null && File.ReadAllText(file) == "preserve", "mkdir over file");
            passed++;
            CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
            Disk.DeleteFile(dir);
            Check(!CommandIO.LastCommandSuccess && WriteMessage.Error != null && Directory.Exists(dir), "delete directory as file");
            passed++;
            foreach (bool move in new[] { false, true })
            foreach (string fault in new[] { "guard", "readonly-source", "readonly-target", "permission", "denied", "log", "collision", "missing", "bad-parent", "" })
            {
                string source = Path.Combine(root, "source");
                string target = Path.Combine(root, "target");
                File.WriteAllBytes(source, new byte[] { 0, 255, 42, 0 });
                if (fault == "missing") File.Delete(source);
                if (fault == "collision") File.WriteAllText(target, "keep destination");
                if (fault == "bad-parent") target = Path.Combine(root, "absent", "target");
                Faults.Stage = fault;
                CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
                if (move) Disk.MoveFile(source, target);
                else Disk.CopyFile(source, target);
                bool success = fault == "";
                Check(CommandIO.LastCommandSuccess == success, "transfer status " + move + "/" + fault);
                Check((WriteMessage.Error != null) == !success, "transfer diagnostic " + fault);
                Check(File.Exists(source) == !(fault == "missing" || (success && move)), "transfer source presence");
                if (File.Exists(source)) Check(Convert.ToHexString(File.ReadAllBytes(source)) == "00FF2A00", "source bytes changed");
                if (success) Check(Convert.ToHexString(File.ReadAllBytes(target)) == "00FF2A00", "binary transfer changed bytes");
                else if (fault == "collision") Check(File.ReadAllText(target) == "keep destination", "collision overwrote destination");
                else Check(!File.Exists(target), "failed transfer created destination");
                if (File.Exists(source)) File.Delete(source);
                if (File.Exists(target)) File.Delete(target);
                passed++;
            }
            Console.WriteLine(passed + " storage mutation checks passed");
        }
        finally { Directory.Delete(root, true); }
    }
}
namespace ZonderqOS
{
    public static partial class Disk
    {
        static bool RejectVirtualMutation(string path, string operation)
        {
            Faults.At("guard");
            if (Faults.Stage != "reject" && !VirtualFs.IsReadOnlyPath(path)) return false;
            WriteMessage.WriteError("Read only", "FS"); return true;
        }
    }
    static class WriteMessage
    {
        public static string Error;
        public static void WriteError(string text, string category) { Error = text; }
        public static void WriteOK(string text, string category) { }
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
    static class RunFs { public static bool IsRunPath(string path) { Faults.At("dispatch"); return Faults.Stage.StartsWith("virtual"); } }
    static class TmpFs { public static bool IsTmpPath(string path) { return false; } }
    static class VirtualFs
    {
        public static bool IsReadOnlyPath(string path)
        {
            Faults.At("guard");
            return (Faults.Stage == "readonly-source" && Path.GetFileName(path) == "source") ||
                (Faults.Stage == "readonly-target" && Path.GetFileName(path) == "target");
        }
        static bool Result(out string error) { Faults.At("virtual"); error = "rejected"; return Faults.Stage == "virtual-ok"; }
        public static bool TryDeleteFile(string path, out string error) { return Result(out error); }
        public static bool TryCreateDirectory(string path, out string error) { return Result(out error); }
        public static bool TryDeleteDirectory(string path, bool recursive, out string error) { return Result(out error); }
    }
    static class PermissionManager
    {
        public static bool CanRead(string path, string user) { return CanWrite(path, user); }
        public static bool CanWrite(string path, string user) { Faults.At("permission"); return Faults.Stage != "denied" && Faults.Stage != "log"; }
    }
    static class SecurityContext { public static string CurrentUser = "test"; }
    static class SecurityLogger { public static void LogEvent(string kind, string text) { Faults.At("log"); } }
}
