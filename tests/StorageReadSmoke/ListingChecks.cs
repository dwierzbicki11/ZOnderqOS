using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.Commands;
using ZonderqOS.SystemCore;

static class ListingChecks
{
    public static int Run(string root)
    {
        string tree = Path.Combine(root, "tree");
        Directory.CreateDirectory(Path.Combine(tree, "child"));
        File.WriteAllText(Path.Combine(tree, "child", "leaf"), "untouched");
        int count = 0;
        foreach (string fault in new[] { "", "path", "dispatch", "output", "nested", "absent" })
        {
            Run(tree, fault, fault == "", false);
            if (File.ReadAllText(Path.Combine(tree, "child", "leaf")) != "untouched") throw new Exception("listing mutated data");
            if (fault == "" && CommandIO.Text.ToString() != "+-- child\n    --- leaf\n") throw new Exception("wrong tree output");
            count++;
        }
        foreach (string fault in new[] { "", "dispatch", "list", "missing", "output" })
        {
            Run("/virtual", fault, fault == "", false);
            if (fault == "" && CommandIO.Text.ToString() != "entry\n") throw new Exception("wrong virtual listing");
            count++;
        }
        Run(tree, "", true, true); count++;
        return count;
    }
    static void Run(string path, string fault, bool success, bool implicitPath)
    {
        Faults.Stage = fault;
        CommandIO.Text.Clear(); CommandIO.LastCommandSuccess = !success; WriteMessage.Error = null;
        string cwd = path;
        new CmdLs().Execute(implicitPath ? new[] { "ls" } : new[] { "ls", fault == "absent" ? path + ".missing" : path }, ref cwd);
        if (CommandIO.LastCommandSuccess != success || (WriteMessage.Error != null) == success)
            throw new Exception("listing status/diagnostic: " + fault);
    }
}
