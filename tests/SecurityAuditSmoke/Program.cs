using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using ZonderqOS;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Updates;

namespace SecurityAuditSmoke
{
    internal static class Program
    {
        private static int passedTests;

        private static int Main()
        {
            try
            {
                Console.WriteLine("[SECURITY-AUDIT-SMOKE] Running host .NET 10 regression suite...");

                RunTest("PermissionManager ACL & Normalization", TestPermissionManager);
                RunTest("Crypto UTF-8, HMAC & PBKDF2", TestCrypto);
                RunTest("AuthenticationGuard Concurrency & Normalization", TestAuthenticationGuard);
                RunTest("UserProfileManager & UserManager Fail-Closed", TestUsersAndProfiles);
                RunTest("TmpFs & RunFs Path Hardening", TestTmpFsAndRunFs);
                RunTest("CommandIO Nested Input & Redirection Stack", TestCommandIO);
                RunTest("ProcessManager & KernelProcess Concurrency", TestProcessManager);
                RunTest("BootSlotManager ELF64 Header & Safe Failover", TestBootSlotManager);
                RunTest("NativeScratchBuffer Unmanaged & Fallback", TestNativeScratchBuffer);

                Console.WriteLine("[SECURITY-AUDIT-SMOKE] PASS (" + passedTests + " test groups passed)");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("[SECURITY-AUDIT-SMOKE] FAIL: " + ex);
                return 1;
            }
        }

        private static void RunTest(string name, Action test)
        {
            test();
            passedTests++;
            Console.WriteLine("  [OK] " + name);
        }

