using System;
using System.IO;
using ZonderqOS;

internal static class Program
{
    private static int checks;

    private static void Check(bool ok, string name)
    {
        checks++;
        if (!ok)
            throw new InvalidOperationException("Shadow transaction regression: " + name);
    }

    private static void Main()
    {
        string root = Path.Combine(Path.GetTempPath(),
            "zonderq-shadow-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "shadow");
        try
        {
            File.WriteAllText(file, "root:oldhash\nalice:unchanged\n");
            int permissions = 0;
            ShadowFileTransaction.Replace(file,
                new[] { "root:newhash", "alice:unchanged" },
                path =>
                {
                    permissions++;
                    Check(path == file + ".new" || path == file + ".bak",
                        "protect only stage and backup");
                });

            Check(permissions == 2, "root-only ACL requested for both sensitive paths");
            Check(File.ReadAllText(file).Contains("root:newhash"), "new hash published");
            Check(File.ReadAllText(file).Contains("alice:unchanged"), "other users preserved");
            Check(!File.Exists(file + ".new"), "staging file removed");
            Check(!File.Exists(file + ".bak"), "backup removed on success");

            bool threw = false;
            try
            {
                ShadowFileTransaction.Replace(file, new[] { "root:broken" },
                    _ => throw new IOException("simulated staging permission failure"));
            }
            catch (IOException)
            {
                threw = true;
            }
            Check(threw, "staging permission exception propagated");
            Check(File.ReadAllText(file).Contains("root:newhash"),
                "old credential remains after pre-stage exception");
            Check(!File.Exists(file + ".new"), "failed stage cleaned");

            // Emulate power cut after moving primary -> .bak but before
            // new primary was published. Recovery must prefer last committed
            // credentials and discard the unfinished candidate.
            File.Move(file, file + ".bak");
            File.WriteAllText(file + ".new", "root:uncommitted\n");
            Check(ShadowFileTransaction.RecoverMissingPrimary(file),
                "backup restored after interrupted commit");
            Check(File.ReadAllText(file).Contains("root:newhash"),
                "restored committed hash is unchanged");
            Check(!File.Exists(file + ".bak"), "backup becomes primary");
            Check(!File.Exists(file + ".new"), "uncommitted hash is discarded");

            File.WriteAllText(file + ".bak", "root:obsolete\n");
            Check(ShadowFileTransaction.RecoverMissingPrimary(file), "existing primary accepted");
            Check(File.ReadAllText(file).Contains("root:newhash"),
                "stale backup does not override primary");

            File.Delete(file);
            File.Delete(file + ".bak");
            Check(!ShadowFileTransaction.RecoverMissingPrimary(file),
                "missing primary without backup fails closed");
            Check(!File.Exists(file), "recovery never invents factory credentials");

            threw = false;
            try
            {
                ShadowFileTransaction.Replace(file, new[] { "root:fake" }, null);
            }
            catch (FileNotFoundException)
            {
                threw = true;
            }
            Check(threw, "cannot overwrite absent shadow database");

            File.WriteAllText(file, "root:present\n");
            threw = false;
            try
            {
                ShadowFileTransaction.Replace(file, Array.Empty<string>(), null);
            }
            catch (ArgumentException)
            {
                threw = true;
            }
            Check(threw, "empty credential database rejected");
            Check(File.ReadAllText(file) == "root:present\n",
                "rejecting empty data leaves primary intact");

            AccountFileTransactionSmoke.Run();
            Console.WriteLine("PASS: " + checks + " staged shadow file smoke assertions");
        }
        finally
        {
            try { Directory.Delete(root, true); } catch { }
        }
    }
}
