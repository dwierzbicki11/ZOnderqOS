using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;
using Org.BouncyCastle.Crypto.Digests;
using ZonderqOS.Storage;

namespace ZonderqOS
{
    internal static class Disk
    {
        public static bool IsLiveSession => true;
    }

    internal static class WriteMessage
    {
        public static void WriteError(string msg, string tag) => Console.WriteLine("[" + tag + "][ERR] " + msg);
        public static void WriteOK(string msg, string tag) => Console.WriteLine("[" + tag + "][OK] " + msg);
    }

    namespace GUI
    {
        internal static class StorageMountManager
        {
            public static string GetMountPoint(int partitionIndex) => null;
        }
    }
}

namespace UniversalInstallerSmoke
{
    internal sealed class VirtualTestDisk : IBlockDevice
    {
        private readonly byte[] buffer;
        public ulong BlockCount { get; }
        public ulong BlockSize => 512UL;
        public string Name { get; }

        public VirtualTestDisk(string name, int sizeMegabytes)
        {
            Name = name;
            buffer = new byte[sizeMegabytes * 1024 * 1024];
            BlockCount = (ulong)buffer.Length / BlockSize;
        }

        public byte[] RawBytes => buffer;

        public void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        {
            int offset = checked((int)(blockNo * BlockSize));
            int count = checked((int)(blockCount * BlockSize));
            buffer.AsSpan(offset, count).CopyTo(data);
        }

        public void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
        {
            int offset = checked((int)(blockNo * BlockSize));
            int count = checked((int)(blockCount * BlockSize));
            data.Slice(0, count).CopyTo(buffer.AsSpan(offset, count));
        }

        public void Flush()
        {
        }

        public string ComputeFullDiskSha256()
        {
            var digest = new Sha256Digest();
            digest.BlockUpdate(buffer, 0, buffer.Length);
            byte[] outBytes = new byte[digest.GetDigestSize()];
            digest.DoFinal(outBytes, 0);
            var sb = new StringBuilder(outBytes.Length * 2);
            for (int i = 0; i < outBytes.Length; i++)
                sb.Append(outBytes[i].ToString("x2"));
            return sb.ToString();
        }
    }

    internal static class Program
    {
        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException("ASSERTION FAILED: " + message);
        }

        private static byte[] BuildSyntheticElfKernel(int sizeBytes)
        {
            byte[] elf = new byte[sizeBytes];
            elf[0] = 0x7F;
            elf[1] = (byte)'E';
            elf[2] = (byte)'L';
            elf[3] = (byte)'F';
            elf[4] = 2; // 64-bit
            elf[5] = 1; // little-endian
            elf[6] = 1; // version
            for (int i = 64; i < sizeBytes; i++)
                elf[i] = (byte)((i * 31 + 17) & 0xFF);
            return elf;
        }

        private static void InstallHostCosmosNativeStubs()
        {
            const string soPath = "/tmp/libcosmos_host_stub.so";
            if (!System.IO.File.Exists(soPath))
            {
                const string cSrc = "/tmp/cosmos_host_stub.c";
                System.IO.File.WriteAllText(cSrc,
                    "#include <stdint.h>\n" +
                    "uint64_t _native_cpu_save_irq_and_disable(void) { return 0; }\n" +
                    "void _native_cpu_restore_irq(uint64_t flags) { (void)flags; }\n" +
                    "uint64_t _native_cpu_read_irq_flags(void) { return 0; }\n" +
                    "void _native_cpu_disable_interrupts(void) {}\n" +
                    "void _native_cpu_enable_interrupts(void) {}\n" +
                    "void _native_cpu_halt(void) {}\n" +
                    "void _native_io_write_byte(uint16_t port, uint8_t val) { (void)port; (void)val; }\n" +
                    "void _native_io_write_word(uint16_t port, uint16_t val) { (void)port; (void)val; }\n" +
                    "void _native_io_write_dword(uint16_t port, uint32_t val) { (void)port; (void)val; }\n" +
                    "uint8_t _native_io_read_byte(uint16_t port) { (void)port; return 0x20; }\n" +
                    "uint16_t _native_io_read_word(uint16_t port) { (void)port; return 0; }\n" +
                    "uint32_t _native_io_read_dword(uint16_t port) { (void)port; return 0; }\n");
                var p = System.Diagnostics.Process.Start("gcc", "-shared -fPIC -O2 " + cSrc + " -o " + soPath);
                p?.WaitForExit();
            }

            IntPtr handle = System.Runtime.InteropServices.NativeLibrary.Load(soPath);
            string[] asmNames = { "Cosmos.Kernel.Core", "Cosmos.Kernel.Core.X64", "Cosmos.Kernel.HAL", "Cosmos.Kernel.HAL.X64" };
            foreach (string name in asmNames)
            {
                try
                {
                    var asm = System.Reflection.Assembly.Load(name);
                    System.Runtime.InteropServices.NativeLibrary.SetDllImportResolver(asm, (libName, _, _) =>
                        libName == "*" ? handle : IntPtr.Zero);
                }
                catch
                {
                }
            }
        }