        private static void Check(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private static void TestPermissionManager()
        {
            // Seed baseline system defaults in memory (SaveAclLocked safely no-ops on non-writable /etc)
            PermissionManager.SetPermission("/", "root", 755);
            PermissionManager.SetPermission("/home", "root", 755);
            PermissionManager.SetPermission("/root", "root", 700);
            PermissionManager.SetPermission("/tmp", "root", 777);

            Check(PermissionManager.HasExplicitPermission("/"), "/ must have explicit ACL");
            Check(PermissionManager.GetPermission("/") == ("root", 755), "/ default permissions mismatch");
            Check(PermissionManager.GetPermission("/home") == ("root", 755), "/home default permissions mismatch");
            Check(PermissionManager.GetPermission("/root") == ("root", 700), "/root default permissions mismatch");
            Check(PermissionManager.GetPermission("/tmp") == ("root", 777), "/tmp default permissions mismatch");
            Check(!PermissionManager.HasExplicitPermission("/tmp/audit_unregistered_12345"),
                "unregistered path reported explicit permission");

            // Case-insensitive ACL parity (OrdinalIgnoreCase)
            PermissionManager.SetPermission("/tmp/AuditCase/SubFile.txt", "alice", 640);
            Check(PermissionManager.HasExplicitPermission("/TMP/auditcase/subfile.txt"),
                "case-insensitive HasExplicitPermission failed");
            var caseAcl = PermissionManager.GetPermission("/tmp/auditcase/SUBFILE.TXT");
            Check(caseAcl.Owner == "alice" && caseAcl.Perms == 640,
                "case-insensitive GetPermission parity failed");
            Check(PermissionManager.CanRead("/tmp/auditcase/subfile.txt", "alice"),
                "owner should read 640 file");
            Check(!PermissionManager.CanRead("/tmp/auditcase/subfile.txt", "bob"),
                "other user should not read 640 file");

            // Path normalization: backslash, '.', '..'
            PermissionManager.SetPermission(@"\tmp\audit_norm\a\b\..\.\target.txt", "alice", 600);
            Check(PermissionManager.HasExplicitPermission("/tmp/audit_norm/a/target.txt"),
                "normalized backslash/./.. path missing");
            Check(PermissionManager.GetPermission("/tmp/audit_norm/a/./target.txt") == ("alice", 600),
                "normalized path GetPermission mismatch");

            // CanCreate: parent walk + root vs non-root
            Check(!PermissionManager.CanCreate("/", "alice"), "non-root cannot create /");
            Check(!PermissionManager.CanCreate("", "alice"), "empty path cannot be created");
            Check(!PermissionManager.CanCreate("relative/path", "alice"), "relative path cannot be created");
            Check(PermissionManager.CanCreate("/tmp/audit_nonexistent_dir/nested/file.txt", "alice"),
                "CanCreate should walk up to /tmp (777) for non-root");
            Check(!PermissionManager.CanCreate("/root/audit_secret.txt", "alice"),
                "non-root should not create inside /root (700)");
            Check(PermissionManager.CanCreate("/root/audit_secret.txt", "root"),
                "root should be allowed to create inside /root");
            Check(!PermissionManager.CanCreate("/home/audit_blocked.txt", "alice"),
                "non-root should not create directly inside /home (755)");

            // CanDeleteRecursive
            Check(!PermissionManager.CanDeleteRecursive("/", "root"), "root cannot delete / recursively");
            Check(!PermissionManager.CanDeleteRecursive("", "alice"), "empty dir delete must fail");
            PermissionManager.SetPermission("/tmp/audit_del", "alice", 700);
            PermissionManager.SetPermission("/tmp/audit_del/owned", "alice", 600);
            PermissionManager.SetPermission("/tmp/audit_del/locked", "root", 600);
            Check(!PermissionManager.CanDeleteRecursive("/tmp/audit_del", "alice"),
                "CanDeleteRecursive must deny when child is not writable by user");
            PermissionManager.RemovePermission("/tmp/audit_del/locked");
            Check(PermissionManager.CanDeleteRecursive("/tmp/audit_del", "alice"),
                "CanDeleteRecursive should allow when dir and all children are writable");
            Check(!PermissionManager.CanDeleteRecursive("/tmp/audit_del", "bob"),
                "CanDeleteRecursive must deny other non-root user");

            // CopyPermissionsUnder basic test
            PermissionManager.SetPermission("/tmp/audit_copy_src", "alice", 750);
            PermissionManager.SetPermission("/tmp/audit_copy_src/child", "alice", 640);
            PermissionManager.CopyPermissionsUnder("/tmp/audit_copy_src", "/tmp/audit_copy_dst", "bob");
            Check(PermissionManager.GetPermission("/tmp/audit_copy_dst") == ("bob", 750),
                "CopyPermissionsUnder root failed");
            Check(PermissionManager.GetPermission("/tmp/audit_copy_dst/child") == ("bob", 640),
                "CopyPermissionsUnder child failed");

            // Cleanup before MaxAclEntries test
            PermissionManager.RemovePermissionsUnder("/tmp/AuditCase");
            PermissionManager.RemovePermissionsUnder("/tmp/audit_norm");
            PermissionManager.RemovePermissionsUnder("/tmp/audit_del");
            PermissionManager.RemovePermissionsUnder("/tmp/audit_copy_src");
            PermissionManager.RemovePermissionsUnder("/tmp/audit_copy_dst");

            // MaxAclEntries (4096) enforcement in SetPermission and CopyPermissionsUnder
            try
            {
                PermissionManager.SetPermission("/tmp/audit_cap_src", "alice", 700);
                PermissionManager.SetPermission("/tmp/audit_cap_src/c1", "alice", 600);
                PermissionManager.SetPermission("/tmp/audit_cap_src/c2", "alice", 600);
                PermissionManager.SetPermission("/tmp/audit_cap_src/c3", "alice", 600);

                // Currently 4 baseline + 4 cap_src = 8 entries. Fill up to 4095 entries,
                // leaving room for exactly 1 new entry in CopyPermissionsUnder.
                int added = 0;
                for (int i = 0; i < 4100; i++)
                {
                    string p = "/tmp/audit_cap_fill/" + i;
                    PermissionManager.SetPermission(p, "alice", 600);
                    if (!PermissionManager.HasExplicitPermission(p))
                        break;
                    added++;
                }
                Check(added == 4096 - 8, "Expected 4088 cap filler entries, got " + added);

                // Remove one filler so 1 free slot remains (count = 4095)
                PermissionManager.RemovePermission("/tmp/audit_cap_fill/0");
                Check(!PermissionManager.HasExplicitPermission("/tmp/audit_cap_fill/0"), "filler removal failed");

                // CopyPermissionsUnder has 4 entries to copy, only 1 free slot before MaxAclEntries (4096)
                PermissionManager.CopyPermissionsUnder("/tmp/audit_cap_src", "/tmp/audit_cap_dst", "bob");
                int copiedCount = 0;
                if (PermissionManager.HasExplicitPermission("/tmp/audit_cap_dst")) copiedCount++;
                if (PermissionManager.HasExplicitPermission("/tmp/audit_cap_dst/c1")) copiedCount++;
                if (PermissionManager.HasExplicitPermission("/tmp/audit_cap_dst/c2")) copiedCount++;
                if (PermissionManager.HasExplicitPermission("/tmp/audit_cap_dst/c3")) copiedCount++;
                Check(copiedCount == 1, "CopyPermissionsUnder must stop adding at MaxAclEntries (copied " + copiedCount + ")");
            }
            finally
            {
                PermissionManager.RemovePermissionsUnder("/tmp/audit_cap_fill");
                PermissionManager.RemovePermissionsUnder("/tmp/audit_cap_src");
                PermissionManager.RemovePermissionsUnder("/tmp/audit_cap_dst");
            }
        }

        private static void TestCrypto()
        {
            // Unchanged ASCII SHA-256 vector
            string asciiSha = Crypto.ComputeSha256("abc");
            Check(asciiSha == "BA7816BF8F01CFEA414140DE5DAE2223B00361A396177A9CB410FF61F20015AD",
                "ASCII SHA-256 mismatch: " + asciiSha);

            // Non-ASCII UTF-8 parity with .NET SHA256
            const string nonAsciiText = "zażółć gęślą jaźń 🔐";
            byte[] nonAsciiUtf8 = Encoding.UTF8.GetBytes(nonAsciiText);
            string expectedNonAsciiSha = Convert.ToHexString(SHA256.HashData(nonAsciiUtf8));
            Check(Crypto.ComputeSha256(nonAsciiText) == expectedNonAsciiSha,
                "Non-ASCII SHA-256 UTF-8 parity mismatch");

            // PBKDF2-HMAC-SHA256 ASCII & non-ASCII UTF-8 parity
            const string asciiPass = "ZonderqPass2026!";
            const string asciiSalt = "0123456789ABCDEF";
            string expectedAsciiPbkdf2 = Convert.ToHexString(
                Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(asciiPass),
                    Encoding.UTF8.GetBytes(asciiSalt),
                    16, HashAlgorithmName.SHA256, 32));
            string actualAsciiPbkdf2 = Crypto.HashPasswordV2(asciiPass, asciiSalt, 16);
            Check(actualAsciiPbkdf2 == expectedAsciiPbkdf2, "ASCII PBKDF2 mismatch");

            const string utf8Pass = "Hasło-Żółć-2026";
            const string utf8Salt = "Sól-ĄĘ";
            string expectedUtf8Pbkdf2 = Convert.ToHexString(
                Rfc2898DeriveBytes.Pbkdf2(
                    Encoding.UTF8.GetBytes(utf8Pass),
                    Encoding.UTF8.GetBytes(utf8Salt),
                    16, HashAlgorithmName.SHA256, 32));
            string actualUtf8Pbkdf2 = Crypto.HashPasswordV2(utf8Pass, utf8Salt, 16);
            Check(actualUtf8Pbkdf2 == expectedUtf8Pbkdf2, "Non-ASCII UTF-8 PBKDF2 mismatch");

            // VerifyPasswordV2 with lowercase hex & char[] buffer (ASCII + non-ASCII fallback)
            char[] asciiChars = asciiPass.ToCharArray();
            Check(Crypto.VerifyPasswordV2(asciiChars, asciiChars.Length, asciiSalt, 16, actualAsciiPbkdf2.ToLowerInvariant()),
                "VerifyPasswordV2 ASCII lowercase hex failed");
            asciiChars[0] ^= (char)1;
            Check(!Crypto.VerifyPasswordV2(asciiChars, asciiChars.Length, asciiSalt, 16, actualAsciiPbkdf2),
                "VerifyPasswordV2 accepted wrong ASCII password");

            char[] utf8Chars = utf8Pass.ToCharArray();
            Check(Crypto.VerifyPasswordV2(utf8Chars, utf8Chars.Length, utf8Salt, 16, actualUtf8Pbkdf2.ToLowerInvariant()),
                "VerifyPasswordV2 non-ASCII lowercase hex failed");
            utf8Chars[0] = 'X';
            Check(!Crypto.VerifyPasswordV2(utf8Chars, utf8Chars.Length, utf8Salt, 16, actualUtf8Pbkdf2),
                "VerifyPasswordV2 accepted wrong non-ASCII password");

            // FixedTimeHexEquals case-insensitive hex parity
            Check(Crypto.FixedTimeHexEquals("0123456789abcdefABCDEF", "0123456789ABCDEFabcdef"),
                "FixedTimeHexEquals case-insensitive parity failed");
            Check(!Crypto.FixedTimeHexEquals("0123456789abcdef", "0123456789abcdee"),
                "FixedTimeHexEquals matched differing hex");
            Check(!Crypto.FixedTimeHexEquals("abcd", "abc"),
                "FixedTimeHexEquals matched differing length");
            Check(!Crypto.FixedTimeHexEquals(null, "abcd"),
                "FixedTimeHexEquals matched null");

            // ComputeHmacSha256 UTF-8 parity
            const string hmacKey = "klucz-żółć";
            const string hmacMsg = "wiadomość-testowa-ąęś";
            string expectedHmac = Convert.ToHexString(
                HMACSHA256.HashData(Encoding.UTF8.GetBytes(hmacKey), Encoding.UTF8.GetBytes(hmacMsg)));
            Check(Crypto.ComputeHmacSha256(hmacKey, hmacMsg) == expectedHmac,
                "ComputeHmacSha256 UTF-8 parity failed");
            Check(Crypto.ComputeHmacSha256((string)null, hmacMsg) == string.Empty,
                "ComputeHmacSha256 null key must return empty");
        }

