using System;
using System.Text;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;
using Org.BouncyCastle.Crypto.Digests;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Storage
{
    public sealed class InstallationRequest
    {
        public DiskIdentityToken TargetDiskToken { get; set; }
        public string TypedDiskConfirmation { get; set; } = string.Empty;
        public InstallationPartitionLayout PartitionLayout { get; set; } = InstallationPartitionLayout.DualEfiAndSystem;
        public bool ZeroFillHeaderRegion { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Hostname { get; set; } = "zonderq-pc";
        public char[] PasswordBuffer { get; set; }
        public int PasswordLength { get; set; }
        public char[] ConfirmPasswordBuffer { get; set; }
        public int ConfirmPasswordLength { get; set; }

        // Optional override for host unit tests or simulated failure testing (ETAP 10 Test E)
        public byte[] TestKernelElfOverride { get; set; }
        public bool SimulateInterruptedCopyError { get; set; }

        public void ClearSensitiveData()
        {
            if (PasswordBuffer != null)
                Array.Clear(PasswordBuffer, 0, PasswordBuffer.Length);
            if (ConfirmPasswordBuffer != null)
                Array.Clear(ConfirmPasswordBuffer, 0, ConfirmPasswordBuffer.Length);
            PasswordLength = 0;
            ConfirmPasswordLength = 0;
        }
    }

    /// <summary>
    /// End-to-end bare-metal installation engine for ZonderqOS Gen3.
    /// Executes the 9-stage pipeline: disk verification, MBR partitioning, FAT32 formatting,
    /// Limine UEFI + BIOS & BootGuard installation, chunked kernel ELF streaming with SHA-256,
    /// A/B boot slot initialization, PBKDF2-HMAC-SHA256 account provisioning, and verification.
    /// </summary>
    public static class SystemInstallerEngine
    {
        private const string BootGuardResourceName = "Bootloader.bootguard.efi";
        private const string LimineEfiResourceName = "Bootloader.LIMINE.EFI";
        private const string LimineBiosSysResourceName = "Bootloader.limine-bios.sys";
        private const string EfiMountPoint = "/mnt/zinst_efi";
        private const string SysMountPoint = "/mnt/zinst_sys";
        private const int CopyChunkSize = 65536; // 64 KiB reusable buffer

        private static int mountSequence;

        public static bool ExecuteInstallation(
            InstallationRequest request,
            InstallerStepTracker tracker,
            Action onProgressUpdated,
            out string error)
        {
            error = null;
            if (tracker == null)
                tracker = new InstallerStepTracker();
            tracker.Reset();

            void NotifyProgress(int step, int pct)
            {
                tracker.BeginStep(step, pct);
                onProgressUpdated?.Invoke();
            }

            if (request == null)
            {
                error = "Installation request is null.";
                tracker.Fail(error);
                onProgressUpdated?.Invoke();
                return false;
            }

            bool efiMounted = false;
            bool sysMounted = false;
            string passwordString = null;
            string confirmString = null;

            try
            {
                // 0. Pre-flight input & payload verification BEFORE touching the target disk
                if (!InstallerInputValidator.ValidateUsername(request.Username, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                if (!InstallerInputValidator.ValidateHostname(request.Hostname, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                if (request.PasswordBuffer == null || request.PasswordLength <= 0 ||
                    request.PasswordLength > request.PasswordBuffer.Length)
                {
                    error = "Password cannot be empty.";
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                if (request.ConfirmPasswordBuffer == null ||
                    request.ConfirmPasswordLength != request.PasswordLength ||
                    request.ConfirmPasswordLength > request.ConfirmPasswordBuffer.Length)
                {
                    error = "Password and confirmation password do not match.";
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                passwordString = new string(request.PasswordBuffer, 0, request.PasswordLength);
                confirmString = new string(request.ConfirmPasswordBuffer, 0, request.ConfirmPasswordLength);

                if (!Crypto.FixedTimeEquals(passwordString, confirmString))
                {
                    error = "Password and confirmation password do not match.";
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                if (!PasswordPolicy.Validate(passwordString, out string policyReason))
                {
                    error = policyReason;
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                // Verify bootloader embedded assets exist before modifying disk
                byte[] bootGuardBytes = DiskFormattingEngine.LoadEmbeddedResourceBytes(BootGuardResourceName);
                byte[] limineEfiBytes = DiskFormattingEngine.LoadEmbeddedResourceBytes(LimineEfiResourceName);
                byte[] limineBiosSysBytes = DiskFormattingEngine.LoadEmbeddedResourceBytes(LimineBiosSysResourceName);

                if (bootGuardBytes == null || bootGuardBytes.Length < 1024 ||
                    limineEfiBytes == null || limineEfiBytes.Length < 1024 ||
                    limineBiosSysBytes == null || limineBiosSysBytes.Length < 1024)
                {
                    error = "Required embedded bootloader binaries (BootGuard / Limine) are missing from the kernel image.";
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                if (!HasAvailableKernelPayload(request, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                // Steps 0, 1, 2: Preparing disk, Creating partitions, Formatting filesystem
                NotifyProgress(0, 5);
                if (!DiskFormattingEngine.PrepareDiskForInstallation(
                    request.TargetDiskToken,
                    request.TypedDiskConfirmation,
                    request.PartitionLayout,
                    request.ZeroFillHeaderRegion,
                    (pct, stepName) =>
                    {
                        if (pct < 15) NotifyProgress(0, pct);
                        else if (pct < 26) NotifyProgress(1, pct);
                        else NotifyProgress(2, pct);
                    },
                    out PreparedInstallPartitions prepared,
                    out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                tracker.CompleteStep(0);
                tracker.CompleteStep(1);
                tracker.CompleteStep(2);

                // Step 3: Installing bootloader (Limine BIOS stage 1/2 + EFI System Partition files)
                NotifyProgress(3, 40);
                if (!DiskFormattingEngine.InstallLimineBiosBootloader(prepared.Device, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                string limineConfigText = BuildDefaultLimineConfig();

                EnsureVfsMountDirectories();

                if (prepared.EfiPartition != null)
                {
                    NotifyProgress(3, 44);
                    if (!TryMountPartitionAt(prepared.EfiPartition, EfiMountPoint, out error))
                    {
                        tracker.Fail(error);
                        onProgressUpdated?.Invoke();
                        return false;
                    }
                    efiMounted = true;

                    CreateDirVfs(EfiMountPoint + "/EFI");
                    CreateDirVfs(EfiMountPoint + "/EFI/BOOT");
                    CreateDirVfs(EfiMountPoint + "/boot");
                    CreateDirVfs(EfiMountPoint + "/boot/limine");

                    if (!WriteBytesVfs(EfiMountPoint + "/EFI/BOOT/BOOTX64.EFI", bootGuardBytes, out error) ||
                        !WriteBytesVfs(EfiMountPoint + "/EFI/BOOT/LIMINE.EFI", limineEfiBytes, out error) ||
                        !WriteBytesVfs(EfiMountPoint + "/boot/limine/limine-bios.sys", limineBiosSysBytes, out error) ||
                        !WriteTextVfs(EfiMountPoint + "/boot/limine/limine.conf", limineConfigText, out error) ||
                        !WriteTextVfs(EfiMountPoint + "/boot/limine.conf", limineConfigText, out error) ||
                        !WriteTextVfs(EfiMountPoint + "/limine.conf", limineConfigText, out error))
                    {
                        tracker.Fail("EFI partition bootloader write failed: " + error);
                        onProgressUpdated?.Invoke();
                        return false;
                    }

                    VfsManager.TryUnmount(EfiMountPoint);
                    efiMounted = false;
                    DiskFormattingEngine.MirrorPrimaryFatToSecondaryFat(prepared.EfiPartition);
                    prepared.EfiPartition.Flush();
                }

                tracker.CompleteStep(3);

                // Step 4: Copying system files onto ZONDERQDATA System Partition
                NotifyProgress(4, 52);
                if (!TryMountPartitionAt(prepared.SystemPartition, SysMountPoint, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }
                sysMounted = true;

                string[] fhsDirs = new string[]
                {
                    "/bin", "/boot", "/boot/slot_a", "/boot/slot_b", "/boot/limine",
                    "/EFI", "/EFI/BOOT", "/system", "/etc", "/etc/zonderq", "/etc/security",
                    "/usr", "/home", "/home/" + request.Username, "/root",
                    "/var", "/var/log", "/var/updates", "/var/updates/installed", "/var/updates/backup",
                    "/updates", "/tmp", "/mnt", "/media", "/opt", "/srv"
                };

                for (int i = 0; i < fhsDirs.Length; i++)
                {
                    if (!CreateDirVfs(SysMountPoint + fhsDirs[i]))
                    {
                        error = "Failed to create directory structure on target disk: " + fhsDirs[i];
                        tracker.Fail(error);
                        onProgressUpdated?.Invoke();
                        return false;
                    }
                }

                // Also place EFI & BIOS bootloader files on the ZONDERQDATA volume itself
                if (!WriteBytesVfs(SysMountPoint + "/EFI/BOOT/BOOTX64.EFI", bootGuardBytes, out error) ||
                    !WriteBytesVfs(SysMountPoint + "/EFI/BOOT/LIMINE.EFI", limineEfiBytes, out error) ||
                    !WriteBytesVfs(SysMountPoint + "/boot/limine/limine-bios.sys", limineBiosSysBytes, out error))
                {
                    tracker.Fail("System partition bootloader copy failed: " + error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                NotifyProgress(4, 56);
                if (!StreamKernelElfToTarget(
                    request,
                    SysMountPoint + "/boot/slot_a/ZonderqOS.elf",
                    SysMountPoint + "/boot/ZonderqOS.elf",
                    pct =>
                    {
                        tracker.UpdateStepProgress(pct);
                        onProgressUpdated?.Invoke();
                    },
                    out string kernelSha256,
                    out int kernelWrittenBytes,
                    out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                tracker.CompleteStep(4);

                // Step 5: Configuring boot slots (A/B metadata, SHA-256 digest, Limine configuration)
                NotifyProgress(5, 76);
                if (!WriteTextVfs(SysMountPoint + "/boot/slot_a/kernel.sha256", kernelSha256 + "\n", out error))
                {
                    tracker.Fail("Failed to write Slot A SHA-256 digest: " + error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                string bootStateText = BuildInitialBootStateConfig();
                if (!WriteTextVfs(SysMountPoint + "/boot/boot_state.cfg", bootStateText, out error) ||
                    !WriteTextVfs(SysMountPoint + "/boot/limine.conf", limineConfigText, out error) ||
                    !WriteTextVfs(SysMountPoint + "/limine.conf", limineConfigText, out error) ||
                    !WriteTextVfs(SysMountPoint + "/boot/limine/limine.conf", limineConfigText, out error))
                {
                    tracker.Fail("Failed to write A/B boot slot configuration: " + error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                tracker.CompleteStep(5);

                // Step 6: Setting up user account and system configuration (PBKDF2-HMAC-SHA256 v2)
                NotifyProgress(6, 84);
                if (!ProvisionUserAndSystemConfig(SysMountPoint, request.Username, request.Hostname, passwordString, out error))
                {
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                // Clear plaintext password strings immediately after hashing
                passwordString = null;
                confirmString = null;
                request.ClearSensitiveData();

                tracker.CompleteStep(6);

                // Step 7: Verifying installation
                NotifyProgress(7, 92);
                if (!VerifyInstalledFilesystem(
                    SysMountPoint,
                    request.Username,
                    request.Hostname,
                    kernelWrittenBytes,
                    kernelSha256,
                    out error))
                {
                    tracker.Fail("Installation verification failed: " + error);
                    onProgressUpdated?.Invoke();
                    return false;
                }

                tracker.CompleteStep(7);

                // Step 8: Finishing installation (sync & unmount)
                NotifyProgress(8, 97);
                if (!VfsManager.TryUnmount(SysMountPoint))
                {
                    error = "Failed to cleanly unmount target system partition.";
                    tracker.Fail(error);
                    onProgressUpdated?.Invoke();
                    return false;
                }
                sysMounted = false;

                DiskFormattingEngine.MirrorPrimaryFatToSecondaryFat(prepared.SystemPartition);
                prepared.SystemPartition.Flush();
                DiskFormattingEngine.WriteMbrInstallationCompleteMarker(prepared.Device);
                prepared.Device.Flush();

                tracker.CompleteAll();
                onProgressUpdated?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                error = "Installation exception: " + ex.Message;
                tracker.Fail(error);
                onProgressUpdated?.Invoke();
                return false;
            }
            finally
            {
                passwordString = null;
                confirmString = null;
                request?.ClearSensitiveData();

                if (efiMounted)
                {
                    try { VfsManager.TryUnmount(EfiMountPoint); } catch { }
                }
                if (sysMounted)
                {
                    try { VfsManager.TryUnmount(SysMountPoint); } catch { }
                }
            }
        }

        private static bool HasAvailableKernelPayload(InstallationRequest request, out string error)
        {
            error = null;
            if (request.TestKernelElfOverride != null && request.TestKernelElfOverride.Length >= 128)
            {
                if (request.TestKernelElfOverride[0] == 0x7F &&
                    request.TestKernelElfOverride[1] == (byte)'E' &&
                    request.TestKernelElfOverride[2] == (byte)'L' &&
                    request.TestKernelElfOverride[3] == (byte)'F')
                {
                    return true;
                }
                error = "Supplied kernel override does not have a valid ELF header.";
                return false;
            }

            if (LimineBootMediaDetector.TryGetBootKernelElf(out IntPtr addr, out int size) &&
                addr != IntPtr.Zero && size >= 1024)
            {
                return true;
            }

            if (VfsFileExistsWithMinSize("/boot/slot_a/ZonderqOS.elf", 1024) ||
                VfsFileExistsWithMinSize("/boot/ZonderqOS.elf", 1024))
            {
                return true;
            }

            error = "Could not locate running ZonderqOS.elf kernel payload in Limine memory or /boot.";
            return false;
        }

        private static unsafe bool StreamKernelElfToTarget(
            InstallationRequest request,
            string slotADestPath,
            string rootBootDestPath,
            Action<int> progressPctCallback,
            out string sha256Hex,
            out int totalBytesWritten,
            out string error)
        {
            sha256Hex = string.Empty;
            totalBytesWritten = 0;
            error = null;

            byte[] chunk = new byte[CopyChunkSize];
            var digest = new Sha256Digest();

            if (!VfsManager.TryCreateFile(slotADestPath, VfsMode.RegularFile) ||
                !VfsManager.TryOpenFile(slotADestPath, out IVfsFileHandle slotAHandle))
            {
                error = "Could not create kernel image at " + slotADestPath;
                return false;
            }

            if (!VfsManager.TryCreateFile(rootBootDestPath, VfsMode.RegularFile) ||
                !VfsManager.TryOpenFile(rootBootDestPath, out IVfsFileHandle rootBootHandle))
            {
                error = "Could not create fallback kernel image at " + rootBootDestPath;
                return false;
            }

            if (request.TestKernelElfOverride != null && request.TestKernelElfOverride.Length >= 128)
            {
                byte[] src = request.TestKernelElfOverride;
                int total = src.Length;
                int offset = 0;
                while (offset < total)
                {
                    if (request.SimulateInterruptedCopyError && offset >= total / 2)
                    {
                        error = "Simulated disk write error during system file copy.";
                        return false;
                    }

                    int len = Math.Min(CopyChunkSize, total - offset);
                    ReadOnlySpan<byte> slice = src.AsSpan(offset, len);
                    if (slotAHandle.Write(slice) != len || rootBootHandle.Write(slice) != len)
                    {
                        error = "Disk write error while copying kernel image.";
                        return false;
                    }

                    digest.BlockUpdate(src, offset, len);
                    offset += len;
                    totalBytesWritten += len;
                    int pct = 56 + (int)((offset * 18L) / Math.Max(1, total));
                    progressPctCallback?.Invoke(pct);
                }
            }
            else if (LimineBootMediaDetector.TryGetBootKernelElf(out IntPtr elfAddr, out int elfSize))
            {
                byte* basePtr = (byte*)elfAddr;
                int offset = 0;
                while (offset < elfSize)
                {
                    if (request.SimulateInterruptedCopyError && offset >= elfSize / 2)
                    {
                        error = "Simulated disk write error during system file copy.";
                        return false;
                    }

                    int len = Math.Min(CopyChunkSize, elfSize - offset);
                    for (int i = 0; i < len; i++)
                        chunk[i] = basePtr[offset + i];

                    ReadOnlySpan<byte> slice = chunk.AsSpan(0, len);
                    if (slotAHandle.Write(slice) != len || rootBootHandle.Write(slice) != len)
                    {
                        error = "Disk write error while streaming kernel image from memory.";
                        return false;
                    }

                    digest.BlockUpdate(chunk, 0, len);
                    offset += len;
                    totalBytesWritten += len;
                    int pct = 56 + (int)((offset * 18L) / Math.Max(1, elfSize));
                    progressPctCallback?.Invoke(pct);
                }
            }
            else
            {
                string srcPath = VfsFileExistsWithMinSize("/boot/slot_a/ZonderqOS.elf", 1024)
                    ? "/boot/slot_a/ZonderqOS.elf"
                    : "/boot/ZonderqOS.elf";

                if (!VfsManager.TryStat(srcPath, out VfsStat stat) ||
                    !VfsManager.TryOpenFile(srcPath, out IVfsFileHandle srcHandle))
                {
                    error = "Could not open source kernel image at " + srcPath;
                    return false;
                }

                int total = (int)stat.Size;
                int offset = 0;
                while (offset < total)
                {
                    if (request.SimulateInterruptedCopyError && offset >= total / 2)
                    {
                        error = "Simulated disk write error during system file copy.";
                        return false;
                    }

                    int n = (int)srcHandle.Read(chunk);
                    if (n <= 0)
                        break;

                    ReadOnlySpan<byte> slice = chunk.AsSpan(0, n);
                    if (slotAHandle.Write(slice) != n || rootBootHandle.Write(slice) != n)
                    {
                        error = "Disk write error while copying kernel image.";
                        return false;
                    }

                    digest.BlockUpdate(chunk, 0, n);
                    offset += n;
                    totalBytesWritten += n;
                    int pct = 56 + (int)((offset * 18L) / Math.Max(1, total));
                    progressPctCallback?.Invoke(pct);
                }
            }

            slotAHandle.TryFlush();
            rootBootHandle.TryFlush();

            byte[] hashBytes = new byte[digest.GetDigestSize()];
            digest.DoFinal(hashBytes, 0);
            var sb = new StringBuilder(hashBytes.Length * 2);
            for (int i = 0; i < hashBytes.Length; i++)
                sb.Append(hashBytes[i].ToString("x2"));
            sha256Hex = sb.ToString();
            return totalBytesWritten >= 128;
        }

        private static bool ProvisionUserAndSystemConfig(
            string rootPrefix,
            string username,
            string hostname,
            string password,
            out string error)
        {
            error = null;

            string rootSalt = Crypto.GenerateSalt();
            string rootHash = Crypto.HashPasswordV2(password, rootSalt, Crypto.PasswordHashIterations);
            string userSalt = Crypto.GenerateSalt();
            string userHash = Crypto.HashPasswordV2(password, userSalt, Crypto.PasswordHashIterations);

            if (string.IsNullOrEmpty(rootHash) || string.IsNullOrEmpty(userHash))
            {
                error = "Failed to derive PBKDF2-HMAC-SHA256 account credentials.";
                return false;
            }

            string rootCred = "v2$" + Crypto.PasswordHashIterations + "$" + rootSalt + "$" + rootHash;
            string userCred = "v2$" + Crypto.PasswordHashIterations + "$" + userSalt + "$" + userHash;

            string passwdContent =
                "root:x:0:/root\n" +
                username + ":x:1000:/home/" + username + "\n";

            string shadowContent =
                "root:" + rootCred + "\n" +
                username + ":" + userCred + "\n";

            string permissionsContent =
                "/etc/passwd|root|644\n" +
                "/etc/shadow|root|600\n" +
                "/etc/security|root|700\n" +
                "/etc/security/permissions.db|root|600\n" +
                "/root|root|700\n" +
                "/home/" + username + "|" + username + "|700\n";

            string settingsContent =
                "# ZOnderqOS Gen3 system settings\n" +
                "schema=1\n" +
                "performance=1\n" +
                "clock_seconds=false\n" +
                "taskbar_date=true\n" +
                "tray_status=true\n" +
                "desktop_icons=true\n" +
                "boot_gui=true\n" +
                "timezone=2\n" +
                "desktop_background=0\n" +
                "accent_theme=0\n" +
                "language=en-US\n" +
                "auto_lock_minutes=5\n" +
                "network_dhcp=true\n" +
                "static_ip=10.0.2.15\n" +
                "static_mask=255.255.255.0\n" +
                "static_gateway=10.0.2.2\n" +
                "dns_server=1.1.1.1\n";

            string osReleaseContent =
                "NAME=\"ZonderqOS\"\n" +
                "VERSION=\"" + KernelBuildInfo.KernelVersion + "\"\n" +
                "ID=zonderqos\n" +
                "ID_LIKE=cosmos\n" +
                "VERSION_ID=\"" + KernelBuildInfo.KernelVersion + "\"\n" +
                "BUILD_ID=\"" + KernelBuildInfo.KernelBuild + "\"\n" +
                "PRETTY_NAME=\"ZonderqOS " + KernelBuildInfo.KernelVersion + " (Cosmos 3.0.97 Gen3)\"\n";

            string updateCfgContent =
                "CHANNEL=stable\n" +
                "UPDATER_VERSION=3.0\n" +
                "INSTALLED_VERSION=" + KernelBuildInfo.KernelVersion + "\n" +
                "INSTALLED_BUILD=" + KernelBuildInfo.KernelBuild + "\n" +
                "LAST_NOTIFIED=\n";

            if (!WriteTextVfs(rootPrefix + "/etc/passwd", passwdContent, out error) ||
                !WriteTextVfs(rootPrefix + "/etc/shadow", shadowContent, out error) ||
                !WriteTextVfs(rootPrefix + "/etc/security/permissions.db", permissionsContent, out error) ||
                !WriteTextVfs(rootPrefix + "/etc/hostname", hostname + "\n", out error) ||
                !WriteTextVfs(rootPrefix + "/etc/zonderq/settings.conf", settingsContent, out error) ||
                !WriteTextVfs(rootPrefix + "/etc/os-release", osReleaseContent, out error) ||
                !WriteTextVfs(rootPrefix + "/etc/update.cfg", updateCfgContent, out error) ||
                !WriteTextVfs(rootPrefix + "/var/error_log.txt", "=== ZonderqOS System Log Initialized ===\n", out error))
            {
                return false;
            }

            return true;
        }

        private static bool VerifyInstalledFilesystem(
            string rootPrefix,
            string expectedUsername,
            string expectedHostname,
            int expectedKernelBytes,
            string expectedKernelSha256,
            out string error)
        {
            error = null;

            // 1. Verify kernel ELF header, exact byte size, and SHA-256 on disk
            string slotAKernel = rootPrefix + "/boot/slot_a/ZonderqOS.elf";
            if (!VfsManager.TryStat(slotAKernel, out VfsStat kernelStat) ||
                (int)kernelStat.Size != expectedKernelBytes)
            {
                error = "Installed Slot A kernel size does not match source payload.";
                return false;
            }

            if (!VfsManager.TryOpenFile(slotAKernel, out IVfsFileHandle kh))
            {
                error = "Cannot open installed Slot A kernel for verification.";
                return false;
            }

            byte[] buf = new byte[CopyChunkSize];
            var digest = new Sha256Digest();
            int totalRead = 0;
            bool checkedMagic = false;
            while (totalRead < expectedKernelBytes)
            {
                int n = (int)kh.Read(buf);
                if (n <= 0)
                    break;

                if (!checkedMagic)
                {
                    if (n < 4 || buf[0] != 0x7F || buf[1] != (byte)'E' || buf[2] != (byte)'L' || buf[3] != (byte)'F')
                    {
                        error = "Installed Slot A kernel does not have a valid ELF magic header.";
                        return false;
                    }
                    checkedMagic = true;
                }

                digest.BlockUpdate(buf, 0, n);
                totalRead += n;
            }

            if (totalRead != expectedKernelBytes)
            {
                error = "Short read while verifying installed Slot A kernel.";
                return false;
            }

            byte[] actualHash = new byte[digest.GetDigestSize()];
            digest.DoFinal(actualHash, 0);
            var sb = new StringBuilder(actualHash.Length * 2);
            for (int i = 0; i < actualHash.Length; i++)
                sb.Append(actualHash[i].ToString("x2"));

            if (!Crypto.FixedTimeEquals(sb.ToString(), expectedKernelSha256))
            {
                error = "Installed Slot A kernel SHA-256 checksum mismatch.";
                return false;
            }

            // 2. Verify bootloader files
            if (!VfsFileExistsWithMinSize(rootPrefix + "/EFI/BOOT/BOOTX64.EFI", 1024) ||
                !VfsFileExistsWithMinSize(rootPrefix + "/EFI/BOOT/LIMINE.EFI", 1024) ||
                !VfsFileExistsWithMinSize(rootPrefix + "/boot/limine/limine-bios.sys", 1024))
            {
                error = "Bootloader files are missing on installed volume.";
                return false;
            }

            // 3. Verify Limine and A/B boot state configs
            string limineCfg = ReadTextVfs(rootPrefix + "/boot/limine/limine.conf");
            if (string.IsNullOrEmpty(limineCfg) ||
                limineCfg.IndexOf("fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf", StringComparison.Ordinal) < 0)
            {
                error = "Installed limine.conf is missing or invalid.";
                return false;
            }

            string bootState = ReadTextVfs(rootPrefix + "/boot/boot_state.cfg");
            if (string.IsNullOrEmpty(bootState) ||
                bootState.IndexOf("ACTIVE_SLOT=A", StringComparison.Ordinal) < 0)
            {
                error = "Installed boot_state.cfg is missing or invalid.";
                return false;
            }

            // 4. Verify account database and hostname
            string passwd = ReadTextVfs(rootPrefix + "/etc/passwd");
            string shadow = ReadTextVfs(rootPrefix + "/etc/shadow");
            string hostname = ReadTextVfs(rootPrefix + "/etc/hostname");

            if (string.IsNullOrEmpty(passwd) || passwd.IndexOf(expectedUsername + ":x:1000:", StringComparison.Ordinal) < 0)
            {
                error = "Installed /etc/passwd does not contain the configured user account.";
                return false;
            }

            if (string.IsNullOrEmpty(shadow) ||
                shadow.IndexOf("root:v2$", StringComparison.Ordinal) < 0 ||
                shadow.IndexOf(expectedUsername + ":v2$", StringComparison.Ordinal) < 0)
            {
                error = "Installed /etc/shadow does not contain hashed v2 credentials.";
                return false;
            }

            if (string.IsNullOrEmpty(hostname) || !string.Equals(hostname.Trim(), expectedHostname, StringComparison.Ordinal))
            {
                error = "Installed /etc/hostname does not match the configured computer name.";
                return false;
            }

            return true;
        }

        public static string BuildDefaultLimineConfig()
        {
            return
                "# Cosmos Gen3 / Limine v10 A/B dual-slot boot configuration.\n" +
                "timeout: 5\n" +
                "default_entry: 1\n" +
                "remember_last_entry: no\n\n" +
                "/ZonderqOS Slot A\n" +
                "    protocol: limine\n" +
                "    resolution: 1366x768x32\n" +
                "    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n\n" +
                "/ZonderqOS Slot B\n" +
                "    protocol: limine\n" +
                "    resolution: 1366x768x32\n" +
                "    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf\n\n" +
                "/ZonderqOS Recovery ISO\n" +
                "    protocol: limine\n" +
                "    resolution: 1366x768x32\n" +
                "    path: boot():/boot/ZonderqOS.elf\n";
        }

        public static string BuildInitialBootStateConfig()
        {
            return
                "# ZonderqOS A/B Bootloader State Configuration\n" +
                "ACTIVE_SLOT=A\n" +
                "FALLBACK_SLOT=B\n" +
                "PENDING_SLOT=\n" +
                "BOOT_ATTEMPTS=0\n" +
                "BOOTGUARD_PREBOOT=0\n" +
                "BOOTGUARD_FALLBACK=0\n" +
                "MAX_BOOT_ATTEMPTS=3\n" +
                "SLOT_A_STATUS=Healthy\n" +
                "SLOT_A_VERSION=" + KernelBuildInfo.KernelVersion + "\n" +
                "SLOT_A_BUILD=" + KernelBuildInfo.KernelBuild + "\n" +
                "SLOT_B_STATUS=Empty\n" +
                "SLOT_B_VERSION=\n" +
                "SLOT_B_BUILD=0\n";
        }

        private static void EnsureVfsMountDirectories()
        {
            CreateDirVfs("/mnt");
            CreateDirVfs(EfiMountPoint);
            CreateDirVfs(SysMountPoint);
        }

        private static bool TryMountPartitionAt(Partition partition, string mountPoint, out string error)
        {
            error = null;
            if (partition == null)
            {
                error = "Cannot mount null partition.";
                return false;
            }

            try
            {
                VfsManager.TryUnmount(mountPoint);
            }
            catch
            {
            }

            mountSequence++;
            string fsName = "zinstfat_" + mountSequence;
            var fatDriver = new FatFileSystemType(partition);
            if (!VfsManager.RegisterFileSystem(fsName, fatDriver))
            {
                error = "Could not register VFS driver instance " + fsName + ".";
                return false;
            }

            if (!VfsManager.TryMount(fsName, ReadOnlySpan<char>.Empty, MountFlags.None, mountPoint, out _))
            {
                error = "Could not mount target FAT32 partition at " + mountPoint + ".";
                return false;
            }

            return true;
        }

        private static bool CreateDirVfs(string path)
        {
            if (VfsManager.TryStat(path, out _))
                return true;
            return VfsManager.TryCreateDirectory(path, VfsMode.Directory) || VfsManager.TryStat(path, out _);
        }

        private static bool WriteBytesVfs(string path, byte[] data, out string error)
        {
            error = null;
            if (data == null)
            {
                error = "Null byte array for " + path;
                return false;
            }

            try
            {
                if (VfsManager.TryStat(path, out _))
                    VfsManager.TryUnlink(path);

                if (!VfsManager.TryCreateFile(path, VfsMode.RegularFile) ||
                    !VfsManager.TryOpenFile(path, out IVfsFileHandle handle))
                {
                    error = "Cannot create file " + path;
                    return false;
                }

                int offset = 0;
                while (offset < data.Length)
                {
                    int len = Math.Min(CopyChunkSize, data.Length - offset);
                    long written = handle.Write(data.AsSpan(offset, len));
                    if (written != len)
                    {
                        error = "Short write on file " + path;
                        return false;
                    }
                    offset += len;
                }

                handle.TryFlush();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static bool WriteTextVfs(string path, string text, out string error)
        {
            byte[] utf8 = Encoding.UTF8.GetBytes(text ?? string.Empty);
            return WriteBytesVfs(path, utf8, out error);
        }

        private static string ReadTextVfs(string path)
        {
            try
            {
                if (!VfsManager.TryStat(path, out VfsStat stat) || stat.Size == 0 || stat.Size > 128 * 1024)
                    return string.Empty;

                if (!VfsManager.TryOpenFile(path, out IVfsFileHandle handle))
                    return string.Empty;

                byte[] buf = new byte[(int)stat.Size];
                int read = (int)handle.Read(buf);
                if (read <= 0)
                    return string.Empty;

                return Encoding.UTF8.GetString(buf, 0, read);
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool VfsFileExistsWithMinSize(string path, ulong minBytes)
        {
            try
            {
                return VfsManager.TryStat(path, out VfsStat stat) && stat.Size >= minBytes;
            }
            catch
            {
                return false;
            }
        }
    }
}