        private static int Main()
        {
            Console.WriteLine("=== ZonderqOS Universal Graphical Installer Smoke Suite ===");
            InstallHostCosmosNativeStubs();
            typeof(StorageManager)
                .GetMethod("Initialize", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, null);

            // Mount an ephemeral RAM root at "/" so VfsManager has a root superblock for /mnt/...
            Assert(ZonderqOS.RamRootVolume.TryCreate(out Partition ramPart), "RamRootVolume creation failed");
            Assert(VfsManager.RegisterFileSystem("ramfat_root", new FatFileSystemType(ramPart)), "Register ramfat_root failed");
            Assert(VfsManager.TryMount("ramfat_root", ReadOnlySpan<char>.Empty, MountFlags.None, "/", out _), "Mount / failed");

            // 1. Register Disk 0 (Protected Foreign OS Disk - e.g. Kali Linux) and Disk 1 (Target Disk)
            var disk0Protected = new VirtualTestDisk("ahci0-kali-ssd", 72);
            Mbr.Create(disk0Protected);
            Mbr.AddPartition(disk0Protected, 0, 0x83, 2048UL, disk0Protected.BlockCount - 2048UL);
            // Write Ext4 superblock magic 0xEF53 at partition offset 1024 + 56
            int extSbOffset = 2048 * 512 + 1024 + 56;
            disk0Protected.RawBytes[extSbOffset] = 0x53;
            disk0Protected.RawBytes[extSbOffset + 1] = 0xEF;
            StorageManager.RegisterDevice(disk0Protected);

            var disk1Target = new VirtualTestDisk("ahci1-target-hdd", 180);
            // Seed Disk 1 with an old partition to test Test B (existing partitions overwrite)
            Mbr.Create(disk1Target);
            Mbr.AddPartition(disk1Target, 0, 0x07, 2048UL, 50000UL);
            Encoding.ASCII.GetBytes("NTFS    ").CopyTo(disk1Target.RawBytes, 2048 * 512 + 3);
            StorageManager.RegisterDevice(disk1Target);

            string disk0HashBefore = disk0Protected.ComputeFullDiskSha256();
            Console.WriteLine("[Test C] Protected Disk 0 SHA-256 before install: " + disk0HashBefore);

            // 2. Discover disks via DiskCatalog
            List<DiskCatalogEntry> catalog = DiskCatalog.DiscoverPhysicalDisks();
            Assert(catalog.Count == 2, "Expected 2 physical disks (RAM root excluded), found " + catalog.Count);

            DiskCatalogEntry entry0 = catalog[0];
            DiskCatalogEntry entry1 = catalog[1];
            Console.WriteLine("Discovered " + entry0.DiskId + ": " + entry0.Model + " | FS=" + entry0.FilesystemsSummary);
            Console.WriteLine("Discovered " + entry1.DiskId + ": " + entry1.Model + " | FS=" + entry1.FilesystemsSummary);

            Assert(entry0.HasExistingData && entry0.FilesystemsSummary.Contains("EXT2/3/4"), "Disk 0 should report EXT2/3/4 existing data");
            Assert(entry1.HasExistingData && entry1.FilesystemsSummary.Contains("NTFS"), "Disk 1 should report NTFS existing data");

            // 3. Test B: Verify wrong confirmation ID is rejected without modifying Disk 1
            string disk1HashBeforeReject = disk1Target.ComputeFullDiskSha256();
            var badTracker = new InstallerStepTracker();
            var badReq = new InstallationRequest
            {
                TargetDiskToken = entry1.IdentityToken,
                TypedDiskConfirmation = "DISK-0", // Wrong disk ID!
                Username = "alice",
                Hostname = "zonderq-box",
                PasswordBuffer = "SecurePass123".ToCharArray(),
                PasswordLength = 13,
                ConfirmPasswordBuffer = "SecurePass123".ToCharArray(),
                ConfirmPasswordLength = 13,
                TestKernelElfOverride = BuildSyntheticElfKernel(128 * 1024)
            };
            bool badResult = SystemInstallerEngine.ExecuteInstallation(badReq, badTracker, null, out string badErr);
            Assert(!badResult && badTracker.HasFailed, "Mismatched confirmation ID must fail closed");
            Assert(disk1Target.ComputeFullDiskSha256() == disk1HashBeforeReject, "Disk 1 must remain untouched after confirmation failure");
            Console.WriteLine("[Test B] Wrong confirmation rejected safely: " + badErr);

            // 4. Test E: Verify interrupted installation / simulated write error never reports success
            var errTracker = new InstallerStepTracker();
            var errReq = new InstallationRequest
            {
                TargetDiskToken = entry1.IdentityToken,
                TypedDiskConfirmation = entry1.DiskId,
                Username = "alice",
                Hostname = "zonderq-box",
                PasswordBuffer = "SecurePass123".ToCharArray(),
                PasswordLength = 13,
                ConfirmPasswordBuffer = "SecurePass123".ToCharArray(),
                ConfirmPasswordLength = 13,
                TestKernelElfOverride = BuildSyntheticElfKernel(256 * 1024),
                SimulateInterruptedCopyError = true
            };
            bool errResult = SystemInstallerEngine.ExecuteInstallation(errReq, errTracker, null, out string simErr);
            Assert(!errResult && errTracker.HasFailed && !errTracker.IsCompleted,
                "Interrupted installation must fail closed and never mark IsCompleted");
            Console.WriteLine("[Test E] Interrupted copy failed safely as expected: " + simErr);

            // Refresh catalog after Test E mutated Disk 1 so we have a fresh identity token for Disk 1
            catalog = DiskCatalog.DiscoverPhysicalDisks();
            entry1 = catalog[1];

            // 5. Test A/B/C/F: Execute complete dual-partition installation on Disk 1
            var tracker = new InstallerStepTracker();
            var req = new InstallationRequest
            {
                TargetDiskToken = entry1.IdentityToken,
                TypedDiskConfirmation = entry1.DiskId,
                PartitionLayout = InstallationPartitionLayout.DualEfiAndSystem,
                Username = "alice",
                Hostname = "zonderq-workstation",
                PasswordBuffer = "SecurePass123".ToCharArray(),
                PasswordLength = 13,
                ConfirmPasswordBuffer = "SecurePass123".ToCharArray(),
                ConfirmPasswordLength = 13,
                TestKernelElfOverride = BuildSyntheticElfKernel(256 * 1024)
            };

            bool ok = SystemInstallerEngine.ExecuteInstallation(
                req,
                tracker,
                () => Console.WriteLine("  -> Step " + tracker.CurrentStepIndex + " (" + tracker.ProgressPercent + "%): " + tracker.CurrentStepName),
                out string installErr);

            Assert(ok && tracker.IsCompleted && !tracker.HasFailed, "Full installation failed: " + installErr);
            for (int s = 0; s < InstallerStepTracker.StepCount; s++)
                Assert(tracker.GetStepState(s) == InstallerStepTracker.StateCompleted, "Step " + s + " not marked completed");

            // Verify sensitive password buffers were zeroed
            for (int i = 0; i < req.PasswordBuffer.Length; i++)
                Assert(req.PasswordBuffer[i] == '\0', "PasswordBuffer was not zeroed after installation");

            // 6. Test C: Verify Protected Disk 0 SHA-256 is 100% identical!
            string disk0HashAfter = disk0Protected.ComputeFullDiskSha256();
            Console.WriteLine("[Test C] Protected Disk 0 SHA-256 after install:  " + disk0HashAfter);
            Assert(disk0HashBefore == disk0HashAfter, "Protected Disk 0 was modified during Disk 1 installation!");

            // 7. Verify installed Partition Table, Limine BIOS stage, ESP, and ZONDERQDATA approval
            Assert(Mbr.IsMbr(disk1Target), "Disk 1 must have a valid MBR");
            Assert(disk1Target.RawBytes[3] == (byte)'L' && disk1Target.RawBytes[4] == (byte)'I' && disk1Target.RawBytes[5] == (byte)'M',
                "Limine BIOS stage 1 signature missing from Disk 1 MBR");

            List<MbrPartitionEntry> mbrEntries = Mbr.Parse(disk1Target);
            Assert(mbrEntries.Count == 2, "Expected 2 MBR partitions (ESP + ZONDERQDATA), found " + mbrEntries.Count);
            Assert(mbrEntries[0].SystemId == 0xEF, "Partition 1 must be 0xEF (EFI System Partition)");
            Assert(mbrEntries[1].SystemId == 0x0C, "Partition 2 must be 0x0C (FAT32 LBA)");

            // Verify DataVolumePolicy approves Partition 2 on Disk 1 and rejects Partition 1 (ESP)
            Partition installedEsp = null;
            Partition installedSys = null;
            foreach (Partition p in StorageManager.Partitions)
            {
                if (object.ReferenceEquals(p.Host, disk1Target))
                {
                    if (p.StartSector == mbrEntries[0].StartSector) installedEsp = p;
                    if (p.StartSector == mbrEntries[1].StartSector) installedSys = p;
                }
            }
            Assert(installedEsp != null && installedSys != null, "Rescanned partitions on Disk 1 not found");
            Assert(!ZonderqOS.DataVolumePolicy.IsApprovedDataPartition(installedEsp), "ESP must NOT be approved as ZONDERQDATA");
            Assert(ZonderqOS.DataVolumePolicy.IsApprovedDataPartition(installedSys), "System partition MUST be approved by DataVolumePolicy");

            Console.WriteLine("=== ALL UNIVERSAL INSTALLER SMOKE TESTS PASSED ===");
            return 0;
        }
    }
}
