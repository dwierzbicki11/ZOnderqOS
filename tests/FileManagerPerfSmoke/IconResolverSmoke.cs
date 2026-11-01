using System;
using System.Diagnostics;
using System.IO;
using ZonderqOS.GUI.Icons;
using ZonderqOS.GUI.Apps;

internal static class IconResolverSmoke
{
    private static int checks;
    private static long sink;

    private static IconType Legacy(string name, bool directory)
    {
        if (directory) return IconType.Folder;
        string extension;
        try { extension = Path.GetExtension(name ?? "").ToLowerInvariant(); }
        catch { return IconType.File; }
        switch (extension)
        {
            case ".bmp": return IconType.FileBmp;
            case ".doc": return IconType.FileDoc;
            case ".docx": return IconType.FileDocx;
            case ".jpg":
            case ".jpeg": return IconType.FileJpg;
            case ".pdf": return IconType.FilePdf;
            case ".png": return IconType.FilePng;
            case ".svg": return IconType.FileSvg;
            case ".txt":
            case ".log":
            case ".md":
            case ".cfg":
            case ".conf":
            case ".ini": return IconType.FileTxt;
            default: return IconType.File;
        }
    }

    // Snapshot of the previous allocation-free production classifier. Used
    // as the A/B baseline, distinct from the allocating Path.GetExtension
    // legacy baseline. Do not silently compare against a different workload.
    private static IconType PreviousOptimized(string name, bool isDirectory)
    {
        if (isDirectory) return IconType.Folder;
        if (string.IsNullOrEmpty(name)) return IconType.File;
        if (name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) return IconType.FileBmp;
        if (name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)) return IconType.FileDoc;
        if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return IconType.FileDocx;
        if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return IconType.FileJpg;
        if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return IconType.FilePdf;
        if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return IconType.FilePng;
        if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return IconType.FileSvg;
        if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return IconType.FileTxt;
        return IconType.File;
    }

    private static void Check(string name, bool directory)
    {
        IconType expected = Legacy(name, directory);
        IconType actual = FileEntryIconResolver.Resolve(name, directory);
        IconType previous = PreviousOptimized(name, directory);
        if (actual != previous)
            throw new Exception("previous icon classifier mismatch name=" + name +
                " directory=" + directory + " previous=" + previous + " new=" + actual);
        if (actual != expected)
            throw new Exception("icon mismatch name=" + name + " directory=" + directory +
                " old=" + expected + " new=" + actual);
        checks++;
    }

    private static void Verify()
    {
        string[] names =
        {
            null, "", "file", "file.", ".txt", ".DOCX", "foo.JPG", "photo.JpEg",
            "readme.MD", "log.CONF", "sample.ini", "archive.tar.gz", "doc.docxx",
            "a.txt/b.pdf", "a.jpg\\b.png", "x.bmp/y", "a.png.txt", "x.svg",
            "ąęść.png", "file.with.many.dots.PDF", "folder\\nested.txt"
        };
        foreach (string name in names)
        {
            Check(name, false);
            Check(name, true);
        }

        string[] suffixes =
        {
            ".bmp", ".doc", ".docx", ".jpg", ".jpeg", ".pdf", ".png",
            ".svg", ".txt", ".log", ".md", ".cfg", ".conf", ".ini",
            ".exe", ".unknown", "", "."
        };
        var random = new Random(3095);
        for (int i = 0; i < 100000; i++)
        {
            string suffix = suffixes[random.Next(suffixes.Length)];
            string name = "prefix" + i + suffix;
            if ((i & 1) == 0) name = name.ToUpperInvariant();
            if ((i % 13) == 0) name = "sub/dir/" + name;
            Check(name, (i % 19) == 0);
        }

        // Once warmed, the classifier must allocate no temporary strings.
        for (int i = 0; i < 1000; i++)
            sink += (int)FileEntryIconResolver.Resolve("photo.JPEG", false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 10000; i++)
            sink += (int)FileEntryIconResolver.Resolve("photo.JPEG", false);
        long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (bytes != 0)
            throw new Exception("optimized classifier allocated " + bytes + " bytes");
        checks++;
        Console.WriteLine("PASS: production icon classifier legacy equivalence " + checks +
            " checks; warm classifier allocations=" + bytes + " B");
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static double Time(bool cached, string[] names, IconType[] icons)
    {
        const int frames = 300;
        long start = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < frames; frame++)
            for (int i = 0; i < names.Length; i++)
                sink += (int)(cached ? icons[i] : Legacy(names[i], false));
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / frames;
    }

    private static void Benchmark()
    {
        // The same 64 visible tiles, rendered over 300 frames. This models
        // icon selection only, not a complete Canvas frame or QEMU graphics.
        string[] names = new string[64];
        IconType[] cached = new IconType[names.Length];
        string[] suffixes = { ".TXT", ".JPG", ".DOCX", ".PNG", ".pdf", ".cfg", ".md", ".unknown" };
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = "tile" + i + suffixes[i % suffixes.Length];
            cached[i] = FileEntryIconResolver.Resolve(names[i], false);
        }
        for (int i = 0; i < 4; i++) { Time(false, names, cached); Time(true, names, cached); }
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = Time(false, names, cached);
                newMs[i] = Time(true, names, cached);
            }
            else
            {
                newMs[i] = Time(true, names, cached);
                oldMs[i] = Time(false, names, cached);
            }
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        Time(false, names, cached);
        long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        Time(true, names, cached);
        long cachedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (cachedBytes != 0)
            throw new Exception("cached icon loop allocated " + cachedBytes + " bytes");
        if (legacyBytes <= 0)
            throw new Exception("legacy icon loop did not allocate as expected");
        checks += 2;
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B 64 icon selections per frame: legacy=" +
            a.ToString("F5") + " ms cached=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x");
        Console.WriteLine("HOST allocation 300 frames: legacy=" + legacyBytes +
            " B cached=" + cachedBytes + " B; sink=" + sink);
    }

    private static double TimeSnapshot(bool optimized, string[] names)
    {
        const int repeats = 20;
        long start = Stopwatch.GetTimestamp();
        for (int rep = 0; rep < repeats; rep++)
            for (int i = 0; i < names.Length; i++)
                sink += (int)(optimized
                    ? FileEntryIconResolver.Resolve(names[i], false)
                    : Legacy(names[i], false));
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / repeats;
    }

    private static void BenchmarkSnapshot()
    {
        // A second A/B isolates the one-time cost paid during a FAT32
        // directory refresh. No filesystem I/O is included.
        string[] suffixes = { ".TXT", ".JPG", ".DOCX", ".PNG", ".pdf",
            ".cfg", ".md", ".unknown", "", ".exe" };
        string[] names = new string[2048];
        for (int i = 0; i < names.Length; i++)
            names[i] = "file" + i + suffixes[i % suffixes.Length];

        for (int i = 0; i < 4; i++)
        {
            TimeSnapshot(false, names);
            TimeSnapshot(true, names);
        }
        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = TimeSnapshot(false, names);
                newMs[i] = TimeSnapshot(true, names);
            }
            else
            {
                newMs[i] = TimeSnapshot(true, names);
                oldMs[i] = TimeSnapshot(false, names);
            }
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        TimeSnapshot(false, names);
        long legacyBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        TimeSnapshot(true, names);
        long optimizedBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (optimizedBytes != 0)
            throw new Exception("snapshot icon classifier allocated " + optimizedBytes + " bytes");
        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B 2048 icon classifications per snapshot: legacy=" +
            a.ToString("F5") + " ms optimized=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x");
        Console.WriteLine("HOST allocation 20 snapshots: legacy=" + legacyBytes +
            " B optimized=" + optimizedBytes + " B");
    }

    private static double TimeSuffixDispatch(bool optimized, string[] names)
    {
        const int repeats = 32;
        long start = Stopwatch.GetTimestamp();
        for (int rep = 0; rep < repeats; rep++)
            for (int i = 0; i < names.Length; i++)
                sink += (int)(optimized
                    ? FileEntryIconResolver.Resolve(names[i], false)
                    : PreviousOptimized(names[i], false));
        return (Stopwatch.GetTimestamp() - start) * 1000.0 /
            Stopwatch.Frequency / repeats;
    }

    private static void BenchmarkSuffixDispatch()
    {
        // Same immutable names for both classifiers. Include many unknown
        // extensions, long filenames and no-extension entries, as well as
        // common recognized types. This measures only CPU classification,
        // not directory I/O, painting or complete system responsiveness.
        string[] suffixes =
        {
            ".txt", ".jpeg", ".png", ".cfg", ".exe", ".tar.gz",
            "", ".unknown", ".dat", ".docx", ".md", ".CONF"
        };
        string[] names = new string[4096];
        for (int i = 0; i < names.Length; i++)
        {
            string prefix = (i % 7 == 0) ? "nested.folder/" : "";
            names[i] = prefix + "long-directory-entry-" + i + suffixes[i % suffixes.Length];
            Check(names[i], false);
        }
        for (int i = 0; i < 6; i++)
        {
            TimeSuffixDispatch(false, names);
            TimeSuffixDispatch(true, names);
        }

        double[] oldMs = new double[7], newMs = new double[7];
        for (int i = 0; i < 7; i++)
        {
            if ((i & 1) == 0)
            {
                oldMs[i] = TimeSuffixDispatch(false, names);
                newMs[i] = TimeSuffixDispatch(true, names);
            }
            else
            {
                newMs[i] = TimeSuffixDispatch(true, names);
                oldMs[i] = TimeSuffixDispatch(false, names);
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        TimeSuffixDispatch(false, names);
        long oldBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        before = GC.GetAllocatedBytesForCurrentThread();
        TimeSuffixDispatch(true, names);
        long newBytes = GC.GetAllocatedBytesForCurrentThread() - before;
        if (oldBytes != 0 || newBytes != 0)
            throw new Exception("suffix classifier warm allocations old=" + oldBytes +
                " new=" + newBytes);

        double a = Median(oldMs), b = Median(newMs);
        Console.WriteLine("HOST A/B 4096 mixed suffixes per snapshot: previous=" +
            a.ToString("F5") + " ms dispatched=" + b.ToString("F5") +
            " ms ratio=" + (a / b).ToString("F2") + "x");
        Console.WriteLine("HOST suffix allocation 32 snapshots: previous=" +
            oldBytes + " B dispatched=" + newBytes + " B");
    }

    internal static void Run()
    {
        Verify();
        Benchmark();
        BenchmarkSnapshot();
        BenchmarkSuffixDispatch();
    }
}
