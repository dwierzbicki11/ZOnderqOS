using System;
using System.Collections.Generic;
using System.Text;
using Cosmos.Kernel.HAL.Devices.Storage;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.Storage;
using Org.BouncyCastle.Crypto.Digests;

namespace ZonderqOS.Storage
{
    public sealed class DiskPartitionSummary
    {
        public int GlobalPartitionIndex { get; set; } = -1;
        public Partition PartitionReference { get; set; }
        public string Name { get; set; } = "Unknown";
        public ulong StartSector { get; set; }
        public ulong SectorCount { get; set; }
        public ulong BlockSize { get; set; }
        public ulong SizeMegabytes { get; set; }
        public string PartitionType { get; set; } = "Unknown";
        public string Filesystem { get; set; } = "Unknown";
        public string VolumeLabel { get; set; } = string.Empty;
        public string MountPoint { get; set; } = string.Empty;
        public bool IsProtectedSystemPartition { get; set; }
    }

    public sealed class DiskCatalogEntry
    {
        public string DiskId { get; set; } = "DISK-0";
        public int DeviceIndex { get; set; }
        public IBlockDevice Device { get; set; }
        public string DeviceName { get; set; } = "Unknown";
        public string Model { get; set; } = "Unknown";
        public string InterfaceType { get; set; } = "Unknown";
        public ulong BlockCount { get; set; }
        public ulong BlockSize { get; set; }
        public ulong TotalBytes { get; set; }
        public ulong SizeMegabytes { get; set; }
        public string FormattedCapacity { get; set; } = "0 MB";
        public string PartitionTableType { get; set; } = "None";
        public int PartitionCount { get; set; }
        public DiskPartitionSummary[] Partitions { get; set; } = Array.Empty<DiskPartitionSummary>();
        public string PartitionsSummary { get; set; } = "None";
        public string FilesystemsSummary { get; set; } = "None";
        public bool HasExistingData { get; set; }
        public bool IsInstallationMedia { get; set; }
        public bool IsActiveSystemDisk { get; set; }
        public bool IsReadOnly { get; set; }
        public bool CanInstall { get; set; }
        public string BlockReason { get; set; } = string.Empty;
        public DiskIdentityToken IdentityToken { get; set; }
    }

    /// <summary>
    /// Discovers physical block devices, inspects partition tables and filesystems,
    /// identifies protected boot/installation/system media, and issues pre-write
    /// cryptographic identity tokens.
    /// </summary>
    public static class DiskCatalog
    {
        public const ulong MinInstallableMegabytes = 68UL;
        private static readonly Guid GptEfiSystemPartitionGuid =
            new Guid("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");

