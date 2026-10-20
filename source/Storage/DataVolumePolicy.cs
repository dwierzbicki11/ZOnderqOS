using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS
{
    /// <summary>
    /// Fail-closed persistent storage selection for the Cosmos 3.0.94 bare-metal build.
    /// Never treat a numeric partition index (especially 0/ESP) as permission to write.
    /// Only a separately prepared FAT32 data volume explicitly labelled ZONDERQDATA
    /// may be mounted by ZonderqOS. No partition is formatted automatically.
    /// </summary>
    public static class DataVolumePolicy
    {
        public const string RequiredLabel = "ZONDERQDATA"; // Exactly 11 FAT volume-label bytes.
        private const int LabelOffset = 71; // FAT32 BPB BS_VolLab

        public static bool TryFindDataPartition(out Partition selected, out string error)
        {
            selected = null;
            error = null;
            IReadOnlyList<Partition> partitions = StorageManager.Partitions;
            for (int i = 0; i < partitions.Count; i++)
            {
                Partition partition = partitions[i];
                if (!IsApprovedDataPartition(partition))
                    continue;

                if (selected != null)
                {
                    selected = null;
                    error = "Multiple FAT32 ZONDERQDATA volumes found; refusing an ambiguous write target.";
                    return false;
                }

                selected = partition;
            }

            if (selected == null)
            {
                error = "No dedicated FAT32 volume labelled ZONDERQDATA found; refusing to write to EFI or other disks.";
                return false;
            }
            return true;
        }

        public static bool IsApprovedDataPartition(Partition partition)
        {
            if (partition == null || partition.BlockCount < 65536UL ||
                partition.BlockSize < 512UL || partition.BlockSize > 4096UL)
                return false;

            try
            {
                // A label is not an authorization boundary. On Cosmos 3.0.94
                // also inspect the host partition table and explicitly reject
                // GPT EFI System Partitions and MBR type 0xEF, even if someone
                // relabels them ZONDERQDATA.
                if (!IsDedicatedDataPartitionType(partition))
                    return false;

                byte[] sector = new byte[checked((int)partition.BlockSize)];
                partition.ReadBlock(0UL, 1UL, sector);
                if (!FatBootSector.TryParse(sector, out FatBootSector boot) ||
                    boot.Type != FatType.Fat32 ||
                    boot.BytesPerSector != partition.BlockSize ||
                    boot.TotalSectorCount > partition.BlockCount)
                    return false;

                for (int i = 0; i < RequiredLabel.Length; i++)
                {
                    if (sector[LabelOffset + i] != (byte)RequiredLabel[i])
                        return false;
                }

                return true;
            }
            catch
            {
                // Unknown/failed reads must never authorize storage mutations.
                return false;
            }
        }

        private static bool IsDedicatedDataPartitionType(Partition partition)
        {
            // Do not allow unpartitioned/raw volumes or unsupported partition
            // table layouts to become writable roots.
            if (partition.Host == null || partition.Host.BlockSize < 512)
                return false;

            if (Gpt.IsGpt(partition.Host))
            {
                List<GptPartitionEntry> entries = Gpt.Parse(partition.Host);
                for (int i = 0; i < entries.Count; i++)
                {
                    GptPartitionEntry entry = entries[i];
                    if (entry.StartSector == partition.StartSector &&
                        entry.SectorCount == partition.BlockCount)
                        return entry.PartitionType == Gpt.BasicDataPartitionType;
                }
                return false;
            }

            if (!Mbr.IsMbr(partition.Host))
                return false;

            List<MbrPartitionEntry> primary = Mbr.Parse(partition.Host);
            for (int i = 0; i < primary.Count; i++)
            {
                MbrPartitionEntry entry = primary[i];
                if (entry.StartSector == partition.StartSector &&
                    entry.SectorCount == partition.BlockCount)
                {
                    // FAT32 CHS/LBA only. This excludes 0xEF (EFI system),
                    // GPT protective 0xEE, NTFS/exFAT and other host volumes.
                    return entry.SystemId == 0x0B || entry.SystemId == 0x0C;
                }
            }

            // Extended/logical and unknown schemes need separate audit before
            // they can safely be used as a persistent root.
            return false;
        }

        public static bool IsSelectedDataPartition(Partition partition)
        {
            if (partition == null || !IsApprovedDataPartition(partition))
                return false;

            if (!TryFindDataPartition(out Partition selected, out string error))
                return false;

            return object.ReferenceEquals(partition, selected);
        }
    }
}
