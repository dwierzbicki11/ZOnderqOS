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
        SingleSystemPartition = 1,
        DualBootAlongsideExistingOs = 2
    }

    public sealed class PreparedInstallPartitions
    {
        public IBlockDevice Device { get; set; }
        public InstallationPartitionLayout Layout { get; set; }
        public Partition EfiPartition { get; set; }
        public Partition SystemPartition { get; set; }
        public bool PreserveExistingEfiPartition { get; set; }
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

        private static bool EnsureRootPrivilege(out string error)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                error = "Root privileges are required for destructive disk operations.";
                return false;
            }
            error = null;
            return true;
        }

        public static bool PrepareDiskForInstallation(
            DiskIdentityToken token,
            string typedConfirmation,
            InstallationPartitionLayout requestedLayout,
            bool zeroFillHeaderRegion,
            Action<int, string> progressCallback,
            out PreparedInstallPartitions prepared,
            out string error)
        {
            if (requestedLayout == InstallationPartitionLayout.DualBootAlongsideExistingOs)
            {
                return PrepareDiskForDualBoot(
                    token,
                    typedConfirmation,
                    0UL,
                    progressCallback,
                    out prepared,
                    out error);
            }

            prepared = null;
            error = null;

            if (!EnsureRootPrivilege(out error))
                return false;

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

        /// <summary>
        /// Non-destructive Dual-Boot disk preparation:
        ///   - NEVER calls WipePartitionStructures. All foreign partitions (Kali Linux, Windows, ESP) are preserved.
        ///   - If targetPartitionStartLba == 0: allocates a new partition in unallocated free space (&gt;= 64 MiB)
        ///     using the first free MBR slot (0x0C FAT32 LBA) or first free GPT slot (BasicDataPartitionType).
        ///   - If targetPartitionStartLba &gt; 0: reuses that specific existing non-ESP partition and ensures its
        ///     partition type is 0x0C (MBR) or BasicDataPartitionType (GPT).
        ///   - Formats ONLY the target ZONDERQDATA partition as FAT32 and leaves any existing ESP untouched
        ///     (PreserveExistingEfiPartition = true).
        /// </summary>
        public static bool PrepareDiskForDualBoot(
            DiskIdentityToken token,
            string typedConfirmation,
            ulong targetPartitionStartLba,
            Action<int, string> progressCallback,
            out PreparedInstallPartitions prepared,
            out string error)
        {
            prepared = null;
            error = null;

            if (!EnsureRootPrivilege(out error))
                return false;

            if (token == null || !InstallerInputValidator.ValidateDiskConfirmation(token.DiskId, typedConfirmation))
            {
                error = "Disk confirmation ID does not match the selected target disk.";
                return false;
            }

            if (!DiskCatalog.VerifyIdentityBeforeWrite(token, out error))
                return false;

            IBlockDevice device = token.DeviceReference;
            if (device.BlockSize != 512UL)
            {
                error = "Only 512-byte logical sector disks are currently supported for automated OS installation (found " +
                        device.BlockSize + " B).";
                return false;
            }

            if (device.BlockCount < FirstPartitionStartLba + EfiPartitionSectors512)
            {
                error = "Target disk is smaller than the minimum required 68 MB.";
                return false;
            }

            try
            {
                progressCallback?.Invoke(8, "Preparing disk");
                ulong sysStart = 0;
                ulong sysCount = 0;
                ulong existingEspStart = 0;

                if (Gpt.IsGpt(device))
                {
                    if (!PrepareGptForDualBoot(device, targetPartitionStartLba, out sysStart, out sysCount, out existingEspStart, out error))
                        return false;
                }
                else
                {
                    if (!PrepareMbrForDualBoot(device, targetPartitionStartLba, out sysStart, out sysCount, out existingEspStart, out error))
                        return false;
                }

                progressCallback?.Invoke(18, "Creating partitions");
                device.Flush();
                StorageManager.RescanPartitions(device);

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

                        if (p.StartSector == sysStart)
                        {
                            sysPartition = p;
                        }
                        else if (existingEspStart != 0 && p.StartSector == existingEspStart)
                        {
                            efiPartition = p;
                        }
                    }
                }

                if (sysPartition == null)
                {
                    error = "Failed to discover Dual-Boot ZonderqOS system partition at LBA " + sysStart + " after rescan.";
                    return false;
                }

                progressCallback?.Invoke(32, "Formatting filesystem");
                if (!FormatAndVerifyFat32(sysPartition, DataVolumePolicy.RequiredLabel, true, out error))
                    return false;

                prepared = new PreparedInstallPartitions
                {
                    Device = device,
                    Layout = InstallationPartitionLayout.DualBootAlongsideExistingOs,
                    EfiPartition = efiPartition,
                    SystemPartition = sysPartition,
                    PreserveExistingEfiPartition = efiPartition != null
                };
                return true;
            }
            catch (Exception ex)
            {
                error = "Dual-Boot disk preparation exception: " + ex.Message;
                return false;
            }
        }

        private static bool PrepareMbrForDualBoot(
            IBlockDevice device,
            ulong targetPartitionStartLba,
            out ulong sysStartLba,
            out ulong sysSectorCount,
            out ulong existingEspStartLba,
            out string error)
        {
            sysStartLba = 0;
            sysSectorCount = 0;
            existingEspStartLba = 0;
            error = null;

            byte[] mbr = new byte[512];
            device.ReadBlock(0UL, 1UL, mbr);
            if (mbr[510] != 0x55 || mbr[511] != 0xAA)
            {
                error = "Target disk does not have a valid MBR signature (0x55AA) for Dual-Boot.";
                return false;
            }

            int firstFreeSlot = -1;
            int targetSlot = -1;
            ulong highestEndLba = FirstPartitionStartLba;
            bool anyActive = false;

            for (int slot = 0; slot < 4; slot++)
            {
                int off = 446 + slot * 16;
                byte bootFlag = mbr[off + 0];
                byte partType = mbr[off + 4];
                uint start = BitConverter.ToUInt32(mbr, off + 8);
                uint count = BitConverter.ToUInt32(mbr, off + 12);

                if (partType == 0 || start == 0 || count == 0)
                {
                    if (firstFreeSlot < 0)
                        firstFreeSlot = slot;
                    continue;
                }

                if ((bootFlag & 0x80) != 0)
                    anyActive = true;

                if (partType == 0xEF && existingEspStartLba == 0)
                    existingEspStartLba = start;

                ulong endExclusive = (ulong)start + (ulong)count;
                if (endExclusive > highestEndLba)
                    highestEndLba = endExclusive;

                if (targetPartitionStartLba != 0 && start == targetPartitionStartLba)
                    targetSlot = slot;
            }

            if (targetPartitionStartLba != 0)
            {
                if (targetSlot < 0)
                {
                    error = "Selected target partition at LBA " + targetPartitionStartLba + " was not found in the MBR partition table.";
                    return false;
                }

                int off = 446 + targetSlot * 16;
                byte partType = mbr[off + 4];
                uint start = BitConverter.ToUInt32(mbr, off + 8);
                uint count = BitConverter.ToUInt32(mbr, off + 12);

                if (partType == 0xEF)
                {
                    error = "Refusing to format the EFI System Partition (0xEF) as ZONDERQDATA in Dual-Boot mode.";
                    return false;
                }

                if (count < EfiPartitionSectors512)
                {
                    error = "Selected partition at LBA " + start + " is smaller than the 64 MB minimum required for ZonderqOS.";
                    return false;
                }

                mbr[off + 4] = 0x0C; // FAT32 LBA so DataVolumePolicy approves it
                if (!anyActive)
                    mbr[off + 0] = 0x80;

                EnsureMbrSignatureForDualBoot(device, mbr);
                device.WriteBlock(0UL, 1UL, mbr);
                sysStartLba = start;
                sysSectorCount = count;
                return true;
            }

            if (firstFreeSlot < 0)
            {
                error = "All 4 primary MBR partition slots are occupied. Select an existing partition to format as ZONDERQDATA or free one MBR slot.";
                return false;
            }

            ulong alignedStart = highestEndLba < FirstPartitionStartLba ? FirstPartitionStartLba : highestEndLba;
            ulong rem = alignedStart % 2048UL;
            if (rem != 0)
                alignedStart += (2048UL - rem);

            if (device.BlockCount <= alignedStart || (device.BlockCount - alignedStart) < EfiPartitionSectors512)
            {
                error = "Insufficient unallocated free space after existing partitions (need at least 64 MB free space, or select an existing partition).";
                return false;
            }

            ulong freeSectors = device.BlockCount - alignedStart;
            if (alignedStart > uint.MaxValue)
            {
                error = "Unallocated space starts beyond the 2 TiB MBR limit.";
                return false;
            }
            if (freeSectors > uint.MaxValue)
                freeSectors = uint.MaxValue;

            int slotOff = 446 + firstFreeSlot * 16;
            mbr[slotOff + 0] = anyActive ? (byte)0x00 : (byte)0x80;
            SetChsTuple(mbr, slotOff + 1, alignedStart);
            mbr[slotOff + 4] = 0x0C; // FAT32 LBA
            SetChsTuple(mbr, slotOff + 5, alignedStart + freeSectors - 1UL);
            WriteUInt32LE(mbr, slotOff + 8, (uint)alignedStart);
            WriteUInt32LE(mbr, slotOff + 12, (uint)freeSectors);

            EnsureMbrSignatureForDualBoot(device, mbr);
            device.WriteBlock(0UL, 1UL, mbr);

            sysStartLba = alignedStart;
            sysSectorCount = freeSectors;
            return true;
        }

        private static void EnsureMbrSignatureForDualBoot(IBlockDevice device, byte[] mbr)
        {
            uint sig = BitConverter.ToUInt32(mbr, 440);
            if (sig == 0)
            {
                sig = (uint)(0x5A510000U ^ (uint)(device.BlockCount & 0xFFFFU));
                if (sig == LimineBootMediaDetector.BootMbrDiskId)
                    sig ^= 0x0000A55AU;
                WriteUInt32LE(mbr, 440, sig);
            }
            mbr[510] = 0x55;
            mbr[511] = 0xAA;
        }

        private static bool PrepareGptForDualBoot(
            IBlockDevice device,
            ulong targetPartitionStartLba,
            out ulong sysStartLba,
            out ulong sysSectorCount,
            out ulong existingEspStartLba,
            out string error)
        {
            sysStartLba = 0;
            sysSectorCount = 0;
            existingEspStartLba = 0;
            error = null;

            List<GptPartitionEntry> gpt = Gpt.Parse(device);
            if (gpt == null)
            {
                error = "Failed to parse GPT header on target disk.";
                return false;
            }

            int blockSize = (int)device.BlockSize;
            byte[] primaryHdr = new byte[blockSize];
            device.ReadBlock(1UL, 1UL, primaryHdr);

            uint headerSize = BitConverter.ToUInt32(primaryHdr, 12);
            ulong myLba = BitConverter.ToUInt64(primaryHdr, 24);
            ulong altLba = BitConverter.ToUInt64(primaryHdr, 32);
            ulong firstUsable = BitConverter.ToUInt64(primaryHdr, 40);
            ulong lastUsable = BitConverter.ToUInt64(primaryHdr, 48);
            ulong partEntryLba = BitConverter.ToUInt64(primaryHdr, 72);
            uint numEntries = BitConverter.ToUInt32(primaryHdr, 80);
            uint entrySize = BitConverter.ToUInt32(primaryHdr, 84);

            if (entrySize < 128 || entrySize > 512 || numEntries == 0 || numEntries > 1024 || headerSize < 92 || headerSize > (uint)blockSize)
            {
                error = "Unsupported GPT entry geometry for in-place Dual-Boot modification.";
                return false;
            }

            ulong totalTableBytes = (ulong)numEntries * (ulong)entrySize;
            ulong tableSectors = (totalTableBytes + (ulong)blockSize - 1UL) / (ulong)blockSize;
            byte[] tableBytes = new byte[tableSectors * (ulong)blockSize];
            device.ReadBlock(partEntryLba, tableSectors, tableBytes);

            int firstFreeSlot = -1;
            int targetSlot = -1;
            ulong highestEndLba = firstUsable > FirstPartitionStartLba ? firstUsable : FirstPartitionStartLba;

            for (int i = 0; i < (int)numEntries; i++)
            {
                int off = i * (int)entrySize;
                bool empty = true;
                for (int b = 0; b < 16; b++)
                {
                    if (tableBytes[off + b] != 0)
                    {
                        empty = false;
                        break;
                    }
                }

                if (empty)
                {
                    if (firstFreeSlot < 0)
                        firstFreeSlot = i;
                    continue;
                }

                byte[] typeGuidBytes = new byte[16];
                Array.Copy(tableBytes, off, typeGuidBytes, 0, 16);
                Guid typeGuid = new Guid(typeGuidBytes);
                ulong firstLba = BitConverter.ToUInt64(tableBytes, off + 32);
                ulong lastLba = BitConverter.ToUInt64(tableBytes, off + 40);

                if (typeGuid == DualBootManager.GptEfiSystemPartitionGuid && existingEspStartLba == 0)
                    existingEspStartLba = firstLba;

                if (lastLba >= firstLba && lastLba + 1UL > highestEndLba)
                    highestEndLba = lastLba + 1UL;

                if (targetPartitionStartLba != 0 && firstLba == targetPartitionStartLba)
                    targetSlot = i;
            }

            int writeSlot;
            ulong newFirstLba;
            ulong newLastLba;

            if (targetPartitionStartLba != 0)
            {
                if (targetSlot < 0)
                {
                    error = "Selected GPT partition at LBA " + targetPartitionStartLba + " was not found.";
                    return false;
                }

                int off = targetSlot * (int)entrySize;
                byte[] typeGuidBytes = new byte[16];
                Array.Copy(tableBytes, off, typeGuidBytes, 0, 16);
                Guid typeGuid = new Guid(typeGuidBytes);
                if (typeGuid == DualBootManager.GptEfiSystemPartitionGuid)
                {
                    error = "Refusing to overwrite the GPT EFI System Partition as ZONDERQDATA.";
                    return false;
                }

                newFirstLba = BitConverter.ToUInt64(tableBytes, off + 32);
                newLastLba = BitConverter.ToUInt64(tableBytes, off + 40);
                if (newLastLba < newFirstLba || (newLastLba - newFirstLba + 1UL) < EfiPartitionSectors512)
                {
                    error = "Selected GPT partition is smaller than the 64 MB minimum required for ZonderqOS.";
                    return false;
                }
                writeSlot = targetSlot;
            }
            else
            {
                if (firstFreeSlot < 0)
                {
                    error = "No empty GPT partition slot is available on the target disk.";
                    return false;
                }

                ulong alignedStart = highestEndLba < FirstPartitionStartLba ? FirstPartitionStartLba : highestEndLba;
                ulong rem = alignedStart % 2048UL;
                if (rem != 0)
                    alignedStart += (2048UL - rem);

                if (lastUsable <= alignedStart || (lastUsable - alignedStart + 1UL) < EfiPartitionSectors512)
                {
                    error = "Insufficient unallocated GPT space after existing partitions (need at least 64 MB).";
                    return false;
                }

                writeSlot = firstFreeSlot;
                newFirstLba = alignedStart;
                newLastLba = lastUsable;
            }

            int slotOff = writeSlot * (int)entrySize;
            byte[] basicTypeBytes = Gpt.BasicDataPartitionType.ToByteArray();
            Array.Copy(basicTypeBytes, 0, tableBytes, slotOff, 16);

            bool uniqueZero = true;
            for (int b = 0; b < 16; b++)
            {
                if (tableBytes[slotOff + 16 + b] != 0)
                {
                    uniqueZero = false;
                    break;
                }
            }
            if (uniqueZero)
            {
                WriteUInt64LE(tableBytes, slotOff + 16, 0x5A4F4E4445525144UL ^ newFirstLba);
                WriteUInt64LE(tableBytes, slotOff + 24, 0x4455414C424F4F54UL ^ newLastLba);
            }

            WriteUInt64LE(tableBytes, slotOff + 32, newFirstLba);
            WriteUInt64LE(tableBytes, slotOff + 40, newLastLba);
            WriteUInt64LE(tableBytes, slotOff + 48, 0UL);

            const string gptPartName = "ZonderqOS Data";
            for (int c = 0; c < 36; c++)
            {
                ushort ch = c < gptPartName.Length ? (ushort)gptPartName[c] : (ushort)0;
                tableBytes[slotOff + 56 + c * 2] = (byte)(ch & 0xFF);
                tableBytes[slotOff + 56 + c * 2 + 1] = (byte)((ch >> 8) & 0xFF);
            }

            uint entriesCrc = ComputeCrc32(tableBytes, 0, (int)totalTableBytes);
            device.WriteBlock(partEntryLba, tableSectors, tableBytes);

            WriteUInt32LE(primaryHdr, 88, entriesCrc);
            WriteUInt32LE(primaryHdr, 16, 0U);
            uint primaryHdrCrc = ComputeCrc32(primaryHdr, 0, (int)headerSize);
            WriteUInt32LE(primaryHdr, 16, primaryHdrCrc);
            device.WriteBlock(myLba, 1UL, primaryHdr);

            if (altLba > lastUsable && altLba < device.BlockCount)
            {
                try
                {
                    byte[] backupHdr = new byte[blockSize];
                    device.ReadBlock(altLba, 1UL, backupHdr);
                    ulong backupEntriesLba = BitConverter.ToUInt64(backupHdr, 72);
                    if (backupEntriesLba > lastUsable && backupEntriesLba + tableSectors <= altLba)
                    {
                        device.WriteBlock(backupEntriesLba, tableSectors, tableBytes);
                    }
                    WriteUInt32LE(backupHdr, 88, entriesCrc);
                    WriteUInt32LE(backupHdr, 16, 0U);
                    uint backupCrc = ComputeCrc32(backupHdr, 0, (int)headerSize);
                    WriteUInt32LE(backupHdr, 16, backupCrc);
                    device.WriteBlock(altLba, 1UL, backupHdr);
                }
                catch
                {
                }
            }

            sysStartLba = newFirstLba;
            sysSectorCount = newLastLba - newFirstLba + 1UL;
            return true;
        }

        private static void WriteUInt32LE(byte[] buf, int offset, uint value)
        {
            buf[offset + 0] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
        }

        private static void WriteUInt64LE(byte[] buf, int offset, ulong value)
        {
            buf[offset + 0] = (byte)(value & 0xFF);
            buf[offset + 1] = (byte)((value >> 8) & 0xFF);
            buf[offset + 2] = (byte)((value >> 16) & 0xFF);
            buf[offset + 3] = (byte)((value >> 24) & 0xFF);
            buf[offset + 4] = (byte)((value >> 32) & 0xFF);
            buf[offset + 5] = (byte)((value >> 40) & 0xFF);
            buf[offset + 6] = (byte)((value >> 48) & 0xFF);
            buf[offset + 7] = (byte)((value >> 56) & 0xFF);
        }

        private static uint ComputeCrc32(byte[] data, int offset, int length)
        {
            uint crc = 0xFFFFFFFFU;
            for (int i = 0; i < length; i++)
            {
                crc ^= data[offset + i];
                for (int bit = 0; bit < 8; bit++)
                {
                    if ((crc & 1U) != 0)
                        crc = (crc >> 1) ^ 0xEDB88320U;
                    else
                        crc >>= 1;
                }
            }
            return ~crc;
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
            if (!EnsureRootPrivilege(out error))
                return false;

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
            if (!EnsureRootPrivilege(out error))
                return false;

            if (!InstallerInputValidator.ValidateDiskConfirmation(expectedConfirmCode, typedConfirmation))
            {
                error = "Confirmation code does not match.";
                return false;
            }

            if (mbrPartitionSlotIndex < 0 || mbrPartitionSlotIndex > 3)
            {
                error = "Invalid MBR partition slot index.";
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

        public static int FindRawMbrSlotByStartLba(IBlockDevice device, ulong startLba)
        {
            if (device == null || device.BlockSize != 512UL || device.BlockCount == 0UL || startLba == 0UL)
                return -1;

            try
            {
                byte[] sector0 = new byte[512];
                device.ReadBlock(0UL, 1UL, sector0);
                if (sector0[510] != 0x55 || sector0[511] != 0xAA)
                    return -1;

                for (int slot = 0; slot < 4; slot++)
                {
                    int off = 446 + slot * 16;
                    byte partType = sector0[off + 4];
                    if (partType == 0)
                        continue;

                    uint lba = (uint)(sector0[off + 8] |
                                      (sector0[off + 9] << 8) |
                                      (sector0[off + 10] << 16) |
                                      (sector0[off + 11] << 24));
                    if ((ulong)lba == startLba)
                        return slot;
                }
            }
            catch
            {
                // fall through
            }

            return -1;
        }

        public static bool FormatPartitionWithIdentity(
            DiskIdentityToken diskToken,
            ulong partitionStartLba,
            string expectedConfirmCode,
            string typedConfirmation,
            string volumeLabel,
            out string error)
        {
            error = null;
            if (!EnsureRootPrivilege(out error))
                return false;

            if (!InstallerInputValidator.ValidateDiskConfirmation(expectedConfirmCode, typedConfirmation))
            {
                error = "Confirmation code does not match.";
                return false;
            }

            if (!DiskCatalog.VerifyIdentityBeforeWrite(diskToken, out error))
                return false;

            IBlockDevice device = diskToken.DeviceReference;
            Partition target = null;
            var parts = StorageManager.Partitions;
            if (parts != null)
            {
                for (int i = 0; i < parts.Count; i++)
                {
                    Partition p = parts[i];
                    if (p != null && object.ReferenceEquals(p.Host, device) && p.StartSector == partitionStartLba)
                    {
                        target = p;
                        break;
                    }
                }
            }

            if (target == null)
            {
                error = "Target partition no longer exists at LBA " + partitionStartLba + ".";
                return false;
            }

            return FormatAndVerifyFat32(target, volumeLabel, false, out error);
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
