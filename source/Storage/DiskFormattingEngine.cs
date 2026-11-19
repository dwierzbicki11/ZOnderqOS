using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.Storage
{
    public enum InstallationPartitionLayout
    {
        DualEfiAndSystem = 0,
        SingleSystemPartition = 1
    }

    public sealed class PreparedInstallPartitions
    {
        public IBlockDevice Device { get; set; }
        public InstallationPartitionLayout Layout { get; set; }
        public Partition EfiPartition { get; set; }
        public Partition SystemPartition { get; set; }
    }

    /// <summary>
    /// Performs hardware-verified disk erasing, MBR partitioning, FAT32 formatting,
    /// filesystem post-format verification, and Limine BIOS stage installation.
    /// All operations require an explicit DiskIdentityToken and confirmation string.
    /// </summary>
    public static class DiskFormattingEngine
    {
        public const string EfiVolumeLabel = "ZONDERQEFI ";
        public const ulong FirstPartitionStartLba = 2048UL; // 1 MiB alignment (leaves LBA 1..2047 for Limine BIOS stage 2)
        public const ulong EfiPartitionSectors512 = 131072UL; // 64 MiB (valid FAT32 >= 65536 clusters)
        public const ulong DualLayoutMinDiskMegabytes = 160UL;
        private const string LimineHddResourceName = "Bootloader.limine-hdd.bin";

        public static bool PrepareDiskForInstallation(
            DiskIdentityToken token,
            string typedConfirmation,
            InstallationPartitionLayout requestedLayout,
            bool zeroFillHeaderRegion,
            Action<int, string> progressCallback,
            out PreparedInstallPartitions prepared,
            out string error)
        {
            prepared = null;
            error = null;

            // 1. Verify explicit typed confirmation (e.g. "DISK-1")
            if (token == null || !InstallerInputValidator.ValidateDiskConfirmation(token.DiskId, typedConfirmation))
            {
                error = "Disk confirmation ID does not match the selected target disk.";
                return false;
            }

            // 2. Re-verify physical disk identity immediately before writing
            if (!DiskCatalog.VerifyIdentityBeforeWrite(token, out error))
                return false;

            IBlockDevice device = token.DeviceReference;
            if (device.BlockSize != 512UL)
            {
                error = "Only 512-byte logical sector disks are currently supported for automated OS installation (found " +
                        device.BlockSize + " B).";
                return false;
            }

            ulong totalSectors = device.BlockCount;
            ulong totalMegabytes = (totalSectors * device.BlockSize) / (1024UL * 1024UL);
            if (totalMegabytes < DiskCatalog.MinInstallableMegabytes)
            {
                error = "Target disk is smaller than the minimum required 68 MB.";
                return false;
            }

            // Use dual-partition (64 MiB ESP + System) if disk >= 160 MiB and DualEfiAndSystem was chosen;
            // automatically use single-partition layout on compact disks (68..159 MiB) or when explicitly selected.
            InstallationPartitionLayout effectiveLayout = requestedLayout;
            if (effectiveLayout == InstallationPartitionLayout.DualEfiAndSystem &&
                totalMegabytes < DualLayoutMinDiskMegabytes)
            {
                effectiveLayout = InstallationPartitionLayout.SingleSystemPartition;
            }

            try
            {
                // Stage A: Clear old MBR, primary GPT (LBA 0..64) and backup GPT (last 34 sectors)
                progressCallback?.Invoke(8, "Preparing disk");
                if (!WipePartitionStructures(device, zeroFillHeaderRegion, out error))
                    return false;

                // Stage B: Write new MBR partition table
                progressCallback?.Invoke(18, "Creating partitions");
                ulong efiStart = FirstPartitionStartLba;
                ulong efiCount = 0;
                ulong sysStart = FirstPartitionStartLba;
                ulong sysCount = 0;

                if (effectiveLayout == InstallationPartitionLayout.DualEfiAndSystem)
                {
                    efiStart = FirstPartitionStartLba;
                    efiCount = EfiPartitionSectors512;
                    sysStart = efiStart + efiCount;
                    sysCount = totalSectors - sysStart;

                    if (sysCount < 131072UL)
                    {
                        error = "Target disk has insufficient space for both EFI and System FAT32 partitions.";
                        return false;
                    }

                    if (!WriteMbrPartitionTable(device, true, efiStart, efiCount, sysStart, sysCount, out error))
                        return false;
                }
                else
                {
                    sysStart = FirstPartitionStartLba;
                    sysCount = totalSectors - sysStart;

                    if (sysCount < 131072UL)
                    {
                        error = "Target partition requires at least 64 MB (131072 sectors) for FAT32.";
                        return false;
                    }

                    if (!WriteMbrPartitionTable(device, false, 0, 0, sysStart, sysCount, out error))
                        return false;
                }

                device.Flush();
                StorageManager.RescanPartitions(device);

                // Locate the newly created Partition objects bound to this exact IBlockDevice
                Partition efiPartition = null;
                Partition sysPartition = null;
                var allPartitions = StorageManager.Partitions;
                if (allPartitions != null)
                {
                    for (int i = 0; i < allPartitions.Count; i++)
                    {
                        Partition p = allPartitions[i];
                        if (p == null || !object.ReferenceEquals(p.Host, device))
                            continue;

                        if (effectiveLayout == InstallationPartitionLayout.DualEfiAndSystem &&
                            p.StartSector == efiStart && p.BlockCount == efiCount)
                        {
                            efiPartition = p;
                        }
                        else if (p.StartSector == sysStart && p.BlockCount == sysCount)
                        {
                            sysPartition = p;
                        }
                    }
                }

                if (sysPartition == null)
                {
                    error = "Failed to discover newly created ZonderqOS system partition after rescan.";
                    return false;
                }

                if (effectiveLayout == InstallationPartitionLayout.DualEfiAndSystem && efiPartition == null)
                {
                    error = "Failed to discover newly created EFI System Partition after rescan.";
                    return false;
                }

                // Stage C: Format partitions as FAT32 and verify on-disk BPB + labels
                progressCallback?.Invoke(28, "Formatting filesystem");

                if (efiPartition != null)
                {
                    if (!FormatAndVerifyFat32(efiPartition, EfiVolumeLabel, false, out error))
                        return false;
                }

                progressCallback?.Invoke(35, "Formatting filesystem");
                if (!FormatAndVerifyFat32(sysPartition, DataVolumePolicy.RequiredLabel, true, out error))
                    return false;

                prepared = new PreparedInstallPartitions
                {
                    Device = device,
                    Layout = effectiveLayout,
                    EfiPartition = efiPartition,
                    SystemPartition = sysPartition
                };
                return true;
            }
            catch (Exception ex)
            {
                error = "Disk preparation exception: " + ex.Message;
                return false;
            }
        }

        public static bool FormatAndVerifyFat32(
            Partition partition,
            string label11,
            bool verifyDataVolumePolicy,
            out string error)
        {
            error = null;
            if (partition == null || partition.Host == null)
            {
                error = "Target partition is null.";
                return false;
            }

            if (RamRootVolume.IsRamPartition(partition) ||
                DiskCatalog.IsDeviceHostingActiveRoot(partition.Host) ||
                LimineBootMediaDetector.IsBootOrInstallationMedia(partition.Host))
            {
                error = "Refusing to format a protected system or installation partition.";
                return false;
            }

            string normalizedLabel = NormalizeFatLabel(label11);

            try
            {
                // Wipe first 64 sectors of partition before formatting so no stale filesystem signatures remain
                int blockBytes = (int)partition.BlockSize;
                byte[] zeroBlock = new byte[blockBytes];
                ulong clearCount = partition.BlockCount < 64UL ? partition.BlockCount : 64UL;
                for (ulong s = 0; s < clearCount; s++)
                    partition.WriteBlock(s, 1UL, zeroBlock);

                var formatter = new FatFileSystemType(partition);
                var options = new FatFormatOptions
                {
                    Type = FatType.Fat32,
                    VolumeLabel = normalizedLabel
                    // Leave SectorsPerCluster = 0 so FatFormatter.PickSectorsPerCluster chooses optimal cluster size
                };

                if (!formatter.TryFormat(ReadOnlySpan<char>.Empty, options))
                {
                    error = "FAT32 formatting failed on partition " + partition.Name + ".";
                    return false;
                }

                // Write standard FAT32 Root Directory volume label entry (ATTR_VOLUME_ID = 0x08)
                // so Limine fslabel(), UEFI EFI_FILE_SYSTEM_INFO, and host OS tools recognize the label.
                if (!WriteFat32RootVolumeLabelEntry(partition, normalizedLabel, out error))
                    return false;

                partition.Flush();
                partition.Host.Flush();

                // Post-format verification: parse FAT32 boot sector and verify volume label
                byte[] sector0 = new byte[blockBytes];
                partition.ReadBlock(0UL, 1UL, sector0);
                if (!FatBootSector.TryParse(sector0, out FatBootSector boot) ||
                    boot.Type != FatType.Fat32 ||
                    boot.BytesPerSector != partition.BlockSize)
                {
                    error = "Post-format verification failed: invalid FAT32 boot sector on " + partition.Name + ".";
                    return false;
                }

                for (int i = 0; i < 11; i++)
                {
                    if (sector0[71 + i] != (byte)normalizedLabel[i])
                    {
                        error = "Post-format verification failed: volume label mismatch on " + partition.Name + ".";
                        return false;
                    }
                }

                if ( verifyDataVolumePolicy && !DataVolumePolicy.IsApprovedDataPartition(partition))
                {
                    error = "Post-format verification failed: DataVolumePolicy did not approve the formatted ZONDERQDATA partition.";
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "FAT32 format exception: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Writes a standard 32-byte FAT32 Root Directory ATTR_VOLUME_ID (0x08) entry
        /// into Cluster 2 immediately after TryFormat so Limine fslabel() and UEFI
        /// SimpleFileSystem VolumeLabel resolve the volume label reliably.
        /// </summary>
        private static bool WriteFat32RootVolumeLabelEntry(Partition partition, string label11, out string error)
        {
            error = null;
            try
            {
                int blockBytes = (int)partition.BlockSize;
                byte[] sector0 = new byte[blockBytes];
                partition.ReadBlock(0UL, 1UL, sector0);
                if (!FatBootSector.TryParse(sector0, out FatBootSector boot) ||
                    boot.Type != FatType.Fat32 ||
                    boot.DataStartLba == 0 ||
                    boot.DataStartLba >= partition.BlockCount)
                {
                    error = "Cannot write FAT32 root volume label: invalid boot sector.";
                    return false;
                }

                ulong rootClusterLba = boot.DataStartLba + (ulong)(boot.RootCluster >= 2 ? boot.RootCluster - 2 : 0) * boot.SectorsPerCluster;
                if (rootClusterLba >= partition.BlockCount)
                {
                    error = "FAT32 root cluster LBA is out of range.";
                    return false;
                }

                byte[] rootSector = new byte[blockBytes];
                partition.ReadBlock(rootClusterLba, 1UL, rootSector);
                Array.Clear(rootSector, 0, 32);
                for (int i = 0; i < 11; i++)
                    rootSector[i] = (byte)label11[i];
                rootSector[11] = 0x08; // ATTR_VOLUME_ID
                partition.WriteBlock(rootClusterLba, 1UL, rootSector);
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to write FAT32 root volume label entry: " + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Mirrors FAT #1 into FAT #2 after VFS unmount so both FAT copies on disk
        /// are 100% identical for UEFI firmware, Limine, fsck.vfat, and host tools.
        /// </summary>
        public static void MirrorPrimaryFatToSecondaryFat(Partition partition)
        {
            if (partition == null || partition.BlockSize < 512 || partition.BlockSize > 4096)
                return;

            try
            {
                int blockBytes = (int)partition.BlockSize;
                byte[] sector0 = new byte[blockBytes];
                partition.ReadBlock(0UL, 1UL, sector0);
                if (!FatBootSector.TryParse(sector0, out FatBootSector boot) ||
                    boot.NumberOfFats < 2 ||
                    boot.FatSectorCount == 0)
                {
                    return;
                }

                ulong fat1Start = boot.ReservedSectorCount;
                ulong fat2Start = fat1Start + boot.FatSectorCount;
                ulong fatSectors = boot.FatSectorCount;
                if (fat2Start + fatSectors > partition.BlockCount)
                    return;

                const ulong chunkSectors = 64UL;
                byte[] chunkBuf = new byte[(int)chunkSectors * blockBytes];
                ulong copied = 0;
                while (copied < fatSectors)
                {
                    ulong n = fatSectors - copied;
                    if (n > chunkSectors)
                        n = chunkSectors;

                    int byteLen = (int)n * blockBytes;
                    Span<byte> slice = chunkBuf.AsSpan(0, byteLen);
                    partition.ReadBlock(fat1Start + copied, n, slice);
                    partition.WriteBlock(fat2Start + copied, n, slice);
                    copied += n;
                }

                partition.Flush();
            }
            catch
            {
            }
        }

        /// <summary>
        /// Writes the 4-byte completion marker 'ZQOK' into the MBR timestamp field (offset 218..221)
        /// ONLY after all filesystems have been verified, unmounted, and flushed to disk.
        /// </summary>
        public static void WriteMbrInstallationCompleteMarker(IBlockDevice device)
        {
            if (device == null || device.BlockSize != 512UL || device.BlockCount == 0)
                return;

            try
            {
                byte[] mbr = new byte[512];
                device.ReadBlock(0UL, 1UL, mbr);
                if (mbr[510] == 0x55 && mbr[511] == 0xAA)
                {
                    mbr[218] = (byte)'Z';
                    mbr[219] = (byte)'Q';
                    mbr[220] = (byte)'O';
                    mbr[221] = (byte)'K';
                    device.WriteBlock(0UL, 1UL, mbr);
                    device.Flush();
                }
            }
            catch
            {
            }
        }

        public static bool InstallLimineBiosBootloader(IBlockDevice device, out string error)
        {
            error = null;
            if (device == null || device.BlockSize != 512UL || device.BlockCount < FirstPartitionStartLba)
            {
                error = "Invalid target device for Limine BIOS stage installation.";
                return false;
            }

            if (RamRootVolume.IsRamDevice(device) ||
                DiskCatalog.IsDeviceHostingActiveRoot(device) ||
                LimineBootMediaDetector.IsBootOrInstallationMedia(device))
            {
                error = "Refusing to overwrite boot sectors on a protected device.";
                return false;
            }

            try
            {
                byte[] limineHdd = LoadEmbeddedResourceBytes(LimineHddResourceName);
                if (limineHdd == null || limineHdd.Length < 2048 || limineHdd.Length > 64 * 1024)
                {
                    error = "Embedded Limine BIOS HDD stage binary is missing or invalid.";
                    return false;
                }

                // Read existing MBR Sector 0 to preserve timestamp (218..223) and partition table (440..509)
                byte[] mbr = new byte[512];
                device.ReadBlock(0UL, 1UL, mbr);
                if (mbr[510] != 0x55 || mbr[511] != 0xAA)
                {
                    error = "Target disk does not have a valid MBR signature (0x55AA) prior to bootloader installation.";
                    return false;
                }

                byte[] savedTimestamp = new byte[6];
                byte[] savedPartitionTable = new byte[70];
                Array.Copy(mbr, 218, savedTimestamp, 0, 6);
                Array.Copy(mbr, 440, savedPartitionTable, 0, 70);

                // Copy stage 1 bootcode from limineHdd[0..511]
                Array.Copy(limineHdd, 0, mbr, 0, 512);

                // Hardcode stage 2 byte offset (512 = LBA 1) at offset 0x1A4 (420)
                ulong stage2ByteOffset = 512UL;
                for (int i = 0; i < 8; i++)
                    mbr[0x1A4 + i] = (byte)(stage2ByteOffset >> (i * 8));

                // Restore timestamp and partition table
                Array.Copy(savedTimestamp, 0, mbr, 218, 6);
                Array.Copy(savedPartitionTable, 0, mbr, 440, 70);
                mbr[510] = 0x55;
                mbr[511] = 0xAA;

                // Ensure at least one partition is marked active (0x80) for legacy BIOS compatibility
                if ((mbr[446] & 0x80) == 0 && (mbr[462] & 0x80) == 0)
                    mbr[446] = 0x80;

                device.WriteBlock(0UL, 1UL, mbr);

                // Write stage 2 starting at LBA 1 (byte offset 512)
                int stage2Length = limineHdd.Length - 512;
                int stage2Sectors = (stage2Length + 511) / 512;
                byte[] sectorBuf = new byte[512];
                for (int s = 0; s < stage2Sectors; s++)
                {
                    Array.Clear(sectorBuf, 0, 512);
                    int srcOffset = 512 + s * 512;
                    int copyLen = Math.Min(512, limineHdd.Length - srcOffset);
                    if (copyLen > 0)
                        Array.Copy(limineHdd, srcOffset, sectorBuf, 0, copyLen);

                    device.WriteBlock((ulong)(1 + s), 1UL, sectorBuf);
                }

                device.Flush();

                // Verify written MBR and stage 2 header
                byte[] verifyMbr = new byte[512];
                device.ReadBlock(0UL, 1UL, verifyMbr);
                if (verifyMbr[510] != 0x55 || verifyMbr[511] != 0xAA ||
                    verifyMbr[3] != (byte)'L' || verifyMbr[4] != (byte)'I' || verifyMbr[5] != (byte)'M')
                {
                    error = "Post-write verification of Limine MBR stage 1 failed.";
                    return false;
                }

                for (int i = 0; i < 70; i++)
                {
                    if (verifyMbr[440 + i] != savedPartitionTable[i] && !(i == 6 && verifyMbr[446] == 0x80))
                    {
                        error = "Post-write verification failed: MBR partition table was altered during bootloader install.";
                        return false;
                    }
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "Limine BIOS stage installation error: " + ex.Message;
                return false;
            }
        }

        public static bool InitializeDiskWithSingleFat32Partition(
            DiskIdentityToken token,
            string typedConfirmation,
            string volumeLabel,
            out string error)
        {
            error = null;
            if (token == null || !InstallerInputValidator.ValidateDiskConfirmation(token.DiskId, typedConfirmation))
            {
                error = "Confirmation ID does not match the selected disk.";
                return false;
            }

            if (!DiskCatalog.VerifyIdentityBeforeWrite(token, out error))
                return false;

            string cleanLabel = (volumeLabel ?? "ZONDERQVOL").Trim().ToUpperInvariant();
            if (string.Equals(cleanLabel, DataVolumePolicy.RequiredLabel, StringComparison.OrdinalIgnoreCase))
            {
                error = "Label ZONDERQDATA is reserved for the OS installation wizard.";
                return false;
            }

            IBlockDevice device = token.DeviceReference;
            if (device.BlockSize != 512UL || device.BlockCount < FirstPartitionStartLba + 131072UL)
            {
                error = "Disk must have 512-byte sectors and at least 65 MB capacity.";
                return false;
            }

            if (!WipePartitionStructures(device, false, out error))
                return false;

            ulong startLba = FirstPartitionStartLba;
            ulong countLba = device.BlockCount - startLba;
            if (!WriteMbrPartitionTable(device, false, 0, 0, startLba, countLba, out error))
                return false;

            device.Flush();
            StorageManager.RescanPartitions(device);

            Partition created = null;
            var parts = StorageManager.Partitions;
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    Partition p = parts[i];
                    if (p != null && object.ReferenceEquals(p.Host, device) && p.StartSector == startLba)
                    {
                        created = p;
                        break;
                    }
                }
            }

            if (created == null)
            {
                error = "Failed to locate newly created partition after rescan.";
                return false;
            }

            return FormatAndVerifyFat32(created, cleanLabel, false, out error);
        }

        public static bool DeletePartitionFromDisk(
            DiskIdentityToken diskToken,
            int mbrPartitionSlotIndex,
            string expectedConfirmCode,
            string typedConfirmation,
            out string error)
        {
            error = null;
            if (!InstallerInputValidator.ValidateDiskConfirmation(expectedConfirmCode, typedConfirmation))
            {
                error = "Confirmation code does not match.";
                return false;
            }

            if (!DiskCatalog.VerifyIdentityBeforeWrite(diskToken, out error))
                return false;

            IBlockDevice device = diskToken.DeviceReference;
            if (!Mbr.IsMbr(device))
            {
                error = "Only MBR disks are supported for direct partition deletion.";
                return false;
            }

            try
            {
                Mbr.RemovePartition(device, mbrPartitionSlotIndex);
                device.Flush();
                StorageManager.RescanPartitions(device);
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to delete partition: " + ex.Message;
                return false;
            }
        }

        private static bool WipePartitionStructures(IBlockDevice device, bool zeroFillExtendedHeader, out string error)
        {
            error = null;
            try
            {
                int blockBytes = (int)device.BlockSize;
                byte[] zero = new byte[blockBytes];

                // Wipe primary MBR + primary GPT header & entry array (LBA 0..64)
                ulong headSectors = zeroFillExtendedHeader ? 4096UL : 64UL;
                if (headSectors > device.BlockCount)
                    headSectors = device.BlockCount;

                for (ulong lba = 0; lba < headSectors; lba++)
                    device.WriteBlock(lba, 1UL, zero);

                // Wipe backup GPT at the end of the disk (last 34 sectors)
                if (device.BlockCount > 68UL)
                {
                    ulong tailStart = device.BlockCount - 34UL;
                    for (ulong lba = tailStart; lba < device.BlockCount; lba++)
                        device.WriteBlock(lba, 1UL, zero);
                }

                // Also wipe old partition boot sectors at LBA 2048 and LBA 133120 if within disk bounds
                if (device.BlockCount > FirstPartitionStartLba + 8UL)
                {
                    for (ulong i = 0; i < 8UL; i++)
                        device.WriteBlock(FirstPartitionStartLba + i, 1UL, zero);
                }

                if (device.BlockCount > FirstPartitionStartLba + EfiPartitionSectors512 + 8UL)
                {
                    ulong secondStart = FirstPartitionStartLba + EfiPartitionSectors512;
                    for (ulong i = 0; i < 8UL; i++)
                        device.WriteBlock(secondStart + i, 1UL, zero);
                }

                device.Flush();
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to clear existing partition structures: " + ex.Message;
                return false;
            }
        }

        private static bool WriteMbrPartitionTable(
            IBlockDevice device,
            bool dualPartition,
            ulong part1Start,
            ulong part1Count,
            ulong part2Start,
            ulong part2Count,
            out string error)
        {
            error = null;
            try
            {
                Mbr.Create(device);
                if (dualPartition)
                {
                    if (!Mbr.AddPartition(device, 0, 0xEF, part1Start, part1Count))
                    {
                        error = "Mbr.AddPartition failed for EFI System Partition (slot 0).";
                        return false;
                    }
                    if (!Mbr.AddPartition(device, 1, 0x0C, part2Start, part2Count))
                    {
                        error = "Mbr.AddPartition failed for ZonderqOS System Partition (slot 1).";
                        return false;
                    }
                }
                else
                {
                    if (!Mbr.AddPartition(device, 0, 0x0C, part2Start, part2Count))
                    {
                        error = "Mbr.AddPartition failed for ZonderqOS System Partition (slot 0).";
                        return false;
                    }
                }

                // Set deterministic non-zero MBR disk signature at 0x1B8 and active boot flag + LBA CHS tuples
                byte[] mbr = new byte[512];
                device.ReadBlock(0UL, 1UL, mbr);

                uint diskSig = (uint)(0x5A510000U ^ (uint)(device.BlockCount & 0xFFFFU));
                if (diskSig == LimineBootMediaDetector.BootMbrDiskId)
                    diskSig ^= 0x0000A55AU;

                mbr[440] = (byte)(diskSig & 0xFF);
                mbr[441] = (byte)((diskSig >> 8) & 0xFF);
                mbr[442] = (byte)((diskSig >> 16) & 0xFF);
                mbr[443] = (byte)((diskSig >> 24) & 0xFF);

                // Populate standard LBA CHS placeholders (0xFE, 0xFF, 0xFF) and active flag 0x80
                if (dualPartition)
                {
                    mbr[446] = 0x00;
                    SetChsTuple(mbr, 446 + 1, part1Start);
                    SetChsTuple(mbr, 446 + 5, part1Start + part1Count - 1UL);

                    mbr[462] = 0x80; // Active bootable system partition
                    SetChsTuple(mbr, 462 + 1, part2Start);
                    SetChsTuple(mbr, 462 + 5, part2Start + part2Count - 1UL);
                }
                else
                {
                    mbr[446] = 0x80; // Active bootable system partition
                    SetChsTuple(mbr, 446 + 1, part2Start);
                    SetChsTuple(mbr, 446 + 5, part2Start + part2Count - 1UL);
                }

                mbr[510] = 0x55;
                mbr[511] = 0xAA;
                device.WriteBlock(0UL, 1UL, mbr);
                device.Flush();
                return true;
            }
            catch (Exception ex)
            {
                error = "Failed to write MBR partition table: " + ex.Message;
                return false;
            }
        }

        private static void SetChsTuple(byte[] mbr, int offset, ulong lba)
        {
            if (lba > 63UL * 255UL * 1024UL)
            {
                mbr[offset] = 0xFE;
                mbr[offset + 1] = 0xFF;
                mbr[offset + 2] = 0xFF;
                return;
            }

            ulong cylinder = lba / (255UL * 63UL);
            if (cylinder >= 1024UL)
            {
                mbr[offset] = 0xFE;
                mbr[offset + 1] = 0xFF;
                mbr[offset + 2] = 0xFF;
                return;
            }

            ulong head = (lba / 63UL) % 255UL;
            ulong sector = (lba % 63UL) + 1UL;
            mbr[offset] = (byte)head;
            mbr[offset + 1] = (byte)(((cylinder >> 2) & 0xC0UL) | (sector & 0x3FUL));
            mbr[offset + 2] = (byte)(cylinder & 0xFFUL);
        }

        public static string NormalizeFatLabel(string rawLabel)
        {
            if (string.IsNullOrWhiteSpace(rawLabel))
                return "NO NAME    ";

            char[] chars = new char[11];
            for (int i = 0; i < 11; i++)
                chars[i] = ' ';

            string upper = rawLabel.Trim().ToUpperInvariant();
            int limit = Math.Min(11, upper.Length);
            for (int i = 0; i < limit; i++)
            {
                char c = upper[i];
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == '-')
                    chars[i] = c;
                else
                    chars[i] = '_';
            }

            return new string(chars);
        }

        public static byte[] LoadEmbeddedResourceBytes(string logicalName)
        {
            try
            {
                Assembly asm = Assembly.GetExecutingAssembly();
                using (Stream stream = asm.GetManifestResourceStream(logicalName))
                {
                    if (stream == null)
                        return null;

                    int len = (int)stream.Length;
                    byte[] data = new byte[len];
                    int readTotal = 0;
                    while (readTotal < len)
                    {
                        int n = stream.Read(data, readTotal, len - readTotal);
                        if (n <= 0)
                            break;
                        readTotal += n;
                    }
                    return readTotal == len ? data : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
