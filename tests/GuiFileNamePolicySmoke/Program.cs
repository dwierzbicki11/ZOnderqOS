using System;
using System.IO;
using ZonderqOS.GUI;

// Compiles the exact production validator without Cosmos dependencies.
// This checks filename policy only; it does not mount or write to FAT32.
internal static class Program
{
    private static int assertions;

    private static void Expect(string? name, bool accepted)
    {
        assertions++;
        bool actual = FileManagerNamePolicy.IsSafeNewEntryName(name!);
        if (actual != accepted)
            throw new InvalidOperationException(
                "Unexpected FAT32 filename decision for " + (name ?? "<null>") +
                ": expected " + accepted + ", got " + actual);
    }

    private static void CheckCollision(string name, string[]? files,
        string[]? directories, bool expected)
    {
        assertions++;
        bool actual = FileManagerNamePolicy.HasCaseInsensitiveCollision(name, files!, directories!);
        if (actual != expected)
            throw new InvalidOperationException("Unexpected FAT32 name collision decision for " + name);
    }

    // Previous production collision scan, retained only as an allocation
    // baseline. Both paths examine the same preconstructed directory entries.
    private static bool LegacyContainsName(string[] paths, string name)
    {
        for (int i = 0; i < paths.Length; i++)
        {
            string path = paths[i];
            if (string.IsNullOrEmpty(path))
                continue;
            int end = path.Length;
            while (end > 0 && (path[end - 1] == '/' || path[end - 1] == '\\'))
                end--;
            if (end == 0)
                continue;
            int slash = -1;
            for (int j = end - 1; j >= 0; j--)
            {
                if (path[j] == '/' || path[j] == '\\')
                {
                    slash = j;
                    break;
                }
            }
            string existing = path.Substring(slash + 1, end - slash - 1);
            if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void Main()
    {
        foreach (string? value in new string?[]
        {
            null, "", ".", "..", "file.", "file ", " ",
            "a/b", "a\\b", "a:b", "a*b", "a?b", "a\"b",
            "a<b", "a>b", "a|b", "a\u0001b", "a\u001fb",
            "CON", "con.txt", "Con.log", "PRN", "prn.dat",
            "AUX", "aux.txt", "NUL", "nul.cfg", "CON .txt",
            "CONIN$", "conin$.txt", "CONOUT$", "conout$.log",
            "bidi\u202Etxt.exe", "bidi\u202Dtxt.exe", "bidi\u2066txt",
            "bidi\u2069txt", "bidi\u206Atxt", "bidi\u206Btxt",
            "bidi\u206Ctxt", "bidi\u206Dtxt", "bidi\u206Etxt",
            "bidi\u206Ftxt", "bidi\u200Etxt", "bidi\u200Ftxt",
            "bidi\u061Ctxt",
            "hidden\u200Bname", "hidden\u200Cname", "hidden\u200Dname",
            "hidden\u2060name", "hidden\u2061name", "hidden\u2062name",
            "hidden\u2063name", "hidden\u2064name", "hidden\uFEFFname",
            "hidden\u00ADname", "hidden\u034Fname", "hidden\u180Ename",
            "line\u2028break", "paragraph\u2029break",
            "COM1", "com9.txt", "LPT1", "lpt9.log",
            "COM\u00B9", "com\u00B2.txt", "LPT\u00B3", "lpt\u00B9.dat",
            "bad\u007fname", "bad\u0085name", "\uD800", "\uDC00",
            "broken\uD800name", "broken\uDC00name",
            new string('a', 256)
        })
            Expect(value, false);

        foreach (string value in new[]
        {
            "notes.txt", "archive.tar.gz", "my file.txt",
            ".gitignore", "file_name-2026", "image(1).png",
            "COM0", "LPT0", "COM10", "LPT10",
            "zażółć.txt", "report.final",
            new string('a', 255), "emoji-\uD83D\uDE00.txt"
        })
            Expect(value, true);

        // Exhaust the numbered DOS device namespace in both cases and
        // with extensions; the check must not be case-sensitive.
        for (int number = 1; number <= 9; number++)
        {
            Expect("COM" + number, false);
            Expect("com" + number + ".txt", false);
            Expect("LPT" + number, false);
            Expect("lpt" + number + ".dat", false);
        }

        // The LFN limit counts UTF-16 code units, not Unicode code points.
        Expect(new string('a', 254) + "\uD83D\uDE00", false);
        Expect(new string('a', 253) + "\uD83D\uDE00", true);

        CheckCollision("report.txt", new[] { "/data/REPORT.TXT" }, Array.Empty<string>(), true);
        CheckCollision("report.txt", Array.Empty<string>(), new[] { "/data/REPORT.TXT" }, true);
        CheckCollision("report.txt", new[] { @"C:\\DATA\\report.txt" }, Array.Empty<string>(), true);
        CheckCollision("report.txt", new[] { "/data/report2.txt" }, new[] { "/data/archive" }, false);
        CheckCollision("notes", new[] { "/data/notes.txt" }, Array.Empty<string>(), false);
        CheckCollision("report.txt", Array.Empty<string>(), new[] { "/data/REPORT.TXT/" }, true);
        CheckCollision("report.txt", Array.Empty<string>(), new[] { "C:\\DATA\\REPORT.TXT\\" }, true);
        CheckCollision("report.txt", Array.Empty<string>(), new[] { "/" }, false);
        CheckCollision("notes", null, null, false);
        CheckCollision("report", new[] { "/data/REPORTS" }, Array.Empty<string>(), false);
        CheckCollision("report", new[] { "/data/report" }, Array.Empty<string>(), true);
        CheckCollision("report", new[] { "/data/report/" }, Array.Empty<string>(), true);
        CheckCollision("report", new[] { "/data/report//" }, Array.Empty<string>(), true);
        CheckCollision("report", new[] { "/data/repor" }, Array.Empty<string>(), false);
        CheckCollision("report", new[] { "/data/REPORT.log" }, Array.Empty<string>(), false);

        // Compare production against the exact old substring-based scan.
        // This is a host allocation A/B test, not a FAT32 I/O benchmark.
        string[] largeDirectory = new string[2048];
        for (int i = 0; i < largeDirectory.Length - 1; i++)
            largeDirectory[i] = "/mnt/ZONDERQDATA/entry-" + i + ".txt";
        largeDirectory[largeDirectory.Length - 1] = "/mnt/ZONDERQDATA/TARGET.TXT/";
        const string target = "target.txt";
        CheckCollision(target, largeDirectory, Array.Empty<string>(), true);
        CheckCollision("target", largeDirectory, Array.Empty<string>(), false);
        for (int warmup = 0; warmup < 10; warmup++)
        {
            LegacyContainsName(largeDirectory, target);
            FileManagerNamePolicy.HasCaseInsensitiveCollision(target, largeDirectory, Array.Empty<string>());
        }
        long legacyStart = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 16; iteration++)
            if (!LegacyContainsName(largeDirectory, target))
                throw new InvalidOperationException("Legacy baseline missed the collision");
        long legacyAllocated = GC.GetAllocatedBytesForCurrentThread() - legacyStart;
        long optimizedStart = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 16; iteration++)
            if (!FileManagerNamePolicy.HasCaseInsensitiveCollision(target, largeDirectory, Array.Empty<string>()))
                throw new InvalidOperationException("Optimized scan missed the collision");
        long optimizedAllocated = GC.GetAllocatedBytesForCurrentThread() - optimizedStart;
        assertions++;
        if (optimizedAllocated >= legacyAllocated)
            throw new InvalidOperationException("Collision scan allocation regression: " +
                legacyAllocated + " -> " + optimizedAllocated + " bytes");
        Console.WriteLine("FAT32 collision scan host allocations (16 x 2048 entries): " +
            legacyAllocated + " B before, " + optimizedAllocated + " B after");

        // Exercise the exact production CreateNew path on the host. This
        // verifies non-truncation under a duplicate-name race, not FAT32 QEMU.
        string directory = Path.Combine(Path.GetTempPath(),
            "zonderq-gui-create-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "notes.txt");
            FileManagerFileCreation.CreateEmptyFile(file);
            assertions++;
            if (!File.Exists(file) || new FileInfo(file).Length != 0)
                throw new InvalidOperationException("Exclusive creation did not create an empty file");

            File.WriteAllText(file, "KEEP ORIGINAL DATA");
            bool duplicateRejected = false;
            try
            {
                FileManagerFileCreation.CreateEmptyFile(file);
            }
            catch (IOException)
            {
                duplicateRejected = true;
            }
            assertions++;
            if (!duplicateRejected)
                throw new InvalidOperationException("Duplicate create unexpectedly succeeded");
            assertions++;
            if (File.ReadAllText(file) != "KEEP ORIGINAL DATA")
                throw new InvalidOperationException("Duplicate create truncated existing data");

            string second = Path.Combine(directory, "second.txt");
            FileManagerFileCreation.CreateEmptyFile(second);
            assertions++;
            if (!File.Exists(second) || new FileInfo(second).Length != 0)
                throw new InvalidOperationException("Second exclusive creation failed");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        Console.WriteLine("GUI FAT32 filename policy: " + assertions + " C# assertions PASS");
    }
}