        public static List<DiskCatalogEntry> DiscoverPhysicalDisks()
        {
            var results = new List<DiskCatalogEntry>(8);
            try
            {
                LimineBootMediaDetector.Initialize();
                int count = StorageManager.DeviceCount;
                int visibleSlot = 0;

                for (int i = 0; i < count; i++)
                {
                    IBlockDevice device = StorageManager.GetDevice(i);
                    if (device == null)
                        continue;

                    // Exclude ephemeral in-memory RAM root block device from physical disk list.
                    if (RamRootVolume.IsRamDevice(device))
                        continue;

                    DiskCatalogEntry entry = InspectDevice(device, i, visibleSlot);
                    results.Add(entry);
                    visibleSlot++;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("DiskCatalog discovery error: " + ex.Message, "STORAGE");
            }

            return results;
        }

        public static DiskCatalogEntry InspectDevice(IBlockDevice device, int deviceIndex, int displaySlot)
        {
            var entry = new DiskCatalogEntry
            {
                DiskId = "DISK-" + displaySlot,
                DeviceIndex = deviceIndex,
                Device = device,
                DeviceName = string.IsNullOrWhiteSpace(device?.Name) ? "Unknown" : device.Name,
                BlockCount = device != null ? device.BlockCount : 0UL,
                BlockSize = device != null ? device.BlockSize : 0UL
            };

            if (device == null || entry.BlockCount == 0 || entry.BlockSize < 512)
            {
                entry.CanInstall = false;
                entry.BlockReason = "Device is unavailable or reports invalid geometry.";
                return entry;
            }

            entry.TotalBytes = SaturatingMultiply(entry.BlockCount, entry.BlockSize);
            entry.SizeMegabytes = entry.TotalBytes / (1024UL * 1024UL);
            entry.FormattedCapacity = FormatCapacity(entry.TotalBytes);

            ResolveHardwareMetadata(device, out string model, out string interfaceType);
            entry.Model = model;
            entry.InterfaceType = interfaceType;

#pragma warning disable COSMOS0003
            if (device is IRemovableMedia removable && removable.IsReadOnly)
                entry.IsReadOnly = true;
#pragma warning restore COSMOS0003

            entry.IsInstallationMedia = LimineBootMediaDetector.IsBootOrInstallationMedia(device);
            entry.IsActiveSystemDisk = IsDeviceHostingActiveRoot(device);

            InspectPartitionsAndFilesystems(entry);

            // Capture cryptographic identity token (LBA 0 + LBA 1 SHA-256 + MBR signature)
            uint mbrSig = LimineBootMediaDetector.ReadDeviceMbrSignature(device);
            string s0Hash = ComputeSectorSha256(device, 0UL);
            string s1Hash = entry.BlockCount > 1UL ? ComputeSectorSha256(device, 1UL) : s0Hash;
            entry.IdentityToken = new DiskIdentityToken(
                entry.DiskId,
                device,
                entry.DeviceName,
                entry.BlockCount,
                entry.BlockSize,
                mbrSig,
                s0Hash,
                s1Hash);

            if (entry.IsInstallationMedia)
            {
                entry.CanInstall = false;
                entry.BlockReason = "Protected: Active installation media";
            }
            else if (entry.IsActiveSystemDisk)
            {
                entry.CanInstall = false;
                entry.BlockReason = "Protected: Active system disk (/)";
            }
            else if (entry.IsReadOnly)
            {
                entry.CanInstall = false;
                entry.BlockReason = "Protected: Read-only storage device";
            }
            else if (entry.SizeMegabytes < MinInstallableMegabytes)
            {
                entry.CanInstall = false;
                entry.BlockReason = "Disk capacity too small (minimum 68 MB required)";
            }
            else
            {
                entry.CanInstall = true;
                entry.BlockReason = string.Empty;
            }

            return entry;
        }

        public static bool VerifyIdentityBeforeWrite(DiskIdentityToken token, out string error)
        {
            error = null;
            if (token == null || token.DeviceReference == null)
            {
                error = "Invalid or missing disk identity token.";
                return false;
            }

            IBlockDevice device = token.DeviceReference;

            // 1. Confirm the exact device object is still present in StorageManager
            bool foundInManager = false;
            int count = StorageManager.DeviceCount;
            for (int i = 0; i < count; i++)
            {
                if (object.ReferenceEquals(StorageManager.GetDevice(i), device))
                {
                    foundInManager = true;
                    break;
                }
            }

            if (!foundInManager)
            {
                error = "Target disk is no longer connected to the storage controller.";
                return false;
            }

            // 2. Hard-block RAM root, installation media, and active system disks
            if (RamRootVolume.IsRamDevice(device))
            {
                error = "Refusing to format ephemeral RAM root device.";
                return false;
            }

            if (LimineBootMediaDetector.IsBootOrInstallationMedia(device))
            {
                error = "Refusing to format active installation media.";
                return false;
            }

            if (IsDeviceHostingActiveRoot(device) || HasAnyMountedPartition(device))
            {
                error = "Refusing to format a disk with active mounted partitions.";
                return false;
            }

            // 3. Re-read LBA 0 and LBA 1 and verify cryptographic identity token
            uint currentMbrSig = LimineBootMediaDetector.ReadDeviceMbrSignature(device);
            string currentS0 = ComputeSectorSha256(device, 0UL);
            string currentS1 = device.BlockCount > 1UL ? ComputeSectorSha256(device, 1UL) : currentS0;

            if (!token.Matches(
                device,
                device.Name,
                device.BlockCount,
                device.BlockSize,
                currentMbrSig,
                currentS0,
                currentS1,
                out string mismatch))
            {
                error = "Pre-write disk identity verification failed: " + mismatch;
                return false;
            }

            return true;
        }

        public static bool IsDeviceHostingActiveRoot(IBlockDevice device)
        {
            if (device == null)
                return false;

            try
            {
                var mounts = VfsManager.Mounts;
                if (mounts != null)
                {
                    foreach (VfsMount mount in mounts)
                    {
                        if (mount == null)
                            continue;

                        if (mount.Partition != null && object.ReferenceEquals(mount.Partition.Host, device))
                        {
                            if (mount.MountPoint == "/")
                                return true;
                        }

                        if (mount.MountPoint == "/" && mount.Name == "rootfat")
                        {
                            var parts = StorageManager.Partitions;
                            if (parts != null)
                            {
                                for (int i = 0; i < parts.Count; i++)
                                {
                                    Partition p = parts[i];
                                    if (p != null &&
                                        object.ReferenceEquals(p.Host, device) &&
                                        DataVolumePolicy.IsSelectedDataPartition(p))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        public static bool HasAnyMountedPartition(IBlockDevice device)
        {
            if (device == null)
                return false;

            if (IsDeviceHostingActiveRoot(device))
                return true;

            try
            {
                var mounts = VfsManager.Mounts;
                if (mounts != null)
                {
                    foreach (VfsMount mount in mounts)
                    {
                        if (mount?.Partition != null && object.ReferenceEquals(mount.Partition.Host, device))
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void InspectPartitionsAndFilesystems(DiskCatalogEntry entry)
        {
            IBlockDevice device = entry.Device;
            bool isIso = FilesystemSignatureProbe.HasIso9660Signature(device);
            bool isGpt = false;
            bool isMbr = false;

            try { isGpt = Gpt.IsGpt(device); } catch { isGpt = false; }
            if (!isGpt)
            {
                try { isMbr = Mbr.IsMbr(device); } catch { isMbr = false; }
            }

            if (isIso)
                entry.PartitionTableType = "ISO9660 / Hybrid";
            else if (isGpt)
                entry.PartitionTableType = "GPT";
            else if (isMbr)
                entry.PartitionTableType = "MBR";
            else
                entry.PartitionTableType = "None (Unallocated)";

            var partSummaries = new List<DiskPartitionSummary>(4);
            var fsLabels = new List<string>(4);

            try
            {
                var allPartitions = StorageManager.Partitions;
                if (allPartitions != null)
                {
                    for (int i = 0; i < allPartitions.Count; i++)
                    {
                        Partition p = allPartitions[i];
                        if (p == null || !object.ReferenceEquals(p.Host, device))
                            continue;

                        ulong sizeMb = SaturatingMultiply(p.BlockCount, p.BlockSize) / (1024UL * 1024UL);
                        string fs = FilesystemSignatureProbe.ProbePartitionFilesystem(p, out string label);
                        string partType = ResolvePartitionTypeDescription(device, p, isGpt, isMbr);
                        string mountPoint = ZonderqOS.GUI.StorageMountManager.GetMountPoint(i) ?? string.Empty;
                        bool isProtected = mountPoint == "/" ||
                                           string.Equals(label, DataVolumePolicy.RequiredLabel, StringComparison.OrdinalIgnoreCase) &&
                                           entry.IsActiveSystemDisk;

                        var summary = new DiskPartitionSummary
                        {
                            GlobalPartitionIndex = i,
                            PartitionReference = p,
                            Name = string.IsNullOrWhiteSpace(p.Name) ? "part" + partSummaries.Count : p.Name,
                            StartSector = p.StartSector,
                            SectorCount = p.BlockCount,
                            BlockSize = p.BlockSize,
                            SizeMegabytes = sizeMb,
                            PartitionType = partType,
                            Filesystem = fs,
                            VolumeLabel = label,
                            MountPoint = mountPoint,
                            IsProtectedSystemPartition = isProtected
                        };
                        partSummaries.Add(summary);

                        string fsDisplay = string.IsNullOrEmpty(label) ? fs : (fs + " (" + label + ")");
                        fsLabels.Add(fsDisplay);
                    }
                }
            }
            catch
            {
            }

            entry.Partitions = partSummaries.ToArray();
            entry.PartitionCount = entry.Partitions.Length;

            if (entry.PartitionCount > 0)
            {
                var sbParts = new StringBuilder();
                for (int i = 0; i < entry.Partitions.Length; i++)
                {
                    if (i > 0) sbParts.Append(", ");
                    sbParts.Append(entry.Partitions[i].Name)
                           .Append(" (")
                           .Append(entry.Partitions[i].SizeMegabytes)
                           .Append(" MB)");
                }
                entry.PartitionsSummary = sbParts.ToString();

                var sbFs = new StringBuilder();
                for (int i = 0; i < fsLabels.Count; i++)
                {
                    if (i > 0) sbFs.Append(", ");
                    sbFs.Append(fsLabels[i]);
                }
                entry.FilesystemsSummary = sbFs.ToString();
                entry.HasExistingData = true;
            }
            else
            {
                entry.PartitionsSummary = isIso ? "ISO9660 Image" : "None (0 partitions)";
                string rawFs = ProbeRawDiskSector0Filesystem(device);
                entry.FilesystemsSummary = isIso ? "ISO9660" : (rawFs == "Unformatted" ? "None" : rawFs);
                entry.HasExistingData = isIso || isGpt || isMbr || HasNonZeroDataSample(device);
            }
        }

        private static string ResolvePartitionTypeDescription(IBlockDevice device, Partition p, bool isGpt, bool isMbr)
        {
            try
            {
                if (isGpt)
                {
                    List<GptPartitionEntry> entries = Gpt.Parse(device);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].StartSector == p.StartSector)
                        {
                            if (entries[i].PartitionType == GptEfiSystemPartitionGuid)
                                return "EFI System (ESP)";
                            if (entries[i].PartitionType == Gpt.BasicDataPartitionType)
                                return "Basic Data";
                            return "GPT Partition";
                        }
                    }
                }
                else if (isMbr)
                {
                    List<MbrPartitionEntry> entries = Mbr.Parse(device);
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].StartSector == p.StartSector)
                        {
                            byte id = entries[i].SystemId;
                            switch (id)
                            {
                                case 0x0B:
                                case 0x0C: return "FAT32 (0x0C)";
                                case 0x0E:
                                case 0x06: return "FAT16";
                                case 0x07: return "NTFS/exFAT (0x07)";
                                case 0x83: return "Linux (0x83)";
                                case 0xEF: return "EFI System (0xEF)";
                                default: return "MBR 0x" + id.ToString("X2");
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private static string ProbeRawDiskSector0Filesystem(IBlockDevice device)
        {
            try
            {
                byte[] sector0 = new byte[(int)device.BlockSize];
                device.ReadBlock(0UL, 1UL, sector0);
                return FilesystemSignatureProbe.DetectFilesystemName(sector0, null);
            }
            catch
            {
                return "Unknown";
            }
        }

        private static bool HasNonZeroDataSample(IBlockDevice device)
        {
            try
            {
                byte[] buf = new byte[(int)device.BlockSize];
                ulong[] sampleLbas = { 0UL, 1UL, 2048UL };
                for (int s = 0; s < sampleLbas.Length; s++)
                {
                    ulong lba = sampleLbas[s];
                    if (lba >= device.BlockCount)
                        continue;

                    device.ReadBlock(lba, 1UL, buf);
                    for (int i = 0; i < buf.Length; i++)
                    {
                        if (buf[i] != 0)
                            return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private static void ResolveHardwareMetadata(IBlockDevice device, out string model, out string interfaceType)
        {
            model = "Unknown";
            interfaceType = "Unknown";

            if (device == null)
                return;

            string rawName = device.Name ?? string.Empty;

            // 1. Inspect DriverDiagnostics published devices and device nodes
            try
            {
                if (DriverDiagnostics.IsStarted)
                {
                    int pubCount = DriverDiagnostics.DeviceCount;
                    for (int i = 0; i < pubCount; i++)
                    {
                        if (!DriverDiagnostics.TryGetDevice(i, out PublishedDeviceInfo pub))
                            continue;

                        if (!string.IsNullOrEmpty(pub.Name) &&
                            string.Equals(pub.Name, rawName, StringComparison.OrdinalIgnoreCase))
                        {
                            ClassifyDriver(pub.DriverName, pub.NodePath, out interfaceType, out string desc);
                            if (!string.IsNullOrWhiteSpace(desc))
                                model = desc;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }

            // 2. Fallback classification from device name prefix if DriverDiagnostics did not match
            if (interfaceType == "Unknown" && !string.IsNullOrEmpty(rawName))
            {
                string lower = rawName.ToLowerInvariant();
                if (lower.StartsWith("ahci") || lower.StartsWith("sata"))
                    interfaceType = "SATA / AHCI";
                else if (lower.StartsWith("nvme"))
                    interfaceType = "NVMe";
                else if (lower.StartsWith("usb"))
                    interfaceType = "USB Mass Storage";
                else if (lower.StartsWith("virtio") || lower.StartsWith("vd"))
                    interfaceType = "VirtIO Block";
                else if (lower.StartsWith("ata") || lower.StartsWith("ide"))
                    interfaceType = "ATA / IDE";
            }

            if (model == "Unknown" && !string.IsNullOrWhiteSpace(rawName))
            {
                if (interfaceType != "Unknown")
                    model = interfaceType + " Disk (" + rawName + ")";
                else
                    model = "Block Device (" + rawName + ")";
            }
        }

        private static void ClassifyDriver(string driverName, string nodePath, out string interfaceType, out string description)
        {
            interfaceType = "Unknown";
            description = "Unknown";

            string drv = driverName ?? string.Empty;
            if (drv.IndexOf("Ahci", StringComparison.OrdinalIgnoreCase) >= 0)
                interfaceType = "SATA / AHCI";
            else if (drv.IndexOf("Nvme", StringComparison.OrdinalIgnoreCase) >= 0)
                interfaceType = "NVMe";
            else if (drv.IndexOf("UsbMassStorage", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     drv.IndexOf("Usb", StringComparison.OrdinalIgnoreCase) >= 0)
                interfaceType = "USB Mass Storage";
            else if (drv.IndexOf("VirtioBlk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     drv.IndexOf("Virtio", StringComparison.OrdinalIgnoreCase) >= 0)
                interfaceType = "VirtIO Block";

            if (!string.IsNullOrEmpty(nodePath))
            {
                try
                {
                    int nodeCount = DriverDiagnostics.NodeCount;
                    for (int i = 0; i < nodeCount; i++)
                    {
                        if (DriverDiagnostics.TryGetNode(i, out DeviceNodeInfo node) &&
                            string.Equals(node.Path, nodePath, StringComparison.Ordinal))
                        {
                            if (!string.IsNullOrWhiteSpace(node.Description))
                                description = node.Description;
                            break;
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public static string ComputeSectorSha256(IBlockDevice device, ulong lba)
        {
            if (device == null || lba >= device.BlockCount || device.BlockSize < 512 || device.BlockSize > 4096)
                return string.Empty;

            try
            {
                byte[] sector = new byte[(int)device.BlockSize];
                device.ReadBlock(lba, 1UL, sector);
                var digest = new Sha256Digest();
                digest.BlockUpdate(sector, 0, sector.Length);
                byte[] hash = new byte[digest.GetDigestSize()];
                digest.DoFinal(hash, 0);

                var sb = new StringBuilder(hash.Length * 2);
                for (int i = 0; i < hash.Length; i++)
                    sb.Append(hash[i].ToString("x2"));
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string FormatCapacity(ulong bytes)
        {
            ulong mb = bytes / (1024UL * 1024UL);
            if (mb >= 1024UL)
            {
                ulong gbWhole = mb / 1024UL;
                ulong gbTenths = ((mb % 1024UL) * 10UL) / 1024UL;
                return gbWhole + "." + gbTenths + " GB (" + mb + " MB)";
            }
            return mb + " MB";
        }

        private static ulong SaturatingMultiply(ulong a, ulong b)
        {
            if (a == 0 || b == 0)
                return 0;
            if (a > ulong.MaxValue / b)
                return ulong.MaxValue;
            return a * b;
        }
    }
}
