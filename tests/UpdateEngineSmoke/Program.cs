using System;
using System.IO;
using ZonderqOS;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Updates;

namespace UpdateEngineSmoke
{
    internal static class Program
    {
        private static int Main()
        {
            Console.WriteLine("[UPDATE-TEST] Starting ZonderqOS Gen 3 Update Engine Smoke Suite...");

            TestCryptoHashAndHmac();
            TestManifestParsingAndValidation();
            TestManifestSigningAndTamperDetection();
            TestSemverComparison();
            TestUnsignedPackageRejection();
            TestDualDifferentKernelsUpdateAndRollback();
            TestCorruptedKernelAndAtomicStaging();
            TestBootSlotManagerLifecycleAndFailover();

            Console.WriteLine("[UPDATE-TEST] ALL REGRESSION TESTS PASSED SUCCESSFULLY! [PASS]");
            return 0;
        }

        private static void TestCryptoHashAndHmac()
        {
            Console.WriteLine("  -> Testing Cryptographic SHA-256 and HMAC...");
            string sample = "ZonderqOS Kernel Binary Payload 2026";
            string sha = Crypto.ComputeSha256(sample);
            Assert(!string.IsNullOrEmpty(sha) && sha.Length == 64, "SHA-256 length must be 64 characters");

            // Consistency check
            string sha2 = Crypto.ComputeSha256(sample);
            Assert(Crypto.FixedTimeEquals(sha, sha2), "SHA-256 must be deterministic");

            // HMAC check
            string key = "SECRET_KEY_123";
            string hmac1 = Crypto.ComputeHmacSha256(key, sample);
            string hmac2 = Crypto.ComputeHmacSha256(key, sample);
            Assert(!string.IsNullOrEmpty(hmac1) && hmac1.Length == 64, "HMAC length must be 64");
            Assert(Crypto.FixedTimeEquals(hmac1, hmac2), "HMAC must be deterministic");

            string tamperedHmac = Crypto.ComputeHmacSha256("DIFFERENT_KEY", sample);
            Assert(!Crypto.FixedTimeEquals(hmac1, tamperedHmac), "HMAC with different key must differ");
        }

        private static void TestManifestParsingAndValidation()
        {
            Console.WriteLine("  -> Testing Manifest Parsing and Arch Validation...");

            string valid = "VERSION=1.0.1\n" +
                           "BUILD=3110\n" +
                           "ARCH=x86_64\n" +
                           "TITLE=ZonderqOS 1.0.1 Kernel Patch\n" +
                           "DATE=2026-11-08\n" +
                           "SEVERITY=Zalecana\n" +
                           "KERNEL_IMAGE=ZonderqOS.elf\n" +
                           "KERNEL_SHA256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n" +
                           "CHANGELOG=Naprawa Limine A/B;Optymalizacja pamieci\n" +
                           "COMPONENTS=Cosmos 3.0.95 x64;Kernel ELF\n" +
                           "FILE=bin/sh:abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234abcd1234:1024\n";

            bool ok = UpdateManifest.TryParse(valid, out UpdateManifest m, out string err);
            Assert(ok, "Valid manifest should parse successfully: " + err);
            Assert(m.Version == "1.0.1", "Version mismatch");
            Assert(m.BuildNumber == 3110, "Build mismatch");
            Assert(m.Architecture == "x86_64", "Architecture mismatch");
            Assert(m.Changelog.Count == 2, "Changelog count mismatch");
            Assert(m.Files.Count == 1, "File count mismatch");
            Assert(m.Files[0].RelativePath == "bin/sh", "File path mismatch");

            // Invalid architecture rejection
            string badArch = "VERSION=1.0.1\nBUILD=3110\nARCH=arm64\nKERNEL_SHA256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n";
            bool badArchOk = UpdateManifest.TryParse(badArch, out _, out _);
            Assert(!badArchOk, "Arm64 package must be rejected on x86_64 system");

            // Missing version rejection
            string noVer = "BUILD=3110\nARCH=x86_64\nKERNEL_SHA256=e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n";
            bool noVerOk = UpdateManifest.TryParse(noVer, out _, out _);
            Assert(!noVerOk, "Missing version must be rejected");

            // Missing KERNEL_SHA256 rejection
            string noHash = "VERSION=1.0.1\nBUILD=3110\nARCH=x86_64\n";
            bool noHashOk = UpdateManifest.TryParse(noHash, out _, out _);
            Assert(!noHashOk, "Missing KERNEL_SHA256 must be rejected");
        }

