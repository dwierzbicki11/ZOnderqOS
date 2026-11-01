using System;

namespace ZonderqOS
{
    // Read-only boundary: the scanner cannot format, flush or write a device.
    public interface IFatSectorReader
    {
        ulong SectorCount { get; }
        uint SectorSize { get; }
        void ReadSector(ulong sector, byte[] buffer);
    }

    public struct Fat32Space
    {
        public ulong DataBytes;
        public ulong FreeBytes;
        public ulong AllocatedBytes;
        public ulong BadBytes;
    }

    public static class Fat32Allocation
    {
        // Keep an explicit user-triggered scan bounded. Never scan on GUI paint.
        private const ulong MaxScanBytes = 16UL * 1024 * 1024;

        private static uint U16(byte[] b, int p) => (uint)(b[p] | b[p + 1] << 8);
        private static uint U32(byte[] b, int p) =>
            (uint)b[p] | (uint)b[p + 1] << 8 | (uint)b[p + 2] << 16 | (uint)b[p + 3] << 24;

        public static bool TryRead(IFatSectorReader reader, out Fat32Space space, out string error)
        {
            space = default;
            error = null;
            try
            {
                if (reader == null) throw new Exception("No bound volume.");
                uint bytes = reader.SectorSize;
                if (bytes != 512 && bytes != 1024 && bytes != 2048 && bytes != 4096)
                    throw new Exception("Unsupported sector size.");
                byte[] sector = new byte[(int)bytes];
                reader.ReadSector(0, sector);
                uint clusterSectors = sector[13];
                uint reserved = U16(sector, 14);
                uint fats = sector[16];
                uint total = U32(sector, 32);
                uint fatSectors = U32(sector, 36);
                uint flags = U16(sector, 40);
                if (sector[510] != 0x55 || sector[511] != 0xAA || U16(sector, 11) != bytes ||
                    clusterSectors == 0 || clusterSectors > 128 ||
                    (clusterSectors & (clusterSectors - 1)) != 0 || reserved == 0 ||
                    (fats != 1 && fats != 2) || U16(sector, 17) != 0 ||
                    U16(sector, 19) != 0 || U16(sector, 22) != 0 || U16(sector, 42) != 0 ||
                    fatSectors == 0 || total == 0 || total > reader.SectorCount)
                    throw new Exception("Invalid FAT32 geometry.");

                ulong dataStart = reserved + (ulong)fats * fatSectors;
                if (dataStart >= total) throw new Exception("FAT extends outside volume.");
                ulong clusters = (total - dataStart) / clusterSectors;
                if (clusters < 65525 || clusters > 0x0FFFFFEEUL ||
                    (ulong)fatSectors * bytes / 4 < clusters + 2)
                    throw new Exception("Invalid FAT32 cluster count or FAT capacity.");
                uint root = U32(sector, 44);
                if (root < 2 || root >= clusters + 2)
                    throw new Exception("Invalid root cluster.");
                bool mirrored = (flags & 0x80) == 0;
                uint active = mirrored ? 0 : flags & 0x0F;
                if (active >= fats) throw new Exception("Invalid active FAT index.");
                ulong sectorsToRead = ((clusters + 2) * 4 + bytes - 1) / bytes;
                if (sectorsToRead * bytes * (mirrored ? fats : 1) > MaxScanBytes)
                    throw new Exception("FAT allocation scan exceeds the synchronous read budget.");

                ulong fatStart = reserved + (ulong)active * fatSectors;
                byte[] mirror = mirrored && fats == 2 ? new byte[(int)bytes] : null;
                ulong free = 0, bad = 0;
                uint entriesPerSector = bytes / 4;
                for (ulong s = 0; s < sectorsToRead; s++)
                {
                    reader.ReadSector(fatStart + s, sector);
                    if (mirror != null) reader.ReadSector(reserved + (ulong)fatSectors + s, mirror);
                    for (uint e = 0; e < entriesPerSector; e++)
                    {
                        ulong cluster = s * entriesPerSector + e;
                        if (cluster < 2 || cluster >= clusters + 2) continue;
                        int offset = (int)(e * 4);
                        uint value = U32(sector, offset) & 0x0FFFFFFF;
                        if (mirror != null && value != (U32(mirror, offset) & 0x0FFFFFFF))
                            throw new Exception("Mirrored FATs disagree; allocation unavailable.");
                        if (value == 0) free++;
                        else if (value == 0x0FFFFFF7) bad++;
                        else if (value < 2 || (value < 0x0FFFFFF8 && value >= clusters + 2))
                            throw new Exception("Invalid FAT cluster entry.");
                    }
                }

                ulong clusterBytes = (ulong)bytes * clusterSectors;
                // Publish only after every required read succeeds. Metadata and
                // any trailing incomplete cluster are outside the data region.
                space = new Fat32Space {
                    DataBytes = clusters * clusterBytes,
                    FreeBytes = free * clusterBytes,
                    BadBytes = bad * clusterBytes,
                    AllocatedBytes = (clusters - free - bad) * clusterBytes
                };
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
