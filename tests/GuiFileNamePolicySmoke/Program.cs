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

    private static void Main()
    {
        foreach (string? value in new string?[]
        {
            null, "", ".", "..", "file.", "file ", " ",
            "a/b", "a\\b", "a:b", "a*b", "a?b", "a\"b",
            "a<b", "a>b", "a|b", "a\u0001b", "a\u001fb",
            "CON", "con.txt", "Con.log", "PRN", "prn.dat",
            "AUX", "aux.txt", "NUL", "nul.cfg", "CON .txt",
            "COM1", "com9.txt", "LPT1", "lpt9.log"
        })
            Expect(value, false);

        foreach (string value in new[]
        {
            "notes.txt", "archive.tar.gz", "my file.txt",
            ".gitignore", "file_name-2026", "image(1).png",
            "COM0", "LPT0", "COM10", "LPT10",
            "zażółć.txt", "report.final"
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

        Console.WriteLine("GUI FAT32 filename policy: " + assertions + " C# assertions PASS");
    }
}
