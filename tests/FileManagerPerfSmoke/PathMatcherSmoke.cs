using System;
using System.Diagnostics;
using ZonderqOS.GUI.Apps;

internal static class PathMatcherSmoke
{
    private static int checks;
    private static long sink;

    private static bool Legacy(string currentPath, string entryPath)
    {
        if (string.IsNullOrEmpty(entryPath)) return false;
        if (entryPath == "/") return currentPath == "/";
        if (currentPath == entryPath) return true;
        return currentPath.StartsWith(entryPath + "/", StringComparison.Ordinal);
    }

    private static void Check(string currentPath, string entryPath)
    {
        bool expected = Legacy(currentPath, entryPath);
        bool actual = FileManagerPathMatcher.IsActive(currentPath, entryPath);
        if (expected != actual)
            throw new Exception("path mismatch current=" + currentPath + " entry=" + entryPath);
        checks++;
    }

    private static void Verify()
    {
        string[] paths =
        {
            "/", "/root", "/root/", "/root/sub", "/root2", "/ROOT/sub",
            "/mnt", "/mnt/data", "/mnt/database", "/mnt/data/folder",
            "/mnt/data//folder", "/home/ą", "/home/ą/a", "/a/b/c",
            "/a/b/c/", "relative", "relative/path", "", "/mnt/volume"
        };
        string[] roots =
        {
            null, "", "/", "/root", "/root/", "/ROOT", "/mnt", "/mnt/data",
            "/mnt/database", "/home/ą", "/a", "/a/b", "/a/b/c", "relative"
        };
        foreach (string current in paths)
            foreach (string root in roots)
                Check(current, root);

        var random = new Random(3095);
        for (int i = 0; i < 20000; i++)
        {
            string current = paths[random.Next(paths.Length)];
            string root = roots[random.Next(roots.Length)];
            Check(current, root);
        }

        for (int i = 0; i < 1000; i++)
            if (FileManagerPathMatcher.IsActive("/mnt/data/folder", "/mnt/data")) sink++;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            if (FileManagerPathMatcher.IsActive("/mnt/data/folder", "/mnt/data")) sink++;
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            throw new Exception("path matcher allocated " + bytes + " bytes");
        checks++;
        Console.WriteLine("PASS: production path-prefix equivalence " + checks +
            " checks; warm allocations=" + bytes + " B");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double Time(bool optimized, string current, string[] roots)
    {
        const int frames = 10000;
        long start = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < frames; frame++)
            for (int i = 0; i < roots.Length; i++)
                if (optimized
                    ? FileManagerPathMatcher.IsActive(current, roots[i])
                    : Legacy(current, roots[i]))
                    sink++;
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / frames;
    }

    private static void Benchmark()
    {
        string current = "/mnt/data/folder";
        string[] roots = { "/root", "/", "/mnt", "/mnt/data", "/mnt/database", "/home" };
        for (int i = 0; i < 4; i++) { Time(false, current, roots); Time(true, current, roots); }
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = Time(false, current, roots);
                newMs[i] = Time(true, current, roots);
            }
            else
            {
                newMs[i] = Time(true, current, roots);
                oldMs[i] = Time(false, current, roots);
            }
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        Time(false, current, roots);
        long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Time(true, current, roots);
        long optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (optimizedBytes != 0)
            throw new Exception("optimized sidebar loop allocated " + optimizedBytes + " bytes");
        if (legacyBytes <= 0)
            throw new Exception("legacy sidebar loop unexpectedly allocated no strings");
        checks += 2;
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B 6 sidebar checks per frame: legacy=" +
            a.ToString("F6") + " ms optimized=" + b.ToString("F6") +
            " ms ratio=" + (a / b).ToString("F2") + "x");
        Console.WriteLine("HOST allocation 10000 frames: legacy=" + legacyBytes +
            " B optimized=" + optimizedBytes + " B; sink=" + sink);
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
    }
}
