using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.SystemCore
{
    public static class SysFs
    {
        public static bool IsSysPath(string path)
        {
            string normalized = Normalize(path);
            return normalized == "/sys" || normalized.StartsWith("/sys/", StringComparison.Ordinal);
        }

        public static bool TryRead(string path, out string content)
        {
            string normalized = Normalize(path);
            content = null;

            if (normalized == "/sys/kernel/ostype")
            {
                content = "ZOnderqOS\n";
                return true;
            }

            if (normalized == "/sys/kernel/osrelease")
            {
                content = "Gen3\n";
                return true;
            }

            if (normalized == "/sys/kernel/scheduler")
            {
                content = (SchedulerInfo.SchedulerName ?? "N/A") + "\n";
                return true;
            }

            if (normalized == "/sys/kernel/architecture")
            {
                content = HardwareSnapshot.Capture().Architecture + "\n";
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/logical_count")
            {
                content = SchedulerInfo.CpuCount.ToString() + "\n";
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/online")
            {
                content = BuildCpuRange(SchedulerInfo.CpuCount) + "\n";
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/scheduler")
            {
                content = (SchedulerInfo.SchedulerName ?? "N/A") + "\n";
                return true;
            }

            if (TryParseCpuPath(normalized, out int cpuIndex, out string cpuLeaf))
            {
                uint count = SchedulerInfo.CpuCount;
                if (cpuIndex < 0 || (uint)cpuIndex >= count)
                    return false;

                if (cpuLeaf == "online")
                {
                    content = "1\n";
                    return true;
                }

                if (cpuLeaf == "index")
                {
                    content = cpuIndex.ToString() + "\n";
                    return true;
                }

                return false;
            }

            if (TryParseBlockPath(normalized, out bool partition, out int index, out string blockLeaf))
            {
                try
                {
                    ulong blocks;
                    uint blockSize;

                    if (partition)
                    {
                        var partitions = StorageManager.Partitions;
                        if (partitions == null || index < 0 || index >= partitions.Count)
                            return false;

                        var part = partitions[index];
                        blocks = part.BlockCount;
                        blockSize = part.BlockSize;
                    }
                    else
                    {
                        if (index < 0 || index >= StorageManager.DeviceCount)
                            return false;

                        var device = StorageManager.GetDevice(index);
                        blocks = device.BlockCount;
                        blockSize = device.BlockSize;
                    }

                    if (blockLeaf == "blocks")
                    {
                        content = blocks.ToString() + "\n";
                        return true;
                    }

                    if (blockLeaf == "block_size")
                    {
                        content = blockSize.ToString() + "\n";
                        return true;
                    }

                    if (blockLeaf == "size_bytes")
                    {
                        content = SaturatingMultiply(blocks, blockSize).ToString() + "\n";
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        public static bool TryList(string path, out string[] entries)
        {
            string normalized = Normalize(path);
            entries = null;

            if (normalized == "/sys")
            {
                entries = new[] { "kernel", "devices", "class" };
                return true;
            }

            if (normalized == "/sys/kernel")
            {
                entries = new[] { "ostype", "osrelease", "architecture", "scheduler" };
                return true;
            }

            if (normalized == "/sys/devices")
            {
                entries = new[] { "system" };
                return true;
            }

            if (normalized == "/sys/devices/system")
            {
                entries = new[] { "cpu" };
                return true;
            }

            if (normalized == "/sys/devices/system/cpu")
            {
                int cpuCount = checked((int)SchedulerInfo.CpuCount);
                entries = new string[3 + cpuCount];
                entries[0] = "logical_count";
                entries[1] = "online";
                entries[2] = "scheduler";
                for (int i = 0; i < cpuCount; i++)
                    entries[3 + i] = "cpu" + i;
                return true;
            }

            if (TryParseCpuDirectory(normalized, out int cpuIndex))
            {
                if (cpuIndex < 0 || (uint)cpuIndex >= SchedulerInfo.CpuCount)
                    return false;

                entries = new[] { "online", "index" };
                return true;
            }

            if (normalized == "/sys/class")
            {
                entries = new[] { "block" };
                return true;
            }

            if (normalized == "/sys/class/block")
            {
                try
                {
                    int devices = Math.Max(0, StorageManager.DeviceCount);
                    int partitions = StorageManager.Partitions == null
                        ? 0
                        : Math.Max(0, StorageManager.Partitions.Count);

                    entries = new string[devices + partitions];
                    int target = 0;
                    for (int i = 0; i < devices; i++)
                        entries[target++] = "disk" + i;
                    for (int i = 0; i < partitions; i++)
                        entries[target++] = "part" + i;
                    return true;
                }
                catch
                {
                    entries = new string[0];
                    return true;
                }
            }

            if (TryParseBlockDirectory(normalized, out bool partition, out int index))
            {
                try
                {
                    if (partition)
                    {
                        var partitions = StorageManager.Partitions;
                        if (partitions == null || index < 0 || index >= partitions.Count)
                            return false;
                    }
                    else if (index < 0 || index >= StorageManager.DeviceCount)
                    {
                        return false;
                    }

                    entries = new[] { "blocks", "block_size", "size_bytes" };
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static string BuildCpuRange(uint count)
        {
            if (count == 0)
                return string.Empty;
            if (count == 1)
                return "0";
            return "0-" + (count - 1);
        }

        private static bool TryParseCpuDirectory(string path, out int cpuIndex)
        {
            cpuIndex = -1;
            const string prefix = "/sys/devices/system/cpu/cpu";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            return tail.Length > 0 && tail.IndexOf('/') < 0 &&
                   int.TryParse(tail, out cpuIndex);
        }

        private static bool TryParseCpuPath(string path, out int cpuIndex, out string leaf)
        {
            cpuIndex = -1;
            leaf = string.Empty;
            const string prefix = "/sys/devices/system/cpu/cpu";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash >= tail.Length - 1)
                return false;

            if (!int.TryParse(tail.Substring(0, slash), out cpuIndex))
                return false;

            leaf = tail.Substring(slash + 1);
            return leaf.IndexOf('/') < 0;
        }

        private static bool TryParseBlockDirectory(string path, out bool partition, out int index)
        {
            partition = false;
            index = -1;
            const string prefix = "/sys/class/block/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            if (tail.Length == 0 || tail.IndexOf('/') >= 0)
                return false;

            if (tail.StartsWith("disk", StringComparison.Ordinal))
                return int.TryParse(tail.Substring(4), out index);

            if (tail.StartsWith("part", StringComparison.Ordinal))
            {
                partition = true;
                return int.TryParse(tail.Substring(4), out index);
            }

            return false;
        }

        private static bool TryParseBlockPath(string path, out bool partition, out int index, out string leaf)
        {
            partition = false;
            index = -1;
            leaf = string.Empty;
            const string prefix = "/sys/class/block/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash >= tail.Length - 1)
                return false;

            string node = tail.Substring(0, slash);
            leaf = tail.Substring(slash + 1);
            if (leaf.IndexOf('/') >= 0)
                return false;

            if (node.StartsWith("disk", StringComparison.Ordinal))
                return int.TryParse(node.Substring(4), out index);

            if (node.StartsWith("part", StringComparison.Ordinal))
            {
                partition = true;
                return int.TryParse(node.Substring(4), out index);
            }

            return false;
        }

        private static ulong SaturatingMultiply(ulong left, ulong right)
        {
            if (left == 0 || right == 0)
                return 0;
            if (left > ulong.MaxValue / right)
                return ulong.MaxValue;
            return left * right;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            string value = path.Replace('\\', '/').Trim();
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
    }
}
