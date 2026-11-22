using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.Storage
{
    public sealed class DualBootEntry
    {
        public string Title { get; set; } = string.Empty;
        public string Protocol { get; set; } = "efi";
        public string PathOrDrive { get; set; } = string.Empty;
        public int PartitionIndex { get; set; }
        public string Comment { get; set; } = string.Empty;
        public string DetectedOsKind { get; set; } = string.Empty;
    }

    public sealed class DualBootCandidatePartition
    {
        public int PartitionNumber { get; set; }
        public ulong StartLba { get; set; }
        public ulong SectorCount { get; set; }
        public ulong SizeMegabytes { get; set; }
        public string FilesystemType { get; set; } = string.Empty;
        public string VolumeLabel { get; set; } = string.Empty;
        public string PartitionType { get; set; } = string.Empty;
    }

    public sealed class DualBootDiskInspection
    {
        public bool IsValid { get; set; }
        public bool IsGpt { get; set; }
        public bool CanAllocateFromFreeSpace { get; set; }
        public ulong FreeSpaceStartLba { get; set; }
        public ulong FreeSpaceSectors { get; set; }
        public ulong FreeSpaceMegabytes { get; set; }
        public int FreeMbrSlotIndex { get; set; } = -1;
        public int FreeGptSlotIndex { get; set; } = -1;
        public bool HasExistingEfiPartition { get; set; }
        public ulong ExistingEfiStartLba { get; set; }
        public ulong ExistingEfiSectors { get; set; }
        public int ForeignPartitionCount { get; set; }
        public string DetectedOsSummary { get; set; } = "None detected";
        public List<DualBootCandidatePartition> CandidateTargetPartitions { get; } = new List<DualBootCandidatePartition>();
        public string DiagnosticNote { get; set; } = string.Empty;
    }

    /// <summary>
    /// Non-destructive Dual-Boot manager for ZonderqOS:
    ///   1. Inspects disks for unallocated free space, existing EFI System Partitions,
    ///      foreign OS partitions (Kali Linux, Ubuntu, Debian, Windows NTFS, etc.),
    ///      and eligible target partitions that can be formatted as ZONDERQDATA.
    ///   2. Backs up any pre-existing foreign /EFI/BOOT/BOOTX64.EFI to /EFI/BOOT/BOOTX64_PREV.EFI
    ///      before installing ZonderqOS BootGuard + Limine on a shared ESP.
    ///   3. Scans mounted EFI System Partitions and physical disks to build Limine
    ///      chainload entries for Kali Linux, Windows Boot Manager, Ubuntu, Debian,
    ///      Fedora, Arch, systemd-boot, and BIOS partitions.
    ///   4. Persists entries in /boot/dualboot_entries.cfg so BootSlotManager.SyncLimineConfig()
    ///      preserves all Dual-Boot entries across A/B OTA updates and rollbacks.
    /// </summary>
    public static class DualBootManager
    {
        public const string DualBootConfigRelativePath = "/boot/dualboot_entries.cfg";
        public const string BackupFallbackEfiRelativePath = "/EFI/BOOT/BOOTX64_PREV.EFI";
        public const ulong MinZonderqPartitionSectors = 131072UL; // 64 MiB @ 512B
        public static readonly Guid GptEfiSystemPartitionGuid =
            new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

        private static int tempMountCounter;

        private struct KnownEfiBootloader
        {
            public string RelativeEfiPath;
            public string Title;
            public string OsKind;
            public string Comment;

            public KnownEfiBootloader(string path, string title, string osKind, string comment)
            {
                RelativeEfiPath = path;
                Title = title;
                OsKind = osKind;
                Comment = comment;
            }
        }

        private static readonly KnownEfiBootloader[] KnownEfiBootloaders = new KnownEfiBootloader[]
        {
            new KnownEfiBootloader("/EFI/kali/grubx64.efi", "Kali Linux (GRUB UEFI)", "Kali Linux", "Chainload Kali Linux via UEFI GRUB"),
            new KnownEfiBootloader("/EFI/kali/shimx64.efi", "Kali Linux (Shim UEFI)", "Kali Linux", "Chainload Kali Linux via UEFI Shim"),
            new KnownEfiBootloader("/EFI/Microsoft/Boot/bootmgfw.efi", "Windows Boot Manager", "Windows", "Chainload Microsoft Windows Boot Manager"),
            new KnownEfiBootloader("/EFI/ubuntu/grubx64.efi", "Ubuntu Linux (GRUB UEFI)", "Ubuntu", "Chainload Ubuntu Linux via UEFI GRUB"),
            new KnownEfiBootloader("/EFI/debian/grubx64.efi", "Debian GNU/Linux (GRUB UEFI)", "Debian", "Chainload Debian GNU/Linux via UEFI GRUB"),
            new KnownEfiBootloader("/EFI/fedora/grubx64.efi", "Fedora Linux (GRUB UEFI)", "Fedora", "Chainload Fedora Linux via UEFI GRUB"),
            new KnownEfiBootloader("/EFI/arch/grubx64.efi", "Arch Linux (GRUB UEFI)", "Arch Linux", "Chainload Arch Linux via UEFI GRUB"),
            new KnownEfiBootloader("/EFI/grub/grubx64.efi", "Linux (GRUB UEFI)", "Linux", "Chainload Linux GRUB bootloader"),
            new KnownEfiBootloader("/EFI/systemd/systemd-bootx64.efi", "Linux (systemd-boot UEFI)", "Linux", "Chainload systemd-boot UEFI Boot Manager"),
            new KnownEfiBootloader(BackupFallbackEfiRelativePath, "Previous UEFI OS (Fallback Bootloader)", "Previous UEFI OS", "Chainload backed-up pre-existing /EFI/BOOT/BOOTX64_PREV.EFI")
        };

        /// <summary>
        /// Inspects a physical disk entry from DiskCatalog to determine Dual-Boot readiness:
        ///   - Unallocated free space after existing partitions (&gt;= 64 MiB)
        ///   - Free MBR primary slot (0..3) or GPT entry slot
        ///   - Existing EFI System Partition (0xEF on MBR or C12A7328-... on GPT)
        ///   - Foreign OS partitions (Linux EXT2/3/4, Windows NTFS, etc.)
        ///   - Candidate non-ESP partitions that the user can optionally select as ZONDERQDATA
        /// </summary>
        public static DualBootDiskInspection InspectDiskForDualBoot(DiskCatalogEntry disk)
        {
            var result = new DualBootDiskInspection();
            if (disk == null || disk.Device == null || disk.Device.BlockSize == 0 || disk.Device.BlockCount < MinZonderqPartitionSectors)
            {
                result.DiagnosticNote = "Disk is too small or unavailable for Dual-Boot.";
                return result;
            }

            IBlockDevice dev = disk.Device;
            int blockSize = (int)dev.BlockSize;
            ulong totalSectors = dev.BlockCount;
            var detectedOsNames = new List<string>();

            try
            {
                if (Gpt.IsGpt(dev))
                {
                    result.IsGpt = true;
                    List<GptPartitionEntry> gptPartitions = Gpt.Parse(dev);
                    if (gptPartitions == null)
                    {
                        result.DiagnosticNote = "GPT header could not be parsed.";
                        return result;
                    }

                    byte[] hdr = new byte[blockSize];
                    dev.ReadBlock(1UL, 1UL, hdr);
                    ulong firstUsableLba = BitConverter.ToUInt64(hdr, 40);
                    ulong lastUsableLba = BitConverter.ToUInt64(hdr, 48);
                    if (lastUsableLba == 0 || lastUsableLba >= totalSectors)
                        lastUsableLba = totalSectors > 34UL ? totalSectors - 34UL : totalSectors - 1UL;

                    ulong highestEndLba = firstUsableLba > 2048UL ? firstUsableLba : 2048UL;
                    for (int i = 0; i < gptPartitions.Count; i++)
                    {
                        GptPartitionEntry gp = gptPartitions[i];
                        if (gp.SectorCount > 0 && gp.StartSector > 0)
                        {
                            ulong endExclusive = gp.StartSector + gp.SectorCount;
                            if (endExclusive > highestEndLba)
                                highestEndLba = endExclusive;
                        }

                        if (gp.PartitionType == GptEfiSystemPartitionGuid && !result.HasExistingEfiPartition)
                        {
                            result.HasExistingEfiPartition = true;
                            result.ExistingEfiStartLba = gp.StartSector;
                            result.ExistingEfiSectors = gp.SectorCount;
                        }
                    }

                    result.FreeGptSlotIndex = FindFirstEmptyGptSlot(dev);

                    ulong alignedStart = AlignUp2048(highestEndLba);
                    if (lastUsableLba > alignedStart)
                    {
                        ulong freeSectors = lastUsableLba - alignedStart + 1UL;
                        result.FreeSpaceStartLba = alignedStart;
                        result.FreeSpaceSectors = freeSectors;
                        result.FreeSpaceMegabytes = (freeSectors * (ulong)blockSize) / (1024UL * 1024UL);
                        if (freeSectors >= MinZonderqPartitionSectors && result.FreeGptSlotIndex >= 0)
                            result.CanAllocateFromFreeSpace = true;
                    }
                }
                else
                {
                    result.IsGpt = false;
                    byte[] mbr = new byte[blockSize];
                    dev.ReadBlock(0UL, 1UL, mbr);
                    bool validSig = blockSize >= 512 && mbr[510] == 0x55 && mbr[511] == 0xAA;

                    ulong highestEndLba = 2048UL;
                    int firstFreeSlot = -1;
                    if (validSig)
                    {
                        for (int slot = 0; slot < 4; slot++)
                        {
                            int off = 446 + slot * 16;
                            byte partType = mbr[off + 4];
                            uint startLba = BitConverter.ToUInt32(mbr, off + 8);
                            uint countLba = BitConverter.ToUInt32(mbr, off + 12);

                            if (partType == 0 || startLba == 0 || countLba == 0)
                            {
                                if (firstFreeSlot < 0)
                                    firstFreeSlot = slot;
                                continue;
                            }

                            ulong endExclusive = (ulong)startLba + (ulong)countLba;
                            if (endExclusive > highestEndLba)
                                highestEndLba = endExclusive;

                            if (partType == 0xEF && !result.HasExistingEfiPartition)
                            {
                                result.HasExistingEfiPartition = true;
                                result.ExistingEfiStartLba = startLba;
                                result.ExistingEfiSectors = countLba;
                            }
                        }
                    }
                    else
                    {
                        firstFreeSlot = 0;
                    }

                    result.FreeMbrSlotIndex = firstFreeSlot;
                    ulong alignedStart = AlignUp2048(highestEndLba);
                    if (totalSectors > alignedStart)
                    {
                        ulong freeSectors = totalSectors - alignedStart;
                        result.FreeSpaceStartLba = alignedStart;
                        result.FreeSpaceSectors = freeSectors;
                        result.FreeSpaceMegabytes = (freeSectors * (ulong)blockSize) / (1024UL * 1024UL);
                        if (freeSectors >= MinZonderqPartitionSectors && firstFreeSlot >= 0)
                            result.CanAllocateFromFreeSpace = true;
                    }
                }

                // Inspect cataloged partitions on this disk
                if (disk.Partitions != null)
                {
                    for (int i = 0; i < disk.Partitions.Length; i++)
                    {
                        DiskPartitionSummary p = disk.Partitions[i];
                        if (p == null)
                            continue;

                        bool isEsp = (result.HasExistingEfiPartition && p.StartSector == result.ExistingEfiStartLba) ||
                                     p.PartitionType.IndexOf("EFI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                     string.Equals(p.VolumeLabel.Trim(), "ZONDERQEFI", StringComparison.OrdinalIgnoreCase);

                        if (isEsp)
                            continue;

                        string fs = p.Filesystem ?? string.Empty;
                        string lbl = (p.VolumeLabel ?? string.Empty).Trim();

                        if (fs.StartsWith("EXT", StringComparison.OrdinalIgnoreCase) ||
                            p.PartitionType.IndexOf("Linux", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.ForeignPartitionCount++;
                            string name = !string.IsNullOrEmpty(lbl) ? ("Linux (" + lbl + ")") : ("Linux / Kali (" + fs + ")");
                            AddUniqueString(detectedOsNames, name);
                        }
                        else if (string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(fs, "exFAT", StringComparison.OrdinalIgnoreCase) ||
                                 p.PartitionType.IndexOf("NTFS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            result.ForeignPartitionCount++;
                            string name = !string.IsNullOrEmpty(lbl) ? ("Windows (" + lbl + ")") : ("Windows (" + fs + ")");
                            AddUniqueString(detectedOsNames, name);
                        }
                        else if (!string.Equals(lbl, DataVolumePolicy.RequiredLabel, StringComparison.OrdinalIgnoreCase))
                        {
                            result.ForeignPartitionCount++;
                        }

                        if (!p.IsProtectedSystemPartition && string.IsNullOrEmpty(p.MountPoint) && p.SectorCount >= MinZonderqPartitionSectors)
                        {
                            result.CandidateTargetPartitions.Add(new DualBootCandidatePartition
                            {
                                PartitionNumber = i + 1,
                                StartLba = p.StartSector,
                                SectorCount = p.SectorCount,
                                SizeMegabytes = p.SizeMegabytes,
                                FilesystemType = fs,
                                VolumeLabel = lbl,
                                PartitionType = p.PartitionType
                            });
                        }
                    }
                }

                if (detectedOsNames.Count > 0)
                {
                    var sb = new StringBuilder();
                    for (int i = 0; i < detectedOsNames.Count; i++)
                    {
                        if (i > 0) sb.Append(", ");
                        sb.Append(detectedOsNames[i]);
                    }
                    result.DetectedOsSummary = sb.ToString();
                }
                else if (result.HasExistingEfiPartition)
                {
                    result.DetectedOsSummary = "Existing UEFI System Partition (ESP)";
                }

                result.IsValid = result.CanAllocateFromFreeSpace || result.CandidateTargetPartitions.Count > 0;
                if (result.CanAllocateFromFreeSpace)
                {
                    result.DiagnosticNote = "Ready: " + result.FreeSpaceMegabytes + " MB unallocated space available for ZonderqOS.";
                }
                else if (result.CandidateTargetPartitions.Count > 0)
                {
                    result.DiagnosticNote = "No unallocated tail space >= 64 MB; select an existing partition to format as ZONDERQDATA.";
                }
                else
                {
                    result.DiagnosticNote = "No free partition slot or space >= 64 MB available on this disk.";
                }
            }
            catch (Exception ex)
            {
                result.DiagnosticNote = "Dual-Boot inspection failed: " + ex.Message;
            }

            return result;
        }

        /// <summary>
        /// When sharing an existing EFI System Partition (ESP), backs up a foreign
        /// /EFI/BOOT/BOOTX64.EFI to /EFI/BOOT/BOOTX64_PREV.EFI before ZonderqOS installs
        /// its own BootGuard + LIMINE.EFI. Won't overwrite an existing BOOTX64_PREV.EFI.
        /// Uses VfsManager directly so it works both in the live kernel and host smoke tests.
        /// </summary>
        public static bool BackupForeignFallbackBootloaderIfNeeded(
            string efiMountPoint,
            int zonderqBootGuardLen,
            int zonderqLimineLen,
            out string backupStatus)
        {
            backupStatus = string.Empty;
            if (string.IsNullOrEmpty(efiMountPoint))
                return false;

            try
            {
                string prefix = efiMountPoint.TrimEnd('/');
                string bootx64Path = prefix + "/EFI/BOOT/BOOTX64.EFI";
                string prevPath = prefix + BackupFallbackEfiRelativePath;

                if (!VfsExists(bootx64Path))
                    return false;

                if (VfsExists(prevPath))
                {
                    backupStatus = "Existing backup " + BackupFallbackEfiRelativePath + " already present; preserved.";
                    return true;
                }

                byte[] existingBytes = ReadAllBytesVfs(bootx64Path);
                if (existingBytes == null || existingBytes.Length == 0)
                    return false;

                if (existingBytes.Length == zonderqBootGuardLen || existingBytes.Length == zonderqLimineLen)
                {
                    if (ContainsAscii(existingBytes, "ZonderqOS") || ContainsAscii(existingBytes, "LIMINE") || ContainsAscii(existingBytes, "limine"))
                    {
                        backupStatus = "Existing /EFI/BOOT/BOOTX64.EFI is already ZonderqOS/Limine; no backup needed.";
                        return false;
                    }
                }

                if (WriteAllBytesVfs(prevPath, existingBytes))
                {
                    backupStatus = "Backed up foreign /EFI/BOOT/BOOTX64.EFI (" + existingBytes.Length + " bytes) -> " + BackupFallbackEfiRelativePath;
                    return true;
                }
            }
            catch (Exception ex)
            {
                backupStatus = "Could not back up existing BOOTX64.EFI: " + ex.Message;
            }
            return false;
        }

        /// <summary>
        /// Scans a mounted EFI System Partition (e.g. /mnt/zinst_efi) for known UEFI bootloaders
        /// (Kali Linux, Windows Boot Manager, Ubuntu, Debian, Fedora, Arch, systemd-boot, or backed-up BOOTX64_PREV.EFI).
        /// </summary>
        public static void ScanMountedEfiForBootloaders(string efiMountPoint, List<DualBootEntry> entries)
        {
            if (string.IsNullOrEmpty(efiMountPoint) || entries == null)
                return;

            string prefix = efiMountPoint.TrimEnd('/');
            for (int i = 0; i < KnownEfiBootloaders.Length; i++)
            {
                KnownEfiBootloader kb = KnownEfiBootloaders[i];
                string fullPath = prefix + kb.RelativeEfiPath;
                if (VfsExists(fullPath))
                {
                    AddUniqueEntry(entries, new DualBootEntry
                    {
                        Title = kb.Title,
                        Protocol = "efi",
                        PathOrDrive = kb.RelativeEfiPath,
                        Comment = kb.Comment,
                        DetectedOsKind = kb.OsKind
                    });
                }
            }
        }

        /// <summary>
        /// Scans physical disks for foreign OS partitions (e.g., Kali Linux / EXT4, Windows NTFS)
        /// and adds BIOS chainload entries for legacy BIOS / hybrid boot systems.
        /// </summary>
        public static void ScanDisksForForeignPartitions(List<DualBootEntry> entries)
        {
            if (entries == null)
                return;

            try
            {
                List<DiskCatalogEntry> disks = DiskCatalog.DiscoverPhysicalDisks();
                for (int d = 0; d < disks.Count; d++)
                {
                    DiskCatalogEntry disk = disks[d];
                    if (disk == null || disk.IsReadOnly || disk.IsInstallationMedia || disk.Partitions == null)
                        continue;

                    int limineDriveNumber = d + 1;
                    for (int p = 0; p < disk.Partitions.Length; p++)
                    {
                        DiskPartitionSummary part = disk.Partitions[p];
                        if (part == null)
                            continue;

                        string fs = part.Filesystem ?? string.Empty;
                        string lbl = (part.VolumeLabel ?? string.Empty).Trim();
                        int partNumber = p + 1;

                        if (fs.StartsWith("EXT", StringComparison.OrdinalIgnoreCase) ||
                            part.PartitionType.IndexOf("Linux", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string title = !string.IsNullOrEmpty(lbl)
                                ? ("Linux / Kali (" + lbl + " - BIOS Disk " + limineDriveNumber + " Part " + partNumber + ")")
                                : ("Kali / Linux (" + fs + " - BIOS Disk " + limineDriveNumber + " Part " + partNumber + ")");

                            AddUniqueEntry(entries, new DualBootEntry
                            {
                                Title = title,
                                Protocol = "bios",
                                PathOrDrive = limineDriveNumber.ToString(),
                                PartitionIndex = partNumber,
                                Comment = "BIOS chainload Linux partition (" + fs + ")",
                                DetectedOsKind = "Linux"
                            });
                        }
                        else if (string.Equals(fs, "NTFS", StringComparison.OrdinalIgnoreCase) ||
                                 part.PartitionType.IndexOf("NTFS", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string title = !string.IsNullOrEmpty(lbl)
                                ? ("Windows (" + lbl + " - BIOS Disk " + limineDriveNumber + " Part " + partNumber + ")")
                                : ("Microsoft Windows (NTFS - BIOS Disk " + limineDriveNumber + " Part " + partNumber + ")");

                            AddUniqueEntry(entries, new DualBootEntry
                            {
                                Title = title,
                                Protocol = "bios",
                                PathOrDrive = limineDriveNumber.ToString(),
                                PartitionIndex = partNumber,
                                Comment = "BIOS chainload Windows NTFS partition",
                                DetectedOsKind = "Windows"
                            });
                        }
                    }
                }
            }
            catch
            {
            }
        }

        /// <summary>
        /// Performs a full scan of all connected disks (temporarily mounting any unmounted EFI System Partitions
        /// at /mnt/zdb_efi if needed) and returns the discovered Dual-Boot entries.
        /// </summary>
        public static List<DualBootEntry> DiscoverAllDualBootEntries(string alreadyMountedEfiPath = null)
        {
            var entries = new List<DualBootEntry>();
            if (!string.IsNullOrEmpty(alreadyMountedEfiPath))
            {
                ScanMountedEfiForBootloaders(alreadyMountedEfiPath, entries);
            }

            try
            {
                List<DiskCatalogEntry> disks = DiskCatalog.DiscoverPhysicalDisks();
                var allPartitions = StorageManager.Partitions;
                if (allPartitions != null)
                {
                    for (int d = 0; d < disks.Count; d++)
                    {
                        DiskCatalogEntry disk = disks[d];
                        if (disk == null || disk.Device == null || disk.IsReadOnly)
                            continue;

                        for (int p = 0; p < allPartitions.Count; p++)
                        {
                            Partition part = allPartitions[p];
                            if (part == null || !object.ReferenceEquals(part.Host, disk.Device))
                                continue;

                            if (!IsEfiSystemPartition(disk.Device, part))
                                continue;

                            const string tempEfiMount = "/mnt/zdb_efi";
                            bool mountedHere = false;
                            try
                            {
                                EnsureDirVfs("/mnt");
                                EnsureDirVfs(tempEfiMount);
                                try { VfsManager.TryUnmount(tempEfiMount); } catch { }

                                tempMountCounter++;
                                string fsName = "zdbfat_" + tempMountCounter;
                                var fatDriver = new FatFileSystemType(part);
                                if (VfsManager.RegisterFileSystem(fsName, fatDriver) &&
                                    VfsManager.TryMount(fsName, ReadOnlySpan<char>.Empty, MountFlags.None, tempEfiMount, out _))
                                {
                                    mountedHere = true;
                                    ScanMountedEfiForBootloaders(tempEfiMount, entries);
                                }
                            }
                            catch
                            {
                            }
                            finally
                            {
                                if (mountedHere)
                                {
                                    try { VfsManager.TryUnmount(tempEfiMount); } catch { }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            ScanDisksForForeignPartitions(entries);
            return entries;
        }

        public static bool IsEfiSystemPartition(IBlockDevice disk, Partition part)
        {
            if (disk == null || part == null || disk.BlockSize == 0)
                return false;

            try
            {
                if (Gpt.IsGpt(disk))
                {
                    List<GptPartitionEntry> gptParts = Gpt.Parse(disk);
                    if (gptParts != null)
                    {
                        for (int i = 0; i < gptParts.Count; i++)
                        {
                            GptPartitionEntry gp = gptParts[i];
                            if (gp.StartSector == part.StartSector && gp.PartitionType == GptEfiSystemPartitionGuid)
                                return true;
                        }
                    }
                    return false;
                }

                byte[] mbr = new byte[disk.BlockSize];
                disk.ReadBlock(0UL, 1UL, mbr);
                if (disk.BlockSize < 512 || mbr[510] != 0x55 || mbr[511] != 0xAA)
                    return false;

                for (int slot = 0; slot < 4; slot++)
                {
                    int off = 446 + slot * 16;
                    byte type = mbr[off + 4];
                    uint startLba = BitConverter.ToUInt32(mbr, off + 8);
                    if (type == 0xEF && startLba == part.StartSector)
                        return true;
                }
            }
            catch
            {
            }
            return false;
        }

        /// <summary>
        /// Formats a single Dual-Boot entry as a Limine v8/v9/v10 configuration block.
        /// </summary>
        public static string FormatLimineEntry(DualBootEntry entry)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Title) || string.IsNullOrWhiteSpace(entry.PathOrDrive))
                return string.Empty;

            var sb = new StringBuilder();
            string cleanTitle = entry.Title.Trim().TrimStart('/');
            sb.Append('/').Append(cleanTitle).Append('\n');

            if (!string.IsNullOrWhiteSpace(entry.Comment))
                sb.Append("    comment: ").Append(entry.Comment.Trim()).Append('\n');

            if (string.Equals(entry.Protocol, "bios", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(entry.Protocol, "bios_chainload", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append("    protocol: bios\n");
                sb.Append("    drive: ").Append(entry.PathOrDrive.Trim()).Append('\n');
                if (entry.PartitionIndex > 0)
                    sb.Append("    partition: ").Append(entry.PartitionIndex).Append('\n');
            }
            else
            {
                string efiPath = entry.PathOrDrive.Trim();
                if (!efiPath.StartsWith("boot():", StringComparison.OrdinalIgnoreCase) &&
                    !efiPath.StartsWith("hdd(", StringComparison.OrdinalIgnoreCase) &&
                    !efiPath.StartsWith("guid(", StringComparison.OrdinalIgnoreCase))
                {
                    if (!efiPath.StartsWith("/"))
                        efiPath = "/" + efiPath;
                    efiPath = "boot():" + efiPath;
                }

                sb.Append("    protocol: efi\n");
                sb.Append("    path: ").Append(efiPath).Append('\n');
                sb.Append("    image_path: ").Append(efiPath).Append('\n');
            }

            return sb.ToString();
        }

        /// <summary>
        /// Formats a list of Dual-Boot entries for inclusion at the bottom of limine.conf.
        /// </summary>
        public static string FormatLimineDualBootSection(IReadOnlyList<DualBootEntry> entries)
        {
            if (entries == null || entries.Count == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < entries.Count; i++)
            {
                string block = FormatLimineEntry(entries[i]);
                if (!string.IsNullOrEmpty(block))
                {
                    if (sb.Length > 0)
                        sb.Append('\n');
                    sb.Append(block);
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// Saves Dual-Boot entries to &lt;rootPrefix&gt;/boot/dualboot_entries.cfg via VfsManager.
        /// </summary>
        public static bool SaveEntries(string rootPrefix, IReadOnlyList<DualBootEntry> entries, out string error)
        {
            error = string.Empty;
            try
            {
                string prefix = string.IsNullOrEmpty(rootPrefix) || rootPrefix == "/" ? string.Empty : rootPrefix.TrimEnd('/');
                string bootDir = prefix + "/boot";
                EnsureDirVfs(bootDir);

                var sb = new StringBuilder();
                sb.Append("# ZonderqOS Dual-Boot Chainloader Entries\n");
                sb.Append("# Format: PROTOCOL|TITLE|PATH_OR_DRIVE|PARTITION_INDEX|COMMENT\n");
                if (entries != null)
                {
                    for (int i = 0; i < entries.Count; i++)
                    {
                        DualBootEntry e = entries[i];
                        if (e == null || string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.PathOrDrive))
                            continue;

                        sb.Append(SanitizeField(e.Protocol))
                          .Append('|')
                          .Append(SanitizeField(e.Title))
                          .Append('|')
                          .Append(SanitizeField(e.PathOrDrive))
                          .Append('|')
                          .Append(e.PartitionIndex)
                          .Append('|')
                          .Append(SanitizeField(e.Comment))
                          .Append('\n');
                    }
                }

                byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
                if (!WriteAllBytesVfs(prefix + DualBootConfigRelativePath, bytes))
                {
                    error = "Failed to write " + prefix + DualBootConfigRelativePath;
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Loads saved Dual-Boot entries from &lt;rootPrefix&gt;/boot/dualboot_entries.cfg via VfsManager (or File fallback).
        /// </summary>
        public static List<DualBootEntry> LoadSavedEntries(string rootPrefix = "")
        {
            var list = new List<DualBootEntry>();
            try
            {
                string prefix = string.IsNullOrEmpty(rootPrefix) || rootPrefix == "/" ? string.Empty : rootPrefix.TrimEnd('/');
                string path = prefix + DualBootConfigRelativePath;
                byte[] raw = ReadAllBytesVfs(path);
                if ((raw == null || raw.Length == 0) && File.Exists(path))
                {
                    raw = File.ReadAllBytes(path);
                }
                if (raw == null || raw.Length == 0)
                    return list;

                string text = Encoding.UTF8.GetString(raw, 0, raw.Length);
                if (string.IsNullOrWhiteSpace(text))
                    return list;

                string[] lines = text.Split('\n');
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#')
                        continue;

                    string[] parts = line.Split('|');
                    if (parts.Length < 3)
                        continue;

                    int partIdx = 0;
                    if (parts.Length >= 4)
                        int.TryParse(parts[3].Trim(), out partIdx);

                    string comment = parts.Length >= 5 ? parts[4].Trim() : string.Empty;
                    AddUniqueEntry(list, new DualBootEntry
                    {
                        Protocol = parts[0].Trim(),
                        Title = parts[1].Trim(),
                        PathOrDrive = parts[2].Trim(),
                        PartitionIndex = partIdx,
                        Comment = comment
                    });
                }
            }
            catch
            {
            }
            return list;
        }

        public static void AddUniqueEntry(List<DualBootEntry> list, DualBootEntry candidate)
        {
            if (list == null || candidate == null || string.IsNullOrWhiteSpace(candidate.Title) || string.IsNullOrWhiteSpace(candidate.PathOrDrive))
                return;

            for (int i = 0; i < list.Count; i++)
            {
                DualBootEntry existing = list[i];
                if (string.Equals(existing.Protocol, candidate.Protocol, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.PathOrDrive, candidate.PathOrDrive, StringComparison.OrdinalIgnoreCase) &&
                    existing.PartitionIndex == candidate.PartitionIndex)
                {
                    return;
                }
            }
            list.Add(candidate);
        }

        private static bool VfsExists(string path)
        {
            try
            {
                return VfsManager.TryStat(path, out VfsStat stat) && stat.Size > 0;
            }
            catch
            {
                return false;
            }
        }

        private static void EnsureDirVfs(string path)
        {
            try
            {
                if (!VfsManager.TryStat(path, out _))
                    VfsManager.TryCreateDirectory(path, VfsMode.Directory);
            }
            catch
            {
            }
        }

        private static byte[] ReadAllBytesVfs(string path)
        {
            try
            {
                if (!VfsManager.TryStat(path, out VfsStat stat) || stat.Size == 0 || stat.Size > 8 * 1024 * 1024)
                    return null;

                if (!VfsManager.TryOpenFile(path, out IVfsFileHandle handle))
                    return null;

                try
                {
                    byte[] buf = new byte[(int)stat.Size];
                    int total = 0;
                    while (total < buf.Length)
                    {
                        int n = (int)handle.Read(buf.AsSpan(total, buf.Length - total));
                        if (n <= 0)
                            break;
                        total += n;
                    }
                    return total == buf.Length ? buf : null;
                }
                finally
                {
                    (handle as IDisposable)?.Dispose();
                }
            }
            catch
            {
                return null;
            }
        }

        private static bool WriteAllBytesVfs(string path, byte[] data)
        {
            if (data == null)
                return false;

            try
            {
                if (VfsManager.TryStat(path, out _))
                    VfsManager.TryUnlink(path);

                if (!VfsManager.TryCreateFile(path, VfsMode.RegularFile) ||
                    !VfsManager.TryOpenFile(path, out IVfsFileHandle handle))
                    return false;

                try
                {
                    long written = handle.Write(data);
                    handle.TryFlush();
                    return written == data.Length;
                }
                finally
                {
                    (handle as IDisposable)?.Dispose();
                }
            }
            catch
            {
                return false;
            }
        }

        private static int FindFirstEmptyGptSlot(IBlockDevice dev)
        {
            try
            {
                int blockSize = (int)dev.BlockSize;
                byte[] headerBuf = new byte[blockSize];
                dev.ReadBlock(1UL, 1UL, headerBuf);
                ulong entryLba = BitConverter.ToUInt64(headerBuf, 72);
                uint numEntries = BitConverter.ToUInt32(headerBuf, 80);
                uint entrySize = BitConverter.ToUInt32(headerBuf, 84);
                if (entrySize < 128 || entrySize > 512 || numEntries == 0)
                    return -1;

                uint maxCheck = numEntries > 128 ? 128 : numEntries;
                byte[] sectorBuf = new byte[blockSize];
                ulong currentLba = ulong.MaxValue;

                for (uint i = 0; i < maxCheck; i++)
                {
                    ulong bytePos = (ulong)i * entrySize;
                    ulong sectorOffset = bytePos / (ulong)blockSize;
                    int inSectorOffset = (int)(bytePos % (ulong)blockSize);
                    if (inSectorOffset + 16 > blockSize)
                        continue;

                    ulong targetLba = entryLba + sectorOffset;
                    if (targetLba != currentLba)
                    {
                        dev.ReadBlock(targetLba, 1UL, sectorBuf);
                        currentLba = targetLba;
                    }

                    bool allZero = true;
                    for (int b = 0; b < 16; b++)
                    {
                        if (sectorBuf[inSectorOffset + b] != 0)
                        {
                            allZero = false;
                            break;
                        }
                    }
                    if (allZero)
                        return (int)i;
                }
            }
            catch
            {
            }
            return -1;
        }

        private static ulong AlignUp2048(ulong lba)
        {
            if (lba < 2048UL)
                return 2048UL;
            ulong rem = lba % 2048UL;
            return rem == 0 ? lba : (lba + (2048UL - rem));
        }

        private static string SanitizeField(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace('|', '/').Replace('\r', ' ').Replace('\n', ' ').Trim();
        }

        private static void AddUniqueString(List<string> list, string value)
        {
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i], value, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            list.Add(value);
        }

        private static bool ContainsAscii(byte[] buffer, string needle)
        {
            if (buffer == null || string.IsNullOrEmpty(needle) || buffer.Length < needle.Length)
                return false;

            int limit = buffer.Length - needle.Length;
            for (int i = 0; i <= limit; i++)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (buffer[i + j] != (byte)needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
            return false;
        }
    }
}
