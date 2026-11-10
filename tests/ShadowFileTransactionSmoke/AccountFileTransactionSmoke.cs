using System;
using System.IO;
using ZonderqOS;

internal static class AccountFileTransactionSmoke
{
    internal static void Run()
    {
        string dir = Path.Combine(Path.GetTempPath(),
            "zonderq-account-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        string passwd = Path.Combine(dir, "passwd");
        string shadow = Path.Combine(dir, "shadow");
        int checks = 0;

        void Verify(bool success, string description)
        {
            checks++;
            if (!success)
                throw new InvalidOperationException("Account transaction: " + description);
        }

        try
        {
            Verify(AccountFileTransaction.IsFreshAccountStorage(false, false, false, false),
                "brand-new account storage can be provisioned");
            Verify(!AccountFileTransaction.IsFreshAccountStorage(false, false, true, false),
                "provisioned marker blocks factory root reset when databases vanish");
            Verify(!AccountFileTransaction.IsFreshAccountStorage(false, false, false, true),
                "interrupted account transaction blocks factory provisioning");
            Verify(!AccountFileTransaction.IsFreshAccountStorage(true, false, false, false),
                "passwd-only database blocks default credentials");
            Verify(!AccountFileTransaction.IsFreshAccountStorage(false, true, false, false),
                "shadow-only database blocks default credentials");
            Verify(!AccountFileTransaction.IsFreshAccountStorage(true, true, true, false),
                "provisioned database cannot be reinitialized");
            Verify(!AccountFileTransaction.ContainsUser(new[] { "alice2:hash" }, "alice"),
                "account name comparison must match exactly");

            File.WriteAllLines(passwd, new[] { "root:x:0:/root" });
            File.WriteAllLines(shadow, new[] { "root:root-hash" });
            AccountFileTransaction.AppendAccount(passwd, shadow, "alice",
                "alice:x:1000:/home/alice", "alice:alice-hash", null);

            Verify(AccountFileTransaction.ContainsUser(File.ReadAllLines(passwd), "alice"),
                "new user published in passwd");
            Verify(AccountFileTransaction.ContainsUser(File.ReadAllLines(shadow), "alice"),
                "new user published in shadow");
            Verify(File.ReadAllLines(passwd).Length == 2 &&
                File.ReadAllLines(shadow).Length == 2,
                "root preserved alongside newly committed account");

            bool duplicate = false;
            try
            {
                AccountFileTransaction.AppendAccount(passwd, shadow, "alice",
                    "alice:x:1001:/home/alice2", "alice:replacement", null);
            }
            catch (InvalidOperationException) { duplicate = true; }
            Verify(duplicate, "duplicate account rejected");
            Verify(File.ReadAllLines(passwd).Length == 2 &&
                File.ReadAllLines(shadow).Length == 2,
                "duplicate did not mutate committed databases");

            // Simulate an interruption between the two commits: the shadow
            // record has already been published when staging passwd fails.
            bool sawShadowBeforePasswd = false;
            bool interrupted = false;
            try
            {
                AccountFileTransaction.AppendAccount(passwd, shadow, "bob",
                    "bob:x:1001:/home/bob", "bob:bob-hash", (path, mode) =>
                    {
                        if (path == passwd + ".new")
                        {
                            sawShadowBeforePasswd =
                                AccountFileTransaction.ContainsUser(
                                    File.ReadAllLines(shadow), "bob") &&
                                !AccountFileTransaction.ContainsUser(
                                    File.ReadAllLines(passwd), "bob");
                            throw new IOException("simulated passwd staging failure");
                        }
                    });
            }
            catch (IOException) { interrupted = true; }

            Verify(interrupted && sawShadowBeforePasswd,
                "shadow is committed before passwd, no passwd-only account");
            Verify(!AccountFileTransaction.ContainsUser(File.ReadAllLines(passwd), "bob"),
                "failed new user cannot log in through passwd");
            Verify(!AccountFileTransaction.ContainsUser(File.ReadAllLines(shadow), "bob"),
                "ordinary failure restores old shadow content");

            // A hard power failure after shadow commit creates an orphan
            // record. Reusing that username must be denied, not granted.
            ShadowFileTransaction.Replace(shadow,
                new[] { "root:root-hash", "alice:alice-hash", "carol:orphan-hash" }, null);
            Verify(!AccountFileTransaction.ContainsUser(File.ReadAllLines(passwd), "carol"),
                "simulated orphan absent from passwd");
            bool orphanRejected = false;
            try
            {
                AccountFileTransaction.AppendAccount(passwd, shadow, "carol",
                    "carol:x:1002:/home/carol", "carol:new-hash", null);
            }
            catch (InvalidOperationException) { orphanRejected = true; }
            Verify(orphanRejected, "orphan shadow name cannot be silently reused");

            // Emulate an interrupted passwd rename. The committed backup must
            // restore an existing account without generating factory records.
            File.Move(passwd, passwd + ".bak");
            File.WriteAllText(passwd + ".new", "root:torn\n");
            Verify(ShadowFileTransaction.RecoverMissingPrimary(passwd),
                "passwd restored from backup");
            Verify(AccountFileTransaction.ContainsUser(File.ReadAllLines(passwd), "alice"),
                "restored passwd preserves the user");
            Verify(!File.Exists(passwd + ".new"), "staged passwd discarded on recovery");

            bool invalidUserRejected = false;
            try
            {
                AccountFileTransaction.AppendAccount(passwd, shadow, "bad:name",
                    "bad:name:x:1003:/home/bad", "bad:name:hash", null);
            }
            catch (ArgumentException) { invalidUserRejected = true; }
            Verify(invalidUserRejected, "invalid names cannot enter either database");

            Console.WriteLine("PASS: " + checks + " dual account database assertions");
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }
}
