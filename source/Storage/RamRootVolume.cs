#if ZONDERQ_3094_RAM_ROOT
using System;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS
{
    /// <summary>
    /// 64 MiB ephemeral RAM-only disk. I/O is confined to a private byte[].
    /// A disposable FAT32 partition on it lets the *actual* Gen3 desktop,
    /// user login, settings and icon cache operate with no host-disk writes.
    /// </summary>
    internal sealed class RamRootBlockDevice : IBlockDevice
    {
        internal const int Capacity = 64 * 1024 * 1024;
        private const int SectorBytes = 512;
        private readonly byte[] storage = new byte[Capacity];
        public ulong BlockCount => (ulong)Capacity / SectorBytes;
        public ulong BlockSize => SectorBytes;
        public string Name => "zonderq-ram-root-3094";

        internal RamRootBlockDevice()
        {
            // One MBR primary FAT32-LBA partition. The host is RAM ONLY.
            Span<byte> mbr = storage.AsSpan(0, SectorBytes);
            mbr.Clear();
            const int entry = 446;
            const uint startSector = 2048;
            uint sectors = checked((uint)BlockCount) - startSector;
            mbr[entry + 4] = 0x0C;
            SetUInt32(mbr, entry + 8, startSector);
            SetUInt32(mbr, entry + 12, sectors);
            mbr[510] = 0x55;
            mbr[511] = 0xAA;
        }

        private static void SetUInt32(Span<byte> target, int offset, uint value)
        {
            target[offset] = (byte)value;
            target[offset + 1] = (byte)(value >> 8);
            target[offset + 2] = (byte)(value >> 16);
            target[offset + 3] = (byte)(value >> 24);
        }

        private int ValidateRange(ulong blockNo, ulong blocks, int suppliedBytes)
        {
            if (blocks == 0 || blockNo >= BlockCount || blocks > BlockCount - blockNo)
                throw new ArgumentOutOfRangeException(nameof(blockNo));
            ulong total = blocks * BlockSize;
            if (total > int.MaxValue || suppliedBytes < (int)total)
                throw new ArgumentException("RAM disk buffer is too short.");
            return checked((int)total);
        }

        public void ReadBlock(ulong blockNo, ulong blockCount, Span<byte> data)
        {
            int size = ValidateRange(blockNo, blockCount, data.Length);
            storage.AsSpan(checked((int)(blockNo * BlockSize)), size).CopyTo(data);
        }

        public void WriteBlock(ulong blockNo, ulong blockCount, ReadOnlySpan<byte> data)
        {
            int size = ValidateRange(blockNo, blockCount, data.Length);
            data.Slice(0, size).CopyTo(storage.AsSpan(checked((int)(blockNo * BlockSize)), size));
        }

        public void Flush()
        {
            // No persistence and no physical disk I/O, by construction.
        }
    }

    internal static class RamRootVolume
    {
        private static RamRootBlockDevice disk;

        internal static bool TryCreate(out Partition selected)
        {
            selected = null;
            try
            {
                if (disk == null)
                {
                    disk = new RamRootBlockDevice();
                    StorageManager.RegisterDevice(disk);
                }
                var partitions = StorageManager.Partitions;
                for (int i = 0; i < partitions.Count; i++)
                {
                    Partition p = partitions[i];
                    if (!object.ReferenceEquals(p.Host, disk))
                        continue;

                    if (selected != null)
                    {
                        selected = null;
                        return false;
                    }
                    selected = p;
                }

                if (selected == null)
                    return false;

                // Format only the Partition whose Host is our private RAM disk.
                // Never call global partition-zero formatting.
                var formatter = new FatFileSystemType(selected);
                if (!formatter.TryFormat(ReadOnlySpan<char>.Empty,
                    new FatFormatOptions
                    {
                        Type = FatType.Fat32,
                        VolumeLabel = DataVolumePolicy.RequiredLabel,
                        SectorsPerCluster = 1,
                    }))
                {
                    selected = null;
                    return false;
                }

                if (!DataVolumePolicy.IsApprovedDataPartition(selected))
                {
                    selected = null;
                    return false;
                }

                Console.WriteLine("[RAMROOT3094] 64 MiB FAT32 in RAM: all files ephemeral, host disks untouched.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[RAMROOT3094] Failed safely: " + ex.Message);
                selected = null;
                return false;
            }
        }
    }
}
#endif
