using System;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.FileSystem.Fat;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.Storage
{
    /// <summary>
    /// Read-only filesystem and volume label probe for disk and partition discovery.
    /// Never modifies sectors or mounts volumes.
    /// </summary>
    public static class FilesystemSignatureProbe
    {
        public static string DetectFilesystemName(byte[] sector0, byte[] extSuperblock)
        {
            if (sector0 == null || sector0.Length < 512)
                return "Unknown";

            // NTFS OEM ID at offset 3: "NTFS    "
            if (sector0[3] == (byte)'N' &&
                sector0[4] == (byte)'T' &&
                sector0[5] == (byte)'F' &&
                sector0[6] == (byte)'S')
            {
                return "NTFS";
            }

            // exFAT OEM ID at offset 3: "EXFAT   "
            if (sector0[3] == (byte)'E' &&
                sector0[4] == (byte)'X' &&
                sector0[5] == (byte)'F' &&
                sector0[6] == (byte)'A' &&
                sector0[7] == (byte)'T')
            {
                return "exFAT";
            }

            // FAT32 signature at offset 82 ("FAT32") + 0x55AA boot signature
            if (sector0[510] == 0x55 && sector0[511] == 0xAA)
            {
                if (sector0[82] == (byte)'F' &&
                    sector0[83] == (byte)'A' &&
                    sector0[84] == (byte)'T' &&
                    sector0[85] == (byte)'3' &&
                    sector0[86] == (byte)'2')
                {
                    return "FAT32";
                }

                if (sector0[54] == (byte)'F' &&
                    sector0[55] == (byte)'A' &&
                    sector0[56] == (byte)'T' &&
                    sector0[57] == (byte)'1' &&
                    sector0[58] == (byte)'6')
                {
                    return "FAT16";
                }

                if (sector0[54] == (byte)'F' &&
                    sector0[55] == (byte)'A' &&
                    sector0[56] == (byte)'T' &&
                    sector0[57] == (byte)'1' &&
                    sector0[58] == (byte)'2')
                {
                    return "FAT12";
                }

                if (FatBootSector.TryParse(sector0, out FatBootSector parsed))
                {
                    if (parsed.Type == FatType.Fat32) return "FAT32";
                    if (parsed.Type == FatType.Fat16) return "FAT16";
                    if (parsed.Type == FatType.Fat12) return "FAT12";
                }
            }

            // Ext2/3/4 superblock magic 0xEF53 at byte offset 56 of superblock (1024 + 56 = 1080 from partition start)
            if (extSuperblock != null && extSuperblock.Length >= 60 &&
                extSuperblock[56] == 0x53 && extSuperblock[57] == 0xEF)
            {
                return "EXT2/3/4";
            }

            bool allZero = true;
            for (int i = 0; i < 512; i++)
            {
                if (sector0[i] != 0)
                {
                    allZero = false;
                    break;
                }
            }

            if (allZero)
                return "Unformatted";

            return "Unknown";
        }

        public static string ExtractFatVolumeLabel(byte[] sector0)
        {
            if (sector0 == null || sector0.Length < 512)
                return string.Empty;

            int offset = -1;
            if (sector0[82] == (byte)'F' && sector0[83] == (byte)'A' &&
                sector0[84] == (byte)'T' && sector0[85] == (byte)'3' && sector0[86] == (byte)'2')
            {
                offset = 71;
            }
            else if (sector0[54] == (byte)'F' && sector0[55] == (byte)'A' && sector0[56] == (byte)'T')
            {
                offset = 43;
            }

            if (offset < 0)
                return string.Empty;

            char[] chars = new char[11];
            int len = 0;
            for (int i = 0; i < 11; i++)
            {
                byte b = sector0[offset + i];
                if (b < 32 || b > 126)
                    chars[i] = ' ';
                else
                    chars[i] = (char)b;

                if (chars[i] != ' ')
                    len = i + 1;
            }

            if (len == 0)
                return string.Empty;

            string label = new string(chars, 0, len);
            if (string.Equals(label, "NO NAME", StringComparison.OrdinalIgnoreCase))
                return string.Empty;
            return label;
        }

        public static string ProbePartitionFilesystem(Partition partition, out string volumeLabel)
        {
            volumeLabel = string.Empty;
            if (partition == null || partition.BlockCount == 0 || partition.BlockSize < 512 || partition.BlockSize > 4096)
                return "Unknown";

            try
            {
                int blockBytes = (int)partition.BlockSize;
                byte[] sector0 = new byte[blockBytes];
                partition.ReadBlock(0UL, 1UL, sector0);

                byte[] extSb = null;
                ulong extLba = 1024UL / partition.BlockSize;
                int extOffset = (int)(1024UL % partition.BlockSize);
                if (partition.BlockCount > extLba + 1UL)
                {
                    byte[] rawExt = new byte[blockBytes];
                    partition.ReadBlock(extLba, 1UL, rawExt);
                    if (extOffset + 64 <= rawExt.Length)
                    {
                        extSb = new byte[64];
                        Array.Copy(rawExt, extOffset, extSb, 0, 64);
                    }
                }

                string fs = DetectFilesystemName(sector0, extSb);
                if (fs == "FAT32" || fs == "FAT16" || fs == "FAT12")
                    volumeLabel = ExtractFatVolumeLabel(sector0);

                return fs;
            }
            catch
            {
                return "Unknown";
            }
        }

        public static bool HasIso9660Signature(IBlockDevice device)
        {
            if (device == null || device.BlockSize < 512 || device.BlockSize > 4096)
                return false;

            try
            {
                // ISO9660 Primary Volume Descriptor sits at byte offset 32768 (0x8000).
                ulong targetByteOffset = 32768UL;
                ulong lba = targetByteOffset / device.BlockSize;
                int offsetInBlock = (int)(targetByteOffset % device.BlockSize);
                if (device.BlockCount <= lba || offsetInBlock + 6 > (int)device.BlockSize)
                    return false;

                byte[] sector = new byte[(int)device.BlockSize];
                device.ReadBlock(lba, 1UL, sector);
                return sector[offsetInBlock + 1] == (byte)'C' &&
                       sector[offsetInBlock + 2] == (byte)'D' &&
                       sector[offsetInBlock + 3] == (byte)'0' &&
                       sector[offsetInBlock + 4] == (byte)'0' &&
                       sector[offsetInBlock + 5] == (byte)'1';
            }
            catch
            {
                return false;
            }
        }
    }
}
