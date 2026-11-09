using System;
using ZonderqOS.SystemCore;

static class Program
{
    static bool tmp;
    static string root;
    static int passed;
    static bool Write(string p, string s, bool append, out string error) => tmp
        ? TmpFs.TryWrite(p, s, append, out error) : RunFs.TryWrite(p, s, append, out error);
    static bool Read(string p, out string s) => tmp ? TmpFs.TryRead(p, out s) : RunFs.TryRead(p, out s);
    static void Check(bool ok, string message) { if (!ok) throw new Exception(message); }
    static void Reset()
    {
        if (tmp) { TmpFs.TryDeleteDirectory(root, true, out _); Check(TmpFs.TryCreateDirectory(root, out _), "mkdir"); }
        else { RunFs.TryDeleteDirectory(root, true, out _); Check(RunFs.TryCreateDirectory(root, out _), "mkdir"); }
    }
    static void Pass(string name) { passed++; Console.WriteLine("PASS " + root + " " + name); }
    static void Reject(string path, string extra, string expected, string errorPart)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool result = Write(path, extra, true, out string error);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Check(!result && error != null && error.Contains(errorPart), "wrong rejection: " + error);
        Check(allocated < 16 * 1024, "rejected append allocated content buffer: " + allocated);
        Check(Read(path, out string actual) && actual == expected, "rejected append changed content");
    }
    static void Main()
    {
        foreach (bool value in new[] { true, false })
        {
            tmp = value;
            root = tmp ? "/tmp/quota" : "/run/quota";
            int fileLimit = tmp ? TmpFs.MaxFileChars : RunFs.MaxFileChars;
            int totalLimit = tmp ? 2 * 1024 * 1024 : 512 * 1024;
            string path = root + "/file";
            Reset();
            string full = new string('x', fileLimit);
            Check(Write(path, full, false, out _), "fill file");
            Reject(path, "x", full, "file exceeds");
            Pass("file limit rejects before concatenation");
            Reject(path, new string('y', fileLimit * 4), full, "file exceeds");
            Pass("oversized append rejects before concatenation");
            Check(Write(path, null, true, out _) && Read(path, out string text) && text == full, "null append");
            Pass("null append at limit");
            Check(Write(path, full.Substring(1), false, out _), "replace shorter");
            Check(Write(path, "x", true, out _) && Read(path, out text) && text == full, "exact boundary");
            Pass("append exactly to file limit");
            Reset();
            string half = new string('h', fileLimit / 2);
            Check(Write(path, half, false, out _), "half file");
            Check(Write(root + "/half", half, false, out _), "second half");
            for (int i = 1; i < totalLimit / fileLimit; i++)
                Check(Write(root + "/fill" + i, full, false, out _), "fill total quota");
            Reject(path, "x", half, "memory limit");
            Pass("aggregate limit rejects before concatenation");
            Check(!Write(root + "/extra", "x", false, out _), "rejection changed total accounting");
            Pass("rejection preserves aggregate quota");
            Check(Write(path, "", false, out _), "overwrite should release quota");
            Check(Write(root + "/extra", half, true, out _) && Read(root + "/extra", out text) && text == half, "new append or released quota failed");
            Check(!Write(path, "x", true, out _), "released quota incorrectly accounted");
            Pass("overwrite releases exact quota and append creates file");
            Reset();
        }
        Console.WriteLine(passed + " RAM write quota checks passed");
    }
}
