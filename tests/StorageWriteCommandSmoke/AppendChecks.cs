using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.Commands;
static class AppendChecks
{
    public static int Run(string dir)
    {
        int count = 0;
        foreach (bool existing in new[] { false, true })
        foreach (string fault in new[] { "path", "backend", "partial", "metadata", "" })
        {
            string file = Path.Combine(dir, "append");
            if (existing) File.WriteAllText(file, "old");
            Faults.Stage = fault; Faults.MetadataCalls = 0;
            WriteMessage.Error = null; CommandIO.LastCommandSuccess = true;
            string cwd = dir;
            new CmdAppend().Execute(new[] { "append", file, "new" }, ref cwd);
            bool success = fault == "" || (existing && fault == "metadata");
            if (CommandIO.LastCommandSuccess != success || (WriteMessage.Error != null) == success)
                throw new Exception("append status/diagnostic " + existing + "/" + fault);
            bool wrote = fault != "path" && fault != "backend";
            if (File.Exists(file) != (existing || wrote)) throw new Exception("append file presence");
            if (File.Exists(file) && File.ReadAllText(file) != (existing ? "old" : "") + (wrote ? fault == "partial" ? "partial" : "new\n" : ""))
                throw new Exception("append contents");
            int calls = !existing && (fault == "" || fault == "metadata") ? 1 : 0;
            if (Faults.MetadataCalls != calls) throw new Exception("append metadata after failure");
            if (File.Exists(file)) File.Delete(file);
            count++;
        }
        return count;
    }
}
