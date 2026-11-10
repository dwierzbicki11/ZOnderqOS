using System;
using ZonderqOS;

internal static class Program
{
    private static int checks;
    private static void Check(bool success, string message)
    {
        checks++;
        if (!success)
            throw new InvalidOperationException("Log rotation: " + message);
    }

    public static int Main()
    {
        const long limit = 100;
        Check(!SystemLogStoragePolicy.ShouldRotate(0, "hello\n", limit),
            "short ASCII record fits");
        Check(!SystemLogStoragePolicy.ShouldRotate(94, "hello\n", limit),
            "boundary at exactly limit does not rotate");
        Check(SystemLogStoragePolicy.ShouldRotate(95, "hello\n", limit),
            "one byte beyond limit rotates");
        Check(!SystemLogStoragePolicy.ShouldRotate(95, "a\n", limit),
            "small record stays within limit");
        Check(SystemLogStoragePolicy.ShouldRotate(98, "Zaż\n", limit),
            "UTF-8 non-ASCII record counts encoded bytes");
        Check(!SystemLogStoragePolicy.ShouldRotate(95, "\n", limit),
            "single newline fits");
        Check(SystemLogStoragePolicy.ShouldRotate(101, "", limit),
            "existing oversize file rotates");
        Check(!SystemLogStoragePolicy.ShouldRotate(-1, "line\n", limit),
            "invalid metadata leaves log intact");
        Check(!SystemLogStoragePolicy.ShouldRotate(0, "line\n", 0),
            "invalid capacity leaves log intact");
        Check(SystemLogStoragePolicy.ShouldRotate(long.MaxValue, "\n", limit),
            "large file metadata cannot overflow");
        Check(!SystemLogStoragePolicy.ShouldRotate(0, "", limit),
            "empty record does not rotate");
        Check(!SystemLogStoragePolicy.ShouldRotate(0, null, limit),
            "null normalized record does not rotate");
        Console.WriteLine("PASS: " + checks + " UTF-8 log rotation assertions");
        return 0;
    }
}
