using System;
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

    private static void Main()
    {
        foreach (string? value in new string?[]
        {
            null, "", ".", "..", "file.", "file ", " ",
            "a/b", "a\\b", "a:b", "a*b", "a?b", "a\"b",
            "a<b", "a>b", "a|b", "a\u0001b", "a\u001fb",
            "CON", "con.txt", "Con.log", "PRN", "prn.dat",
            "AUX", "aux.txt", "NUL", "nul.cfg", "CON .txt",
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
        CheckCollision("notes", null, null, false);

        Console.WriteLine("GUI FAT32 filename policy: " + assertions + " C# assertions PASS");
    }
}
