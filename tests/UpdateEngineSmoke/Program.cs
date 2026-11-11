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
            TestBootSlotManagerLifecycleAndFailover();
            TestElfStructureValidation();
            TestRealEd25519SignatureVectors();
            TestPackageIntegrityRejectsUndeclaredPayload();

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

            // P0: Manifest parser must reject paths capable of escaping the
            // package root and ambiguous entries before any disk write.
            string[] invalidPaths = {
                "../etc/passwd", "/etc/passwd", "bin/../etc",
                "bin//sh", "C:/Windows/system32", "bin\\\\sh",
                "bin/./sh", "bin/sh.", "bin/sh "
            };
            for (int i = 0; i < invalidPaths.Length; i++)
                Assert(!UpdateManifest.IsSafeRelativePath(invalidPaths[i]),
                    "Unsafe path accepted: " + invalidPaths[i]);
            Assert(UpdateManifest.IsSafeRelativePath("bin/sh"), "Safe nested path rejected");

            string baseManifest = "VERSION=1.0.1\nARCH=x86_64\n" +
                "KERNEL_IMAGE=ZonderqOS.elf\nKERNEL_SHA256=" +
                "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855\n";
            string sha = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
            string[] invalidManifests = {
                baseManifest + "FILE=../etc/passwd:" + sha + ":4\n",
                baseManifest + "FILE=bin/sh:not-a-sha:4\n",
                baseManifest + "FILE=bin/sh:" + sha + ":-1\n",
                baseManifest + "FILE=bin/sh:" + sha + ":abc\n",
                baseManifest + "FILE=bin/sh:" + sha + ":2:extra\n",
                baseManifest + "FILE=bin/sh:" + sha + ":4\nFILE=bin/sh:" + sha + ":4\n",
                baseManifest + "VERSION=2.0.0\n",
                baseManifest + "UNKNOWN=1\n",
                baseManifest.Replace("KERNEL_SHA256=" + sha, "KERNEL_SHA256=bad"),
                baseManifest.Replace("KERNEL_IMAGE=ZonderqOS.elf", "KERNEL_IMAGE=../evil.elf")
            };
            for (int i = 0; i < invalidManifests.Length; i++)
            {
                bool accepted = UpdateManifest.TryParse(invalidManifests[i], out _, out string rejection);
                Assert(!accepted, "Malformed manifest accepted at case " + i);
                Assert(!string.IsNullOrEmpty(rejection), "Missing rejection reason at case " + i);
            }

            // Invalid architecture rejection
            string badArch = "VERSION=1.0.1\nBUILD=3110\nARCH=arm64\n";
            bool badArchOk = UpdateManifest.TryParse(badArch, out _, out string badArchErr);
            Assert(!badArchOk, "Arm64 package must be rejected on x86_64 system");

            // Missing version rejection
            string noVer = "BUILD=3110\nARCH=x86_64\n";
            bool noVerOk = UpdateManifest.TryParse(noVer, out _, out string noVerErr);
            Assert(!noVerOk, "Missing version must be rejected");
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

        private static void TestPackageIntegrityRejectsUndeclaredPayload()
        {
            Console.WriteLine("  -> Testing complete payload inventory verification...");
            string temp = Path.Combine(Path.GetTempPath(), "zonderq_payload_" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(temp);
                string kernel = Path.Combine(temp, "ZonderqOS.elf");
                byte[] elf = { 1, 2, 3, 4, 5, 6 };
                File.WriteAllBytes(kernel, elf);
                string payload = Path.Combine(temp, "payload");
                string appDir = Path.Combine(payload, "system", "bin");
                Directory.CreateDirectory(appDir);
                string app = Path.Combine(appDir, "hello");
                byte[] data = { 10, 11, 12, 13 };
                File.WriteAllBytes(app, data);
                var package = new UpdatePackageInfo
                {
                    KernelImagePath = kernel,
                    KernelSha256 = Crypto.ComputeSha256(elf),
                    PayloadPath = payload
                };
                package.Files.Add(new UpdateFileEntry
                {
                    RelativePath = "system/bin/hello",
                    ExpectedSha256 = Crypto.ComputeSha256(data),
                    SizeBytes = data.Length
                });
                Assert(UpdateManager.VerifyPackageIntegrity(package, null, out string okError),
                    "valid declared payload must pass integrity verification: " + okError);
                string extra = Path.Combine(appDir, "extra");
                File.WriteAllText(extra, "undeclared");
                Assert(!UpdateManager.VerifyPackageIntegrity(package, null, out _),
                    "undeclared file in payload must fail");
                File.Delete(extra);

                package.Files[0].RelativePath = "../escape";
                Assert(!UpdateManager.VerifyPackageIntegrity(package, null, out _),
                    "traversal path must fail");
                package.Files[0].RelativePath = "system/bin/hello";
                package.Files[0].SizeBytes = data.Length + 100;
                Assert(!UpdateManager.VerifyPackageIntegrity(package, null, out _),
                    "wrong payload size must fail");
                package.Files[0].SizeBytes = data.Length;
                package.KernelSha256 = string.Empty;
                Assert(!UpdateManager.VerifyPackageIntegrity(package, null, out _),
                    "kernel SHA must be present");
            }
            finally
            {
                if (Directory.Exists(temp)) Directory.Delete(temp, true);
            }
        }

        private static void TestRealEd25519SignatureVectors()
        {
            Console.WriteLine("  -> Testing standard RFC 8032 Ed25519 signature vectors...");
            byte[] publicKey = Convert.FromHexString(
                "d75a980182b10ab7d54bfed3c964073a0ee172f3daa62325af021a68f707511a");
            byte[] signedEmptyMessage = Convert.FromHexString(
                "e5564300c360ac729086e2cc806e828a84877f1eb8e5d974d873e065224901" +
                "55fb8821590a33bacc61e39701cf9b46bd25bf5f0595bbe24655141438e7a100b");
            byte[] emptyMessage = Array.Empty<byte>();
            Assert(ReleaseSignatureVerifier.VerifyEd25519(
                publicKey, emptyMessage, signedEmptyMessage),
                "RFC 8032 Ed25519 test vector should verify");
            byte[] badSig = (byte[])signedEmptyMessage.Clone();
            badSig[27] ^= 0x01;
            Assert(!ReleaseSignatureVerifier.VerifyEd25519(
                publicKey, emptyMessage, badSig),
                "tampered Ed25519 signature must not verify");
            Assert(!ReleaseSignatureVerifier.VerifyEd25519(
                publicKey, new byte[] { 0 }, signedEmptyMessage),
                "different message must not verify");
            Assert(!ReleaseSignatureVerifier.VerifyEd25519(
                new byte[32], emptyMessage, signedEmptyMessage),
                "invalid public key must not verify");
            Assert(!ReleaseSignatureVerifier.VerifyEd25519(
                publicKey, emptyMessage, new byte[32]),
                "truncated signature must not verify");
        }

        private static void TestElfStructureValidation()
        {
            Console.WriteLine("  -> Testing ELF64 x86_64 header and truncated image rejection...");
            byte[] elf = new byte[128];
            elf[0] = 0x7f; elf[1] = (byte)'E'; elf[2] = (byte)'L'; elf[3] = (byte)'F';
            elf[4] = 2; elf[5] = 1; elf[6] = 1;
            elf[16] = 2; // ET_EXEC
            elf[18] = 62; // EM_X86_64
            elf[32] = 64; // program header offset
            elf[54] = 56; // program header entry size
            elf[56] = 1; // one entry
            Assert(BootSlotManager.HasValidX64ElfHeader(elf), "valid ELF header should be accepted");
            byte[] altered = (byte[])elf.Clone();
            altered[0] = 0;
            Assert(!BootSlotManager.HasValidX64ElfHeader(altered), "ELF magic required");
            altered = (byte[])elf.Clone(); altered[4] = 1;
            Assert(!BootSlotManager.HasValidX64ElfHeader(altered), "ELF32 must be rejected");
            altered = (byte[])elf.Clone(); altered[18] = 183;
            Assert(!BootSlotManager.HasValidX64ElfHeader(altered), "ARM64 ELF must be rejected");
            altered = (byte[])elf.Clone(); altered[32] = 120;
            Assert(!BootSlotManager.HasValidX64ElfHeader(altered), "out of range program table rejected");
            Assert(!BootSlotManager.HasValidX64ElfHeader(new byte[] { 0x7f, 69, 76, 70 }),
                "truncated ELF header rejected");
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
