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

    private static void Check(string name, bool directory)
    {
        IconType expected = Legacy(name, directory);
        IconType actual = FileEntryIconResolver.Resolve(name, directory);
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

    internal static void Run()
    {
        Verify();
        Benchmark();
    }
}
