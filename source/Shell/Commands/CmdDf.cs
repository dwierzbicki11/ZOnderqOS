using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.FileSystem;

namespace ZonderqOS.Commands
{
    public sealed class CmdDf : ICommand
    {
        public string Name => "df";
        public string Description => "Show mounted filesystem usage";

        public void Execute(string[] args, ref string currentPath)
        {
            bool human = false;
            if (args.Length == 2 && (args[1] == "-h" || args[1] == "--human"))
                human = true;
            else if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: df [-h]");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                var mounts = new List<VfsMount>();
                foreach (VfsMount mount in VfsManager.Mounts)
                    mounts.Add(mount);

                CommandIO.WriteLine(human
                    ? "Filesystem   Size       Used       Avail      Use%  Mounted on"
                    : "Filesystem   1K-blocks  Used       Available  Use%  Mounted on");

                for (int i = 0; i < mounts.Count; i++)
                {
                    VfsMount mount = mounts[i];
                    string mountPoint = Normalize(mount.MountPoint);
                    ulong totalBytes = ResolveTotalBytes(mount);
                    ulong usedBytes = CalculateMountUsage(
                        mountPoint,
                        mounts,
                        mountPoint,
                        0);

                    if (totalBytes > 0 && usedBytes > totalBytes)
                        usedBytes = totalBytes;

                    ulong available = totalBytes >= usedBytes ? totalBytes - usedBytes : 0;
                    string percent;
                    if (totalBytes == 0)
                    {
                        percent = "N/A";
                    }
                    else
                    {
                        ulong pct = usedBytes >= totalBytes
                            ? 100UL
                            : (usedBytes <= ulong.MaxValue / 100UL
                                ? (usedBytes * 100UL) / totalBytes
                                : (ulong)((double)usedBytes * 100d / totalBytes));
                        percent = pct.ToString() + "%";
                    }

                    string fsLabel = !string.IsNullOrEmpty(mount.Source)
                        ? mount.Source
                        : (!string.IsNullOrEmpty(mount.Name) ? mount.Name : "?");

                    CommandIO.WriteLine(
                        fsLabel.PadRight(12) +
                        Format(totalBytes, human).PadRight(11) +
                        Format(usedBytes, human).PadRight(11) +
                        Format(available, human).PadRight(11) +
                        percent.PadRight(6) +
                        mountPoint);
                }

                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.WriteLine("df: " + ex.Message);
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static ulong ResolveTotalBytes(VfsMount mount)
        {
            if (mount == null)
                return 0;

            if (mount.Partition != null)
                return SafeMultiplyBytes(mount.Partition.BlockCount, (ulong)mount.Partition.BlockSize);

            var partitions = StorageManager.Partitions;
            if (partitions == null || partitions.Count == 0)
                return 0;

            if (int.TryParse(mount.Source, out int partitionIndex) &&
                partitionIndex >= 0 && partitionIndex < partitions.Count)
            {
                var partition = partitions[partitionIndex];
                return SafeMultiplyBytes(partition.BlockCount, (ulong)partition.BlockSize);
            }

            string mountPoint = Normalize(mount.MountPoint);
            if (mountPoint == "/" ||
                string.Equals(mount.Name, "rootfat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mount.Name, "ramfat", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < partitions.Count; i++)
                {
                    var candidate = partitions[i];
                    if (DataVolumePolicy.IsSelectedDataPartition(candidate))
                        return SafeMultiplyBytes(candidate.BlockCount, (ulong)candidate.BlockSize);
                }
            }

            return 0;
        }

        private static ulong SafeMultiplyBytes(ulong blocks, ulong blockSize)
        {
            if (blocks == 0 || blockSize == 0)
                return 0;

            if (blocks > ulong.MaxValue / blockSize)
                return ulong.MaxValue;

            return blocks * blockSize;
        }

        private static ulong CalculateMountUsage(
            string path,
            List<VfsMount> mounts,
            string rootMountPoint,
            int depth)
        {
            if (depth > 64)
                return 0;

            ulong total = 0;

            try
            {
                string[] files = Directory.GetFiles(path);
                for (int i = 0; i < files.Length; i++)
                {
                    try
                    {
                        long length = new FileInfo(files[i]).Length;
                        if (length <= 0)
                            continue;

                        total = AddSaturated(total, (ulong)length);
                    }
                    catch
                    {
                        // Skip unreadable/special entries without aborting the scan.
                    }
                }

                string[] directories = Directory.GetDirectories(path);
                for (int i = 0; i < directories.Length; i++)
                {
                    string child = Normalize(directories[i]);
                    if (IsNestedMountPoint(child, mounts, rootMountPoint))
                        continue;

                    total = AddSaturated(total, CalculateMountUsage(child, mounts, rootMountPoint, depth + 1));
                }
            }
            catch
            {
                // Keep df useful even when one directory cannot be inspected.
            }

            return total;
        }

        private static bool IsNestedMountPoint(
            string path,
            List<VfsMount> mounts,
            string rootMountPoint)
        {
            for (int i = 0; i < mounts.Count; i++)
            {
                string mountPoint = Normalize(mounts[i].MountPoint);
                if (string.Equals(mountPoint, rootMountPoint, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (string.Equals(mountPoint, path, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private static ulong AddSaturated(ulong left, ulong right)
        {
            return left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
        }

        private static string Format(ulong bytes, bool human)
        {
            if (!human)
                return (bytes / 1024UL).ToString();

            if (bytes >= 1099511627776UL)
                return ((double)bytes / 1099511627776d).ToString("0.0") + "T";
            if (bytes >= 1073741824UL)
                return ((double)bytes / 1073741824d).ToString("0.0") + "G";
            if (bytes >= 1048576UL)
                return ((double)bytes / 1048576d).ToString("0.0") + "M";
            if (bytes >= 1024UL)
                return ((double)bytes / 1024d).ToString("0.0") + "K";
            return bytes + "B";
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            string value = path.Replace('\\', '/');
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
    }
}
