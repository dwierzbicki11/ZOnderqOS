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
            ZonderqOS.SecurityContext.SetAuthenticated("root", "/root", 0);
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

            // Test B2: Non-root user must be rejected by SystemInstallerEngine and DiskFormattingEngine
            ZonderqOS.SecurityContext.SetAuthenticated("alice", "/home/alice", 1000);
            var nonRootTracker = new InstallerStepTracker();
            var nonRootReq = new InstallationRequest
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
            bool nonRootOk = SystemInstallerEngine.ExecuteInstallation(nonRootReq, nonRootTracker, null, out string nonRootErr);
            Assert(!nonRootOk && nonRootErr != null && nonRootErr.Contains("Root privileges", StringComparison.OrdinalIgnoreCase),
                "Non-root user was not rejected by SystemInstallerEngine!");
            bool nonRootFmt = DiskFormattingEngine.InitializeDiskWithSingleFat32Partition(
                entry1.IdentityToken, entry1.DiskId, "TESTVOL", out string nonRootFmtErr);
            Assert(!nonRootFmt && nonRootFmtErr != null && nonRootFmtErr.Contains("Root privileges", StringComparison.OrdinalIgnoreCase),
                "Non-root user was not rejected by DiskFormattingEngine!");
            Console.WriteLine("[Test B2] Non-root destructive disk operation rejected safely: " + nonRootErr);
            ZonderqOS.SecurityContext.SetAuthenticated("root", "/root", 0);

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

            // 8. Test G: Non-Destructive Dual-Boot Installation Alongside Existing Kali Linux + Windows ESP
            Console.WriteLine("[Test G] Preparing 220 MB disk with existing ESP (/EFI/kali/grubx64.efi + /EFI/Microsoft/Boot/bootmgfw.efi) + Kali Linux EXT4 + 115 MB free space...");
            var disk2DualBoot = new VirtualTestDisk("nvme0-dualboot-kali-win", 220);
            Mbr.Create(disk2DualBoot);
            const ulong espStart = 2048UL;
            const ulong espCount = 131072UL; // 64 MiB ESP
            const ulong kaliStart = espStart + espCount; // 133120
            const ulong kaliCount = 81920UL; // 40 MiB Kali EXT4
            Assert(Mbr.AddPartition(disk2DualBoot, 0, 0xEF, espStart, espCount), "Add ESP to disk2 failed");
            Assert(Mbr.AddPartition(disk2DualBoot, 1, 0x83, kaliStart, kaliCount), "Add Kali partition to disk2 failed");

            // Set non-zero MBR disk signature on disk2
            disk2DualBoot.RawBytes[440] = 0x77;
            disk2DualBoot.RawBytes[441] = 0x88;
            disk2DualBoot.RawBytes[442] = 0x99;
            disk2DualBoot.RawBytes[443] = 0xAA;

            // Populate Kali Linux EXT4 partition with superblock magic 0xEF53 and deterministic payload
            int kaliByteOffset = (int)(kaliStart * 512UL);
            int kaliByteLen = (int)(kaliCount * 512UL);
            for (int i = 0; i < kaliByteLen; i += 4096)
            {
                disk2DualBoot.RawBytes[kaliByteOffset + i] = (byte)'K';
                disk2DualBoot.RawBytes[kaliByteOffset + i + 1] = (byte)'A';
                disk2DualBoot.RawBytes[kaliByteOffset + i + 2] = (byte)'L';
                disk2DualBoot.RawBytes[kaliByteOffset + i + 3] = (byte)'I';
            }
            disk2DualBoot.RawBytes[kaliByteOffset + 1024 + 56] = 0x53;
            disk2DualBoot.RawBytes[kaliByteOffset + 1024 + 57] = 0xEF;

            var kaliDigestBefore = new Sha256Digest();
            kaliDigestBefore.BlockUpdate(disk2DualBoot.RawBytes, kaliByteOffset, kaliByteLen);
            byte[] kaliHashBeforeBytes = new byte[kaliDigestBefore.GetDigestSize()];
            kaliDigestBefore.DoFinal(kaliHashBeforeBytes, 0);
            string kaliShaBefore = BitConverter.ToString(kaliHashBeforeBytes).Replace("-", "").ToLowerInvariant();

            StorageManager.RegisterDevice(disk2DualBoot);

            // Format existing ESP on disk2 and seed /EFI/kali/grubx64.efi, /EFI/Microsoft/Boot/bootmgfw.efi, and foreign /EFI/BOOT/BOOTX64.EFI
            Partition disk2Esp = null;
            foreach (Partition p in StorageManager.Partitions)
            {
                if (object.ReferenceEquals(p.Host, disk2DualBoot) && p.StartSector == espStart)
                {
                    disk2Esp = p;
                    break;
                }
            }
            Assert(disk2Esp != null, "disk2 ESP partition not found");
            Assert(DiskFormattingEngine.FormatAndVerifyFat32(disk2Esp, "SYSTEM_ESP ", false, out string espFmtErr), "Format disk2 ESP failed: " + espFmtErr);

            Assert(VfsManager.RegisterFileSystem("testg_esp_seed", new FatFileSystemType(disk2Esp)), "Register testg_esp_seed failed");
            VfsManager.TryCreateDirectory("/mnt", VfsMode.Directory);
            VfsManager.TryCreateDirectory("/mnt/testg_seed", VfsMode.Directory);
            Assert(VfsManager.TryMount("testg_esp_seed", ReadOnlySpan<char>.Empty, MountFlags.None, "/mnt/testg_seed", out _), "Mount testg_seed failed");

            VfsManager.TryCreateDirectory("/mnt/testg_seed/EFI", VfsMode.Directory);
            VfsManager.TryCreateDirectory("/mnt/testg_seed/EFI/kali", VfsMode.Directory);
            VfsManager.TryCreateDirectory("/mnt/testg_seed/EFI/Microsoft", VfsMode.Directory);
            VfsManager.TryCreateDirectory("/mnt/testg_seed/EFI/Microsoft/Boot", VfsMode.Directory);
            VfsManager.TryCreateDirectory("/mnt/testg_seed/EFI/BOOT", VfsMode.Directory);

            byte[] kaliGrubBytes = Encoding.ASCII.GetBytes("KALI_LINUX_GRUBX64_EFI_BOOTLOADER_BINARY_PAYLOAD");
            byte[] winBootBytes = Encoding.ASCII.GetBytes("MICROSOFT_WINDOWS_BOOTMGFW_EFI_BINARY_PAYLOAD");
            byte[] foreignBootx64 = Encoding.ASCII.GetBytes("FOREIGN_PREEXISTING_FALLBACK_BOOTX64_EFI_PAYLOAD");

            WriteSeedVfsFile("/mnt/testg_seed/EFI/kali/grubx64.efi", kaliGrubBytes);
            WriteSeedVfsFile("/mnt/testg_seed/EFI/Microsoft/Boot/bootmgfw.efi", winBootBytes);
            WriteSeedVfsFile("/mnt/testg_seed/EFI/BOOT/BOOTX64.EFI", foreignBootx64);
            Assert(VfsManager.TryUnmount("/mnt/testg_seed"), "Unmount testg_seed failed");

            catalog = DiskCatalog.DiscoverPhysicalDisks();
            Assert(catalog.Count == 3, "Expected 3 physical disks, found " + catalog.Count);
            DiskCatalogEntry entry2 = catalog[2];

            DualBootDiskInspection dbInfo = DualBootManager.InspectDiskForDualBoot(entry2);
            Console.WriteLine("[Test G] DualBootInspection: CanAllocate=" + dbInfo.CanAllocateFromFreeSpace +
                              ", FreeMB=" + dbInfo.FreeSpaceMegabytes +
                              ", HasESP=" + dbInfo.HasExistingEfiPartition +
                              ", DetectedOS=" + dbInfo.DetectedOsSummary);
            Assert(dbInfo.CanAllocateFromFreeSpace && dbInfo.FreeSpaceMegabytes >= 100, "Expected >= 100 MB unallocated space for Dual-Boot");
            Assert(dbInfo.HasExistingEfiPartition && dbInfo.ExistingEfiStartLba == espStart, "Expected existing ESP detection at LBA 2048");
            Assert(dbInfo.DetectedOsSummary.Contains("Linux", StringComparison.OrdinalIgnoreCase), "Expected Kali/Linux detection in DetectedOsSummary");

            var dbTracker = new InstallerStepTracker();
            var dbReq = new InstallationRequest
            {
                TargetDiskToken = entry2.IdentityToken,
                TypedDiskConfirmation = entry2.DiskId,
                PartitionLayout = InstallationPartitionLayout.DualBootAlongsideExistingOs,
                DualBootTargetPartitionStartLba = 0UL, // Allocate from unallocated tail space
                Username = "zonderq",
                Hostname = "zonderq-dualboot",
                PasswordBuffer = "DualBootPass123".ToCharArray(),
                PasswordLength = 15,
                ConfirmPasswordBuffer = "DualBootPass123".ToCharArray(),
                ConfirmPasswordLength = 15,
                TestKernelElfOverride = BuildSyntheticElfKernel(256 * 1024)
            };

            bool dbOk = SystemInstallerEngine.ExecuteInstallation(
                dbReq,
                dbTracker,
                () => Console.WriteLine("  [DualBoot] -> Step " + dbTracker.CurrentStepIndex + " (" + dbTracker.ProgressPercent + "%): " + dbTracker.CurrentStepName),
                out string dbInstallErr);

            Assert(dbOk && dbTracker.IsCompleted && !dbTracker.HasFailed, "Dual-Boot installation failed: " + dbInstallErr);

            // Verify Kali Linux EXT4 partition bytes are 100% bit-for-bit identical!
            var kaliDigestAfter = new Sha256Digest();
            kaliDigestAfter.BlockUpdate(disk2DualBoot.RawBytes, kaliByteOffset, kaliByteLen);
            byte[] kaliHashAfterBytes = new byte[kaliDigestAfter.GetDigestSize()];
            kaliDigestAfter.DoFinal(kaliHashAfterBytes, 0);
            string kaliShaAfter = BitConverter.ToString(kaliHashAfterBytes).Replace("-", "").ToLowerInvariant();
            Console.WriteLine("[Test G] Kali Linux EXT4 partition SHA-256 before: " + kaliShaBefore);
            Console.WriteLine("[Test G] Kali Linux EXT4 partition SHA-256 after:  " + kaliShaAfter);
            Assert(kaliShaBefore == kaliShaAfter, "Kali Linux EXT4 partition was modified during Dual-Boot install!");

            // Verify MBR now has 3 partitions: 0=0xEF (ESP), 1=0x83 (Kali Linux), 2=0x0C (ZONDERQDATA)
            List<MbrPartitionEntry> dbMbr = Mbr.Parse(disk2DualBoot);
            Assert(dbMbr.Count == 3, "Expected 3 MBR partitions after Dual-Boot install, found " + dbMbr.Count);
            Assert(dbMbr[0].SystemId == 0xEF && dbMbr[0].StartSector == espStart, "Partition 1 (ESP) altered!");
            Assert(dbMbr[1].SystemId == 0x83 && dbMbr[1].StartSector == kaliStart, "Partition 2 (Kali 0x83) altered!");
            Assert(dbMbr[2].SystemId == 0x0C && dbMbr[2].StartSector == kaliStart + kaliCount, "Partition 3 (ZONDERQDATA 0x0C) not created at expected LBA!");

            Partition dbEspPart = null;
            Partition dbSysPart = null;
            foreach (Partition p in StorageManager.Partitions)
            {
                if (object.ReferenceEquals(p.Host, disk2DualBoot))
                {
                    if (p.StartSector == dbMbr[0].StartSector) dbEspPart = p;
                    if (p.StartSector == dbMbr[2].StartSector) dbSysPart = p;
                }
            }
            Assert(dbEspPart != null && dbSysPart != null, "Dual-Boot ESP or System partition not found after rescan");
            Assert(ZonderqOS.DataVolumePolicy.IsApprovedDataPartition(dbSysPart), "Dual-Boot ZONDERQDATA partition must be approved by DataVolumePolicy");

            // Mount ESP and verify Kali GRUB, Windows Boot Manager, backed-up BOOTX64_PREV.EFI, and ZonderqOS BootGuard/Limine coexist
            Assert(VfsManager.RegisterFileSystem("testg_esp_verify", new FatFileSystemType(dbEspPart)), "Register testg_esp_verify failed");
            VfsManager.TryCreateDirectory("/mnt/testg_esp_v", VfsMode.Directory);
            Assert(VfsManager.TryMount("testg_esp_verify", ReadOnlySpan<char>.Empty, MountFlags.None, "/mnt/testg_esp_v", out _), "Mount testg_esp_v failed");
            Assert(ReadSeedVfsFile("/mnt/testg_esp_v/EFI/kali/grubx64.efi") == Encoding.ASCII.GetString(kaliGrubBytes),
                "Existing /EFI/kali/grubx64.efi on ESP was not preserved!");
            Assert(ReadSeedVfsFile("/mnt/testg_esp_v/EFI/Microsoft/Boot/bootmgfw.efi") == Encoding.ASCII.GetString(winBootBytes),
                "Existing /EFI/Microsoft/Boot/bootmgfw.efi on ESP was not preserved!");
            Assert(ReadSeedVfsFile("/mnt/testg_esp_v/EFI/BOOT/BOOTX64_PREV.EFI") == Encoding.ASCII.GetString(foreignBootx64),
                "Pre-existing foreign /EFI/BOOT/BOOTX64.EFI was not backed up to /EFI/BOOT/BOOTX64_PREV.EFI!");
            Assert(VfsManager.TryStat("/mnt/testg_esp_v/EFI/BOOT/LIMINE.EFI", out VfsStat limStat) && limStat.Size > 1024,
                "LIMINE.EFI missing on shared ESP");
            Assert(VfsManager.TryUnmount("/mnt/testg_esp_v"), "Unmount testg_esp_v failed");

            // Mount Dual-Boot ZONDERQDATA and verify limine.conf contains Kali Linux, Windows Boot Manager, and Previous UEFI OS chainload entries
            Assert(VfsManager.RegisterFileSystem("testg_sys_verify", new FatFileSystemType(dbSysPart)), "Register testg_sys_verify failed");
            VfsManager.TryCreateDirectory("/mnt/testg_sys_v", VfsMode.Directory);
            Assert(VfsManager.TryMount("testg_sys_verify", ReadOnlySpan<char>.Empty, MountFlags.None, "/mnt/testg_sys_v", out _), "Mount testg_sys_v failed");
            string dbLimineConf = ReadSeedVfsFile("/mnt/testg_sys_v/boot/limine/limine.conf");
            string dbEntriesCfg = ReadSeedVfsFile("/mnt/testg_sys_v/boot/dualboot_entries.cfg");
            Console.WriteLine("[Test G] Generated /boot/dualboot_entries.cfg:\n" + dbEntriesCfg.Trim());
            Assert(dbLimineConf.Contains("/Kali Linux (GRUB UEFI)") && dbLimineConf.Contains("boot():/EFI/kali/grubx64.efi"),
                "limine.conf missing Kali Linux UEFI chainload entry!");
            Assert(dbLimineConf.Contains("/Windows Boot Manager") && dbLimineConf.Contains("boot():/EFI/Microsoft/Boot/bootmgfw.efi"),
                "limine.conf missing Windows Boot Manager UEFI chainload entry!");
            Assert(dbLimineConf.Contains("/Previous UEFI OS (Fallback Bootloader)") && dbLimineConf.Contains("boot():/EFI/BOOT/BOOTX64_PREV.EFI"),
                "limine.conf missing backed-up BOOTX64_PREV.EFI chainload entry!");
            Assert(VfsManager.TryUnmount("/mnt/testg_sys_v"), "Unmount testg_sys_v failed");

            Console.WriteLine("=== ALL UNIVERSAL INSTALLER SMOKE TESTS PASSED ===");
            return 0;
        }

        private static void WriteSeedVfsFile(string path, byte[] data)
        {
            Assert(VfsManager.TryCreateFile(path, VfsMode.RegularFile), "Create seed file failed: " + path);
            Assert(VfsManager.TryOpenFile(path, out IVfsFileHandle h), "Open seed file failed: " + path);
            try
            {
                Assert(h.Write(data) == data.Length, "Write seed file failed: " + path);
                h.TryFlush();
            }
            finally
            {
                (h as IDisposable)?.Dispose();
            }
        }

        private static string ReadSeedVfsFile(string path)
        {
            Assert(VfsManager.TryStat(path, out VfsStat st) && st.Size > 0, "Stat seed file failed: " + path);
            Assert(VfsManager.TryOpenFile(path, out IVfsFileHandle h), "Open seed file for read failed: " + path);
            try
            {
                byte[] buf = new byte[(int)st.Size];
                int r = (int)h.Read(buf);
                return Encoding.UTF8.GetString(buf, 0, r);
            }
            finally
            {
                (h as IDisposable)?.Dispose();
            }
        }
    }
}