        private static void TestAuthenticationGuard()
        {
            AuthenticationGuard.Reset("?");
            AuthenticationGuard.RecordSuccess("cleanup-reset");

            // Username normalization: null / "" / "   " share "?" bucket
            Check(AuthenticationGuard.CanAttempt(null, out _), "initial null attempt should be allowed");
            AuthenticationGuard.RecordFailure(null);
            AuthenticationGuard.RecordFailure("");
            AuthenticationGuard.RecordFailure("   ");
            Check(!AuthenticationGuard.CanAttempt(" ", out int retryEmpty) && retryEmpty > 0,
                "whitespace/null/empty usernames should share normalized '?' lockout");
            AuthenticationGuard.Reset("?");
            Check(AuthenticationGuard.CanAttempt("", out _), "Reset('?') should clear normalized bucket");

            // Multi-threaded lock safety & eviction stress test
            Exception threadError = null;
            Thread[] workers = new Thread[6];
            for (int t = 0; t < workers.Length; t++)
            {
                int tid = t;
                workers[t] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < 200; i++)
                        {
                            string user = "audit_user_" + ((tid * 7 + i) % 24);
                            AuthenticationGuard.CanAttempt(user, out _);
                            if ((i & 3) == 0)
                                AuthenticationGuard.RecordFailure(user);
                            else if ((i & 7) == 1)
                                AuthenticationGuard.RecordSuccess(user);
                            else
                                AuthenticationGuard.Reset(user);
                        }
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref threadError, ex);
                    }
                });
                workers[t].IsBackground = true;
                workers[t].Start();
            }

            for (int t = 0; t < workers.Length; t++)
                workers[t].Join(5000);

            Check(Volatile.Read(ref threadError) == null,
                "AuthenticationGuard thread safety error: " + Volatile.Read(ref threadError));

            for (int i = 0; i < 24; i++)
                AuthenticationGuard.Reset("audit_user_" + i);
            AuthenticationGuard.RecordSuccess("cleanup-reset");
        }

        private static void TestUsersAndProfiles()
        {
            string tempHome = Path.Combine(Path.GetTempPath(), "zonderq_audit_home_" + Guid.NewGuid().ToString("N"));
            try
            {
                UserProfileManager.EnsureProfile("alice", tempHome);
                string[] expectedDirs =
                {
                    tempHome,
                    tempHome + "/Desktop",
                    tempHome + "/Documents",
                    tempHome + "/Downloads",
                    tempHome + "/Pictures",
                    tempHome + "/.config",
                    tempHome + "/.config/zonderq"
                };
                for (int i = 0; i < expectedDirs.Length; i++)
                {
                    Check(Directory.Exists(expectedDirs[i]), "Missing profile dir: " + expectedDirs[i]);
                    Check(PermissionManager.GetPermission(expectedDirs[i]) == ("alice", 700),
                        "Profile ACL mismatch for " + expectedDirs[i]);
                }
            }
            finally
            {
                PermissionManager.RemovePermissionsUnder(tempHome);
                if (Directory.Exists(tempHome))
                    Directory.Delete(tempHome, true);
            }

            // UserManager fail-closed input & privilege checks (never touching /etc)
            Check(!UserManager.ValidateCredentialsChars("bad user", new char[] { 'a' }, 1),
                "ValidateCredentialsChars accepted invalid username");
            Check(!UserManager.ValidateCredentialsChars("alice", null, 0),
                "ValidateCredentialsChars accepted null buffer");
            Check(!UserManager.ValidateCredentialsChars("alice", new char[4], -1),
                "ValidateCredentialsChars accepted negative length");
            Check(!UserManager.ValidateCredentialsChars("alice", new char[4], 5),
                "ValidateCredentialsChars accepted out-of-range length");

            SecurityContext.EnterLoginState();
            Check(!UserManager.CreateUser("alice", "ValidPass123"),
                "CreateUser must fail when unauthenticated");
            SecurityContext.SetAuthenticated("bob", "/home/bob", 1000);
            Check(!UserManager.CreateUser("alice", "ValidPass123"),
                "CreateUser must fail for non-root UID");
            SecurityContext.SetAuthenticated("root", "/root", 0);
            Check(!UserManager.CreateUser("bad/name", "ValidPass123"),
                "CreateUser must reject invalid username");
            Check(!UserManager.CreateUser("alice", "weak"),
                "CreateUser must reject weak password");
        }

        private static void TestTmpFsAndRunFs()
        {
            string err;
            try
            {
                Check(TmpFs.TryCreateDirectory("/tmp/sec_audit", out err), err);
                Check(TmpFs.TryWrite("/tmp/sec_audit/a.txt", "alpha", false, out err), err);
                Check(TmpFs.TryCopyFile("/tmp/sec_audit/a.txt", "/tmp/sec_audit/b.txt", out err), err);
                Check(TmpFs.TryMoveFile("/tmp/sec_audit/b.txt", "/tmp/sec_audit/c.txt", out err), err);
                Check(TmpFs.TryRead("/tmp/sec_audit/c.txt", out string tmpVal) && tmpVal == "alpha",
                    "TmpFs copy/move content mismatch");
                Check(TmpFs.TryDeleteFile("/tmp/sec_audit/a.txt", out err), err);

                // TmpFs rejections
                string[] badTmpPaths =
                {
                    "/tmp/sec_audit/../evil",
                    "/tmp/../etc/passwd",
                    "/tmp/./sec_audit/dot",
                    "/tmp/sec_audit/bad\nline",
                    "/tmp/sec_audit/bad\0nul",
                    "/tmp/sec_audit/bad\u007fdel",
                    "/etc/passwd",
                    "/tmp",
                    ""
                };
                for (int i = 0; i < badTmpPaths.Length; i++)
                {
                    string p = badTmpPaths[i];
                    Check(!TmpFs.TryWrite(p, "x", false, out _), "TmpFs.TryWrite accepted bad path: " + i);
                    Check(!TmpFs.TryCopyFile("/tmp/sec_audit/c.txt", p, out _), "TmpFs.TryCopyFile accepted bad dest: " + i);
                    Check(!TmpFs.TryCopyFile(p, "/tmp/sec_audit/d.txt", out _), "TmpFs.TryCopyFile accepted bad src: " + i);
                    Check(!TmpFs.TryMoveFile("/tmp/sec_audit/c.txt", p, out _), "TmpFs.TryMoveFile accepted bad dest: " + i);
                    Check(!TmpFs.TryDeleteFile(p, out _), "TmpFs.TryDeleteFile accepted bad path: " + i);
                }
            }
            finally
            {
                TmpFs.TryDeleteDirectory("/tmp/sec_audit", true, out _);
            }

            try
            {
                Check(RunFs.TryCreateDirectory("/run/sec_audit", out err), err);
                Check(RunFs.TryWrite("/run/sec_audit/a.pid", "123", false, out err), err);
                Check(RunFs.TryCopyFile("/run/sec_audit/a.pid", "/run/sec_audit/b.pid", out err), err);
                Check(RunFs.TryMoveFile("/run/sec_audit/b.pid", "/run/sec_audit/c.pid", out err), err);
                Check(RunFs.TryRead("/run/sec_audit/c.pid", out string runVal) && runVal == "123",
                    "RunFs copy/move content mismatch");
                Check(RunFs.TryDeleteFile("/run/sec_audit/a.pid", out err), err);

                string[] badRunPaths =
                {
                    "/run/sec_audit/../evil",
                    "/run/../etc/shadow",
                    "/run/./sec_audit/dot",
                    "/run/sec_audit/bad\nline",
                    "/run/sec_audit/bad\0nul",
                    "/run/sec_audit/bad\u007fdel",
                    "/etc/shadow",
                    "/run",
                    ""
                };
                for (int i = 0; i < badRunPaths.Length; i++)
                {
                    string p = badRunPaths[i];
                    Check(!RunFs.TryWrite(p, "x", false, out _), "RunFs.TryWrite accepted bad path: " + i);
                    Check(!RunFs.TryCopyFile("/run/sec_audit/c.pid", p, out _), "RunFs.TryCopyFile accepted bad dest: " + i);
                    Check(!RunFs.TryCopyFile(p, "/run/sec_audit/d.pid", out _), "RunFs.TryCopyFile accepted bad src: " + i);
                    Check(!RunFs.TryMoveFile("/run/sec_audit/c.pid", p, out _), "RunFs.TryMoveFile accepted bad dest: " + i);
                    Check(!RunFs.TryDeleteFile(p, out _), "RunFs.TryDeleteFile accepted bad path: " + i);
                }
            }
            finally
            {
                RunFs.TryDeleteDirectory("/run/sec_audit", true, out _);
            }
        }

        private static void TestCommandIO()
        {
            CommandIO.SetInput("outer-input");
            Check(CommandIO.HasInput && CommandIO.GetInput() == "outer-input", "outer input not set");

            CommandIO.PushInput("middle-input");
            Check(CommandIO.GetInput() == "middle-input", "PushInput failed");

            CommandIO.StartRedirection();
            try
            {
                CommandIO.SetInput("inner-redirect-input");
                CommandIO.WriteLine("captured-line");
            }
            finally
            {
                string captured = CommandIO.EndRedirection();
                Check(captured == "captured-line\n", "redirection capture mismatch");
            }

            Check(CommandIO.GetInput() == "middle-input",
                "EndRedirection did not restore outer input");
            CommandIO.PopInput();
            Check(CommandIO.GetInput() == "outer-input",
                "PopInput did not restore outermost input");

            CommandIO.SaveInput();
            CommandIO.SetInput(null);
            Check(!CommandIO.HasInput, "SetInput(null) did not clear current input");
            CommandIO.RestoreInput();
            Check(CommandIO.GetInput() == "outer-input", "SaveInput/RestoreInput failed");

            // Verify MaxInputDepth (64) enforcement
            int pushed = 0;
            bool threwDepth = false;
            try
            {
                for (int i = 0; i < 70; i++)
                {
                    CommandIO.SaveInput();
                    pushed++;
                }
            }
            catch (InvalidOperationException)
            {
                threwDepth = true;
            }
            finally
            {
                for (int i = 0; i < pushed; i++)
                    CommandIO.RestoreInput();
                CommandIO.SetInput(null);
            }
            Check(threwDepth && pushed == 64, "CommandIO input nesting depth limit mismatch: " + pushed);
        }

        private static void TestProcessManager()
        {
            const int launcherCount = 4;
            const int perLauncher = 10;
            var startedPids = new List<int>();
            object pidLock = new object();
            using var allStarted = new CountdownEvent(launcherCount * perLauncher);
            using var releaseWaiters = new ManualResetEventSlim(false);
            Exception stressError = null;

            Thread[] launchers = new Thread[launcherCount];
            for (int l = 0; l < launcherCount; l++)
            {
                int lid = l;
                launchers[l] = new Thread(() =>
                {
                    try
                    {
                        for (int i = 0; i < perLauncher; i++)
                        {
                            bool shortLived = (i % 2) == 0;
                            int pid = ProcessManager.Start("audit-proc-" + lid + "-" + i, token =>
                            {
                                allStarted.Signal();
                                if (shortLived)
                                    return;
                                while (!token.IsCancellationRequested)
                                    Thread.Sleep(2);
                                releaseWaiters.Wait(3000);
                            });
                            lock (pidLock)
                                startedPids.Add(pid);
                        }
                    }
                    catch (Exception ex)
                    {
                        Volatile.Write(ref stressError, ex);
                    }
                });
                launchers[l].IsBackground = true;
                launchers[l].Start();
            }

            // Concurrent observer polling FillActiveProcesses / IsRunning
            using var stopObservers = new ManualResetEventSlim(false);
            Thread observer = new Thread(() =>
            {
                var scratch = new List<KernelProcess>();
                try
                {
                    while (!stopObservers.IsSet)
                    {
                        ProcessManager.FillActiveProcesses(scratch);
                        ProcessManager.IsRunning("audit-proc-0-1");
                        Thread.Sleep(2);
                    }
                }
                catch (Exception ex)
                {
                    Volatile.Write(ref stressError, ex);
                }
            });
            observer.IsBackground = true;
            observer.Start();

            try
            {
                for (int l = 0; l < launcherCount; l++)
                    launchers[l].Join(5000);
                Check(allStarted.Wait(5000), "Not all stress processes started");

                lock (pidLock)
                {
                    for (int i = 0; i < startedPids.Count; i++)
                    {
                        ProcessManager.SendSignal(startedPids[i],
                            (i % 2) == 0 ? ProcessSignal.Interrupt : ProcessSignal.Terminate);
                    }
                }

                releaseWaiters.Set();
                for (int attempt = 0; attempt < 300; attempt++)
                {
                    if (ProcessManager.GetActiveProcesses().Count == 0)
                        break;
                    Thread.Sleep(10);
                }
            }
            finally
            {
                releaseWaiters.Set();
                stopObservers.Set();
                observer.Join(3000);
            }

            Check(Volatile.Read(ref stressError) == null,
                "ProcessManager stress error: " + Volatile.Read(ref stressError));
            Check(ProcessManager.GetActiveProcesses().Count == 0,
                "Dead processes remained in ProcessManager registry");
            Check(!ProcessManager.SendSignal(0, ProcessSignal.Terminate), "PID 0 signaled");
            Check(!ProcessManager.SendSignal(-5, ProcessSignal.Interrupt), "negative PID signaled");
            Check(!ProcessManager.SendSignal(1, (ProcessSignal)9), "unsupported signal 9 accepted");
        }

        private static byte[] MakeElf64Header(ushort machine, ulong totalLength = 128,
            ulong phoff = 64, ushort phentsize = 56, ushort phnum = 1)
        {
            byte[] elf = new byte[totalLength];
            elf[0] = 0x7F; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
            elf[4] = 2; elf[5] = 1; elf[6] = 1;
            elf[16] = 2; elf[17] = 0; // ET_EXEC
            elf[18] = (byte)(machine & 0xFF);
            elf[19] = (byte)(machine >> 8);
            for (int i = 0; i < 8; i++)
                elf[32 + i] = (byte)((phoff >> (8 * i)) & 0xFF);
            elf[54] = (byte)(phentsize & 0xFF);
            elf[55] = (byte)(phentsize >> 8);
            elf[56] = (byte)(phnum & 0xFF);
            elf[57] = (byte)(phnum >> 8);
            return elf;
        }

        private static void TestBootSlotManager()
        {
            byte[] x64 = MakeElf64Header(62);
            byte[] arm64 = MakeElf64Header(183);

            Check(BootSlotManager.HasValidElfHeader(x64, (ulong)x64.Length),
                "x64 ELF64 header rejected");
            Check(BootSlotManager.HasValidX64ElfHeader(x64),
                "HasValidX64ElfHeader rejected valid x64 ELF");
            Check(!BootSlotManager.HasValidElfHeader(arm64, (ulong)arm64.Length),
                "ARM64 ELF should be rejected on x64 host build");
            Check(!BootSlotManager.HasValidElfHeader(MakeElf64Header(62, 128, 32, 56, 1), 128),
                "phoff < 64 must be rejected");
            Check(!BootSlotManager.HasValidElfHeader(MakeElf64Header(62, 128, 64, 40, 1), 128),
                "phentsize < 56 must be rejected");
            Check(!BootSlotManager.HasValidElfHeader(MakeElf64Header(62, 128, 64, 56, 0), 128),
                "phnum == 0 must be rejected");
            Check(!BootSlotManager.HasValidElfHeader(MakeElf64Header(62, 128, 64, 56, 2), 128),
                "phoff + phnum*phentsize > totalLength must be rejected");

            string tempDir = Path.Combine(Path.GetTempPath(), "zonderq_elf_audit_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(tempDir);
                string validPath = Path.Combine(tempDir, "valid_x64.elf");
                string invalidPath = Path.Combine(tempDir, "bad.elf");
                File.WriteAllBytes(validPath, x64);
                File.WriteAllBytes(invalidPath, new byte[32]);

                Check(BootSlotManager.HasValidElfHeaderFile(validPath),
                    "HasValidElfHeaderFile rejected valid temp ELF");
                Check(!BootSlotManager.HasValidElfHeaderFile(invalidPath),
                    "HasValidElfHeaderFile accepted truncated file");
                Check(!BootSlotManager.HasValidElfHeaderFile(Path.Combine(tempDir, "missing.elf")),
                    "HasValidElfHeaderFile accepted missing file");

                string streamedSha = BootSlotManager.ComputeFileSha256(validPath);
                Check(Crypto.FixedTimeHexEquals(streamedSha, Crypto.ComputeSha256(x64)),
                    "ComputeFileSha256 mismatch against Crypto.ComputeSha256");
            }
            finally
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, true);
            }

            // On unprivileged host where /boot/slot_b/ZonderqOS.elf is absent, ManualRollback fails closed
            Check(!BootSlotManager.ManualRollback(out string rollbackErr) && !string.IsNullOrEmpty(rollbackErr),
                "ManualRollback must fail closed with message when fallback kernel is absent");
        }

        private static void TestNativeScratchBuffer()
        {
            NativeScratchBuffer<int> buf = default;
            Check(buf.Length == 0 && !buf.IsAllocated, "default NativeScratchBuffer should be empty");
            Check(buf[0] == 0, "out-of-bounds read on empty buffer should return default");
            buf[0] = 123; // no-op safe

            buf.Allocate(0);
            Check(!buf.IsAllocated && buf.Length == 0, "Allocate(0) should stay unallocated");

            // Small managed fallback (< 4096 bytes)
            buf.Allocate(16);
            Check(buf.IsAllocated && buf.Length == 16, "managed fallback allocation failed");
            buf[5] = 777;
            Check(buf[5] == 777, "managed fallback indexing failed");
            buf.Clear();
            Check(buf[5] == 0, "managed fallback Clear failed");

            // Unmanaged NativeMemory path (>= 4096 bytes: 2048 ints = 8192 bytes)
            buf.Allocate(2048);
            unsafe
            {
                Check(buf.IsAllocated && buf.Pointer != null && buf.Length == 2048,
                    "unmanaged NativeScratchBuffer allocation failed");
            }
            for (int i = 0; i < buf.Length; i++)
                buf[i] = i + 1;
            Check(buf[0] == 1 && buf[2047] == 2048, "unmanaged indexing mismatch");
            Check(buf[-1] == 0 && buf[2048] == 0, "out-of-bounds read did not return 0");
            buf[-1] = 99;
            buf[2048] = 99;

            Span<int> span = buf.AsSpan();
            Check(span.Length == 2048 && span[100] == 101, "AsSpan mismatch");

            buf.Clear();
            Check(buf[0] == 0 && buf[100] == 0 && buf[2047] == 0, "unmanaged Clear failed");

            buf.Dispose();
            buf.Dispose(); // idempotent double dispose
            unsafe
            {
                Check(!buf.IsAllocated && buf.Length == 0 && buf.Pointer == null,
                    "Dispose did not reset NativeScratchBuffer");
            }
        }
    }
}

namespace ZonderqOS
{
    public static class WriteMessage
    {
        public static void WriteError(string message, string category) { }
    }

    public static class SecurityLogger
    {
        public static void LogEvent(string level, string message) { }
        public static void Flush() { }
    }

    public enum SystemLogLevel
    {
        Debug = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
        Critical = 4
    }

    public static class SystemLogger
    {
        public static void Log(SystemLogLevel level, string source, string message) { }
        public static void Flush() { }
    }

    public static class EnvironmentManager
    {
        public static void Set(string key, string value) { }
    }

    public static class SessionManager
    {
        public static void BeginSession() { }
        public static void EndSession() { }
    }
}

namespace ZonderqOS.Security
{
    internal static class SecurityStubAnchor { }
}

namespace ZonderqOS.SystemCore
{
    internal static class SchedulerTelemetry
    {
        internal static uint CurrentThreadId() => 1u;
        internal static void EnableCurrentManagedPreemption() { }
    }
}

namespace ZonderqOS.SystemCore.Updates
{
    internal static class UpdateManager
    {
        public static bool RestoreSharedFilesAfterBootFailure(string slot, out string error)
        {
            error = string.Empty;
            return false;
        }
    }
}