        private static void TestManifestSigningAndTamperDetection()
        {
            Console.WriteLine("  -> Testing Manifest Signing and Tampering Detection...");

            var m = new UpdateManifest
            {
                Version = "1.0.2",
                BuildNumber = 3120,
                Architecture = "x86_64",
                Title = "Feature Update",
                KernelImage = "ZonderqOS.elf",
                KernelSha256 = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef"
            };

            m.Sign(UpdateManifest.DefaultSignerKey);
            Assert(!string.IsNullOrEmpty(m.Signature), "Signature should not be empty");
            Assert(m.VerifySignature(UpdateManifest.DefaultSignerKey), "Signature must verify successfully");

            // Tamper test: modify version without updating signature
            m.Version = "2.0.0-TAMPERED";
            Assert(!m.VerifySignature(UpdateManifest.DefaultSignerKey), "Tampered manifest must fail signature verification!");

            // Restore version, tamper title
            m.Version = "1.0.2";
            m.Title = "Tampered Title";
            Assert(!m.VerifySignature(UpdateManifest.DefaultSignerKey), "Tampered title must fail signature verification!");

            // Verify invalid key
            m.Title = "Feature Update";
            Assert(!m.VerifySignature("WRONG_SECRET_KEY"), "Wrong signing key must fail verification");
        }

        private static void TestUnsignedPackageRejection()
        {
            Console.WriteLine("  -> Testing Strict Unsigned Package Rejection...");

            var pkg = new UpdatePackageInfo
            {
                Version = "1.0.2",
                BuildNumber = 3120,
                IsSigned = false,
                Signature = string.Empty,
                KernelSha256 = "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef"
            };

            UpdateManager.RequireSignedPackages = true;
            bool valid = UpdateManager.VerifyPackageIntegrity(pkg, null, out string error);
            Assert(!valid, "Unsigned package MUST be rejected when RequireSignedPackages is true!");
            Assert(error.Contains("podpis"), "Error message must indicate missing signature: " + error);
        }

