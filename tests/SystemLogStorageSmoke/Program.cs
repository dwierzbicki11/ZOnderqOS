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
        Check(SystemLogStoragePolicy.BoundMessage(null) == "",
            "null message becomes empty");
        Check(SystemLogStoragePolicy.BoundMessage("first\nsecond\rthird") ==
            "first second third", "newlines are sanitized");
        Check(SystemLogStoragePolicy.BoundMessage("test") == "test",
            "normal message is preserved");
        string longMessage = new string('x', 7000);
        string bounded = SystemLogStoragePolicy.BoundMessage(longMessage);
        Check(bounded.Length == SystemLogStoragePolicy.MaxMessageChars,
            "oversized message is bounded");
        Check(bounded.EndsWith(" [truncated]", StringComparison.Ordinal),
            "truncated messages remain recognizable");
        Check(SystemLogStoragePolicy.BoundMessage(new string('a', 4096)).Length == 4096,
            "exact cap is unchanged");

        Check(SystemLogStoragePolicy.NormalizeSource(null) == "SYS",
            "null source uses a safe label");
        Check(SystemLogStoragePolicy.NormalizeSource("  gui  ") == "GUI",
            "ordinary source is normalized");
        Check(SystemLogStoragePolicy.NormalizeSource("net]\\n[CRITICAL") == "NET___CRITICAL",
            "source cannot inject log delimiters or newlines");
        Check(SystemLogStoragePolicy.NormalizeSource("A\\tB\\rC") == "A_B_C",
            "source controls are sanitized");
        Check(SystemLogStoragePolicy.NormalizeSource(new string('x', 40)).Length == 16,
            "source token is capped at sixteen characters");

        string audit = SecurityAuditLogPolicy.FormatLine(
            "2026-11-10 23:30:00", "WARN]\n[CRITICAL", "root]\nadmin",
            "one\ntwo\rthree");
        Check(audit.Contains("[WARN__\n") == false,
            "severity cannot inject a newline");
        Check(audit.Contains("[UID:root__admin]"),
            "username cannot escape the UID field");
        Check(audit.Contains("one two three"),
            "audit messages are restricted to one physical line");
        Check(audit.Split('\n').Length == 2,
            "audit produces exactly one trailing newline");
        Check(audit.StartsWith("[2026-11-10 23:30:00] "),
            "valid timestamp remains unchanged");
        Check(SecurityAuditLogPolicy.SafeField("", 16, "INFO") == "INFO",
            "empty severity gets a fallback");
        Check(SecurityAuditLogPolicy.SafeField(new string('X', 100), 16, "INFO").Length == 16,
            "large audit field cannot exhaust the log");
        Check(SecurityAuditLogPolicy.SafeField("a\rb\nc", 16, "-") == "a_b_c",
            "audit tokens sanitize carriage return and line feed");
        string hugeAudit = SecurityAuditLogPolicy.FormatLine(
            "t", "INFO", "root", new string('z', 10000));
        Check(hugeAudit.Length < 4300,
            "audit record is bounded despite huge input");
        Check(hugeAudit.Contains("[truncated]"),
            "audit truncation is visible");
        Check(SystemLogStoragePolicy.ShouldRotate(256 * 1024 - 2,
            SecurityAuditLogPolicy.FormatLine("t", "I", "root", "ą"), 256 * 1024),
            "auth log rotation counts UTF-8 bytes");

        Console.WriteLine("PASS: " + checks + " UTF-8 log rotation and audit sanitizer assertions");
        return 0;
    }
}
