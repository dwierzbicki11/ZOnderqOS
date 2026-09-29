using System;
using ZonderqOS.SystemCore.Packages;

internal static class Program
{
    private static int Main()
    {
        ExpectValid(
            "name=demo\nversion=1.2.3\ndescription=Example\n",
            "demo",
            "1.2.3");

        ExpectInvalid("name=demo\nname=other\nversion=1\n");
        ExpectInvalid("name=../escape\nversion=1\n");
        ExpectInvalid("name=demo\nversion=../../bad\n");
        ExpectInvalid("name=demo\n");
        ExpectInvalid("name=demo\nversion=1\ndescription=" + new string('x', 257) + "\n");

        if (!PackageManifest.IsSafeToken("abc-1.2_test", 64))
            return Fail("safe token rejected");
        if (PackageManifest.IsSafeToken(".hidden", 64))
            return Fail("leading dot token accepted");
        if (PackageManifest.IsSafeToken("a/b", 64))
            return Fail("path separator token accepted");

        Console.WriteLine("[ZPKG-TEST] PASS");
        return 0;
    }

    private static void ExpectValid(string text, string name, string version)
    {
        PackageManifest manifest;
        string error;
        if (!PackageManifest.TryParse(text, out manifest, out error))
            throw new InvalidOperationException("Expected valid manifest: " + error);
        if (manifest.Name != name || manifest.Version != version)
            throw new InvalidOperationException("Parsed values differ from expected values.");
    }

    private static void ExpectInvalid(string text)
    {
        PackageManifest manifest;
        string error;
        if (PackageManifest.TryParse(text, out manifest, out error))
            throw new InvalidOperationException("Expected manifest rejection.");
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine("[ZPKG-TEST][FAIL] " + message);
        return 1;
    }
}
