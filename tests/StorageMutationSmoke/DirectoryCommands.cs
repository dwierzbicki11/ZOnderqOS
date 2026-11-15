using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

static class DirectoryCommands
{
    public static int Check(string root)
    {
        int count = 0;
        foreach (bool remove in new[] { false, true })
        foreach (string fault in new[] { "path", "guard", "reject", "usage", "io", "" })
        {
            string path = Path.Combine(root, "command-dir");
            if (remove) { Directory.CreateDirectory(path); File.WriteAllText(Path.Combine(path, "child"), "preserve"); }
            else if (fault == "io") File.WriteAllText(path, "preserve");
            Faults.Stage = fault; CommandIO.LastCommandSuccess = true; WriteMessage.Error = null;
            string cwd = root;
            ICommand command = remove ? new CmdRmdir() : new CmdMkdir();
            // For rmdir use a regular file to provoke a real directory backend error.
            string target = remove && fault == "io" ? Path.Combine(path, "child") : path;
            command.Execute(fault == "usage" ? new[] { command.Name } : new[] { command.Name, target }, ref cwd);
            bool success = fault == "";
            if (CommandIO.LastCommandSuccess != success || (WriteMessage.Error != null) == success)
                throw new Exception(command.Name + "/" + fault + ": wrong result");
            if (remove) {
                if (Directory.Exists(path) == success) throw new Exception("wrong directory state");
                if (!success && File.ReadAllText(Path.Combine(path, "child")) != "preserve") throw new Exception("child changed");
            } else if (fault == "io") {
                if (File.ReadAllText(path) != "preserve") throw new Exception("collision changed");
            } else if (Directory.Exists(path) != success) throw new Exception("unexpected directory");
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
            count++;
        }
        return count;
    }
}
namespace ZonderqOS
{
    static class PathResolver
    {
        public static string GetAbsolutePath(string current, string path) { Faults.At("path"); return path; }
    }
}