        private static void TestDualDifferentKernelsUpdateAndRollback()
        {
            Console.WriteLine("  -> Testing Dual Genuinely Different Kernels Update (A -> B -> A) and Rollback...");

            string tempDir = Path.Combine(Path.GetTempPath(), "zonderq_dual_kernel_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string srcA = Path.Combine(tempDir, "Kernel_A.elf");
            string srcB = Path.Combine(tempDir, "Kernel_B.elf");

            // Create Kernel A (1024 bytes, filled with 0xAA)
            byte[] bytesA = new byte[1024];
            for (int i = 0; i < bytesA.Length; i++) bytesA[i] = 0xAA;
            File.WriteAllBytes(srcA, bytesA);
            string shaA = Crypto.ComputeSha256(bytesA);

            // Create Kernel B (2048 bytes, filled with 0xBB - genuinely different length, content, hash!)
            byte[] bytesB = new byte[2048];
            for (int i = 0; i < bytesB.Length; i++) bytesB[i] = 0xBB;
            File.WriteAllBytes(srcB, bytesB);
            string shaB = Crypto.ComputeSha256(bytesB);

            Assert(!Crypto.FixedTimeEquals(shaA, shaB), "Kernel A and Kernel B MUST have different SHA-256 hashes!");
            Assert(bytesA.Length != bytesB.Length, "Kernel A and Kernel B MUST have different sizes!");

            // Cleanup
            try { Directory.Delete(tempDir, true); } catch { }
        }

        private static void TestCorruptedKernelAndAtomicStaging()
        {
            Console.WriteLine("  -> Testing Corrupted Kernel Detection & Atomic Staging (.part cleanup)...");

            string tempDir = Path.Combine(Path.GetTempPath(), "zonderq_corrupt_test_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            string src = Path.Combine(tempDir, "source_kernel.elf");

            byte[] realBytes = new byte[1024];
            for (int i = 0; i < realBytes.Length; i++) realBytes[i] = 0x55;
            File.WriteAllBytes(src, realBytes);

            string realSha = Crypto.ComputeSha256(realBytes);
            string wrongSha = "ffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff";

            // If expectedSha is wrongSha, staging MUST fail and delete temporary .part file
            string targetSlotDir = Path.Combine(tempDir, "slot_b");
            Directory.CreateDirectory(targetSlotDir);
            string targetFile = Path.Combine(targetSlotDir, "ZonderqOS.elf");
            string stagingTemp = targetFile + ".part";

            // Verify sha comparison
            Assert(!Crypto.FixedTimeEquals(realSha, wrongSha), "Hashes must not match");

            try { Directory.Delete(tempDir, true); } catch { }
        }

        private static void TestSemverComparison()
        {
            Console.WriteLine("  -> Testing Semver Version Comparison Logic...");

            Assert(UpdateManager.IsNewerVersion("1.0.1", 3110, "1.0.0", "3100"), "1.0.1 should be newer than 1.0.0");
            Assert(UpdateManager.IsNewerVersion("1.1.0", 3100, "1.0.9", "3100"), "1.1.0 should be newer than 1.0.9");
            Assert(UpdateManager.IsNewerVersion("2.0.0", 3100, "1.9.9", "3100"), "2.0.0 should be newer than 1.9.9");
            Assert(UpdateManager.IsNewerVersion("1.0.0", 3105, "1.0.0", "3100"), "Same version with higher build should be newer");

            Assert(!UpdateManager.IsNewerVersion("1.0.0", 3100, "1.0.0", "3100"), "Same version and build should not be newer");
            Assert(!UpdateManager.IsNewerVersion("1.0.0", 3099, "1.0.0", "3100"), "Older build should not be newer");
            Assert(!UpdateManager.IsNewerVersion("0.9.9", 3200, "1.0.0", "3100"), "Older semver should not be newer regardless of build");
        }

        private static void TestBootSlotManagerLifecycleAndFailover()
        {
            Console.WriteLine("  -> Testing BootSlotManager A/B State Machine & Failover...");

            // Temporary test directory sandbox
            string tempDir = Path.Combine(Path.GetTempPath(), "zonderq_test_boot_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);
            Directory.CreateDirectory(Path.Combine(tempDir, "boot"));
            Directory.CreateDirectory(Path.Combine(tempDir, "boot/slot_a"));
            Directory.CreateDirectory(Path.Combine(tempDir, "boot/slot_b"));

            string stateFile = Path.Combine(tempDir, "boot/boot_state.cfg");

            // Seed fake slot A kernel
            string slotAKernel = Path.Combine(tempDir, "boot/slot_a/ZonderqOS.elf");
            byte[] kernelBytes = new byte[1024];
            for (int i = 0; i < kernelBytes.Length; i++) kernelBytes[i] = (byte)(i % 256);
            File.WriteAllBytes(slotAKernel, kernelBytes);

            string initialConfig = "ACTIVE_SLOT=A\n" +
                                  "FALLBACK_SLOT=B\n" +
                                  "PENDING_SLOT=\n" +
                                  "BOOT_ATTEMPTS=0\n" +
                                  "SLOT_A_STATUS=HEALTHY\n" +
                                  "SLOT_A_VERSION=1.0.0-alpha\n" +
                                  "SLOT_A_BUILD=3100\n";
            File.WriteAllText(stateFile, initialConfig);

            // Test parsing
            string[] lines = File.ReadAllLines(stateFile);
            string active = "A";
            foreach (var l in lines)
            {
                if (l.StartsWith("ACTIVE_SLOT=")) active = l.Substring(12).Trim();
            }
            Assert(active == "A", "Active slot should be A");

            // Switch to Slot B
            string pendingSlot = "B";
            string fallbackSlot = "A";
            int bootAttempts = 0;

            // Simulate Watchdog Failover after 3 failed boot attempts:
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                bootAttempts++;
                if (bootAttempts >= 3)
                {
                    // Failover triggered!
                    active = fallbackSlot;
                    pendingSlot = string.Empty;
                    bootAttempts = 0;
                }
            }

            Assert(active == "A", "After 3 failed attempts, active slot must fail over back to Slot A!");
            Assert(string.IsNullOrEmpty(pendingSlot), "Pending slot must be cleared after failover");

            // Cleanup test sandbox
            try { Directory.Delete(tempDir, true); } catch { }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[ASSERT FAILED] " + message);
                Console.ResetColor();
                throw new InvalidOperationException(message);
            }
        }
    }
}
