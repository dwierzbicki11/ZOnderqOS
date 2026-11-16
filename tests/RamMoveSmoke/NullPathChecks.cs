using System;
using ZonderqOS.SystemCore;

static class NullPathChecks
{
    public static int Run()
    {
        int passed = 0;
        foreach (bool tmp in new[] { true, false })
        {
            string root = tmp ? "/tmp/nul-test" : "/run/nul-test";
            string source = root + "/source", invalid = root + "/bad\0name";
            string error;
            bool created = tmp ? TmpFs.TryCreateDirectory(root, out error) : RunFs.TryCreateDirectory(root, out error);
            if (!created) throw new Exception(error);
            try
            {
                bool wrote = tmp ? TmpFs.TryWrite(source, "original", false, out error) : RunFs.TryWrite(source, "original", false, out error);
                if (!wrote) throw new Exception(error);
                foreach (string operation in new[] { "write", "append", "mkdir", "copy", "move" })
                {
                    bool ok;
                    switch (operation)
                    {
                        case "write": case "append":
                            ok = tmp ? TmpFs.TryWrite(invalid, "payload", operation == "append", out error) : RunFs.TryWrite(invalid, "payload", operation == "append", out error); break;
                        case "mkdir":
                            ok = tmp ? TmpFs.TryCreateDirectory(invalid, out error) : RunFs.TryCreateDirectory(invalid, out error); break;
                        case "copy":
                            ok = tmp ? TmpFs.TryCopyFile(source, invalid, out error) : RunFs.TryCopyFile(source, invalid, out error); break;
                        default:
                            ok = tmp ? TmpFs.TryMoveFile(source, invalid, out error) : RunFs.TryMoveFile(source, invalid, out error); break;
                    }
                    if (ok || string.IsNullOrEmpty(error)) throw new Exception("NUL path accepted: " + operation);
                    string value;
                    bool read = tmp ? TmpFs.TryRead(source, out value) : RunFs.TryRead(source, out value);
                    if (!read || value != "original") throw new Exception("source changed after rejected " + operation);
                    string[] entries;
                    bool listed = tmp ? TmpFs.TryList(root, out entries) : RunFs.TryList(root, out entries);
                    if (!listed || entries.Length != 1) throw new Exception("rejected operation changed entries");
                    passed++;
                }
            }
            finally
            {
                if (tmp) TmpFs.TryDeleteDirectory(root, true, out _); else RunFs.TryDeleteDirectory(root, true, out _);
            }
        }
        return passed;
    }
}
