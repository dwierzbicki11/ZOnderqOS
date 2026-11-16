using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

static class NavigationChecks
{
    public static int Run(string root)
    {
        string target = Path.Combine(root, "navigation");
        Directory.CreateDirectory(target);
        int count = 0;
        foreach (string fault in new[] { "", "path", "dispatch", "absent", "file", "usage" })
        {
            string file = Path.Combine(target, "file");
            File.WriteAllText(file, "unchanged");
            string path = fault == "absent" ? target + ".missing" : fault == "file" ? file : target;
            Run(root, path, fault, fault == "", fault == "usage");
            if (File.ReadAllText(file) != "unchanged") throw new Exception("navigation changed data");
            count++;
        }
        foreach (string fault in new[] { "", "path", "dispatch", "list", "missing" })
        {
            Run(root, "/virtual", fault, fault == "", false); count++;
        }
        Run(root, "/", "", true, false); count++;
        return count;
    }
    static void Run(string original, string target, string fault, bool success, bool usage)
    {
        Faults.Stage = fault; WriteMessage.Error = null;
        CommandIO.LastCommandSuccess = !success;
        string cwd = original;
        new CmdCd().Execute(usage ? new[] { "cd" } : new[] { "cd", target }, ref cwd);
        if (CommandIO.LastCommandSuccess != success || (WriteMessage.Error != null) == success)
            throw new Exception("navigation status/diagnostic: " + fault);
        if (cwd != (success ? target : original)) throw new Exception("wrong current directory: " + fault);
    }
}
