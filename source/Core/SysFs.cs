using System;
using System.Collections.Generic;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.Hardware;
using ZonderqOS.Platform;

namespace ZonderqOS.SystemCore
{
    public static class SysFs
    {
        private static List<DeviceDescriptor> pciDevices;
        private static bool pciDiscoveryAttempted;

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

            if (normalized == "/sys/kernel/hostname")
            {
                string hostname = global::ZonderqOS.EnvironmentManager.Get("HOSTNAME");
                content = (string.IsNullOrWhiteSpace(hostname) ? "ZonderqOS" : hostname) + "\n";
                return true;
            }

            if (normalized == "/sys/kernel/uptime_seconds")
            {
                ulong seconds = BootTelemetry.UptimeSeconds <= 0d
                    ? 0UL
                    : (ulong)BootTelemetry.UptimeSeconds;
                content = seconds.ToString() + "\n";
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

            if (normalized == "/sys/devices/system/cpu/accounting_available")
            {
                content = (SchedulerTelemetry.HasCpuAccounting ? "1" : "0") + "\n";
                return true;
            }

            if (normalized.StartsWith("/sys/devices/system/cpu/topology/", StringComparison.Ordinal))
            {
                HardwareSnapshot hardware = HardwareSnapshot.Capture();
                string leaf = normalized.Substring("/sys/devices/system/cpu/topology/".Length);

                if (leaf == "physical_cores")
                    content = hardware.PhysicalCores.ToString() + "\n";
                else if (leaf == "logical_processors")
                    content = hardware.LogicalProcessors.ToString() + "\n";
                else if (leaf == "threads_per_core")
                    content = hardware.ThreadsPerCore.ToString() + "\n";
                else
                    return false;

                return true;
            }

            if (normalized.StartsWith("/sys/devices/system/cpu/identity/", StringComparison.Ordinal))
            {
                HardwareSnapshot hardware = HardwareSnapshot.Capture();
                string leaf = normalized.Substring("/sys/devices/system/cpu/identity/".Length);

                if (leaf == "vendor")
                    content = hardware.CpuVendor + "\n";
                else if (leaf == "brand")
                    content = hardware.CpuBrand + "\n";
                else if (leaf == "features")
                    content = hardware.CpuFeatures + "\n";
                else if (leaf == "base_mhz")
                    content = hardware.BaseMHz.ToString() + "\n";
                else if (leaf == "max_mhz")
                    content = hardware.MaxMHz.ToString() + "\n";
                else if (leaf == "hypervisor_present")
                    content = (hardware.HypervisorPresent ? "1" : "0") + "\n";
                else if (leaf == "virtualization_supported")
                    content = (hardware.VirtualizationSupported ? "1" : "0") + "\n";
                else
                    return false;

                return true;
            }

            if (normalized.StartsWith("/sys/devices/system/cpu/cache/", StringComparison.Ordinal))
            {
                HardwareSnapshot hardware = HardwareSnapshot.Capture();
                string leaf = normalized.Substring("/sys/devices/system/cpu/cache/".Length);

                if (leaf == "l1_bytes")
                    content = hardware.L1Bytes.ToString() + "\n";
                else if (leaf == "l2_bytes")
                    content = hardware.L2Bytes.ToString() + "\n";
                else if (leaf == "l3_bytes")
                    content = hardware.L3Bytes.ToString() + "\n";
                else
                    return false;

                return true;
            }

            if (normalized == "/sys/devices/system/memory/page_size")
            {
                content = MemoryInfo.PageSizeBytes.ToString() + "\n";
                return true;
            }

            if (normalized == "/sys/devices/system/memory/total_pages")
            {
                content = MemoryInfo.TotalPages.ToString() + "\n";
                return true;
            }

            if (normalized == "/sys/devices/system/memory/free_pages")
            {
                ulong totalPages = MemoryInfo.TotalPages;
                ulong freePages = MemoryInfo.FreePages;
                if (freePages > totalPages)
                    freePages = totalPages;

                content = freePages.ToString() + "\n";
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

                if (cpuLeaf == "busy_ticks")
                {
                    if (!SchedulerTelemetry.HasCpuAccounting)
                    {
                        content = "N/A\n";
                        return true;
                    }

                    long busy = SchedulerTelemetry.CpuBusyTicks((uint)cpuIndex);
                    content = (busy < 0 ? 0 : busy).ToString() + "\n";
                    return true;
                }

                return false;
            }


            if (TryParsePciDevicePath(normalized, out string pciAddress, out string pciLeaf))
            {
                DeviceDescriptor device = FindPciDevice(pciAddress);
                if (device == null)
                    return false;

                if (pciLeaf == "vendor")
                    content = "0x" + device.VendorId.ToString("X4") + "\n";
                else if (pciLeaf == "device")
                    content = "0x" + device.DeviceId.ToString("X4") + "\n";
                else if (pciLeaf == "class")
                    content = "0x" + device.ClassCode.ToString("X2") + "\n";
                else if (pciLeaf == "subclass")
                    content = "0x" + device.Subclass.ToString("X2") + "\n";
                else if (pciLeaf == "programming_interface")
                    content = "0x" + device.ProgrammingInterface.ToString("X2") + "\n";
                else if (pciLeaf == "modalias")
                    content = BuildPciModalias(device) + "\n";
                else
                    return false;

                return true;
            }

            if (TryParseBlockPath(normalized, out bool partition, out int index, out string blockLeaf))
            {
                try
                {
                    ulong blocks;
                    ulong blockSize;

                    if (partition)
                    {
                        var partitions = StorageManager.Partitions;
                        if (partitions == null || index < 0 || index >= partitions.Count)
                            return false;

                        var part = partitions[index];
                        blocks = part.BlockCount;
                        blockSize = (ulong)part.BlockSize;
                    }
                    else
                    {
                        if (index < 0 || index >= StorageManager.DeviceCount)
                            return false;

                        var device = StorageManager.GetDevice(index);
                        blocks = device.BlockCount;
                        blockSize = (ulong)device.BlockSize;
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

                    if (!partition && blockLeaf == "name")
                    {
                        var device = StorageManager.GetDevice(index);
                        content = (device?.Name ?? ("disk" + index)) + "\n";
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
                entries = new[] { "kernel", "devices", "class", "bus" };
                return true;
            }

            if (normalized == "/sys/kernel")
            {
                entries = new[] { "ostype", "osrelease", "architecture", "scheduler", "hostname", "uptime_seconds" };
                return true;
            }

            if (normalized == "/sys/devices")
            {
                entries = new[] { "system" };
                return true;
            }

            if (normalized == "/sys/devices/system")
            {
                entries = new[] { "cpu", "memory" };
                return true;
            }

            if (normalized == "/sys/devices/system/memory")
            {
                entries = new[] { "page_size", "total_pages", "free_pages" };
                return true;
            }

            if (normalized == "/sys/devices/system/cpu")
            {
                int cpuCount = checked((int)SchedulerInfo.CpuCount);
                entries = new string[7 + cpuCount];
                entries[0] = "logical_count";
                entries[1] = "online";
                entries[2] = "scheduler";
                entries[3] = "accounting_available";
                entries[4] = "topology";
                entries[5] = "identity";
                entries[6] = "cache";
                for (int i = 0; i < cpuCount; i++)
                    entries[7 + i] = "cpu" + i;
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/topology")
            {
                entries = new[] { "physical_cores", "logical_processors", "threads_per_core" };
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/identity")
            {
                entries = new[] {
                    "vendor", "brand", "features", "base_mhz", "max_mhz",
                    "hypervisor_present", "virtualization_supported"
                };
                return true;
            }

            if (normalized == "/sys/devices/system/cpu/cache")
            {
                entries = new[] { "l1_bytes", "l2_bytes", "l3_bytes" };
                return true;
            }

            if (TryParseCpuDirectory(normalized, out int cpuIndex))
            {
                if (cpuIndex < 0 || (uint)cpuIndex >= SchedulerInfo.CpuCount)
                    return false;

                entries = new[] { "online", "index", "busy_ticks" };
                return true;
            }


            if (normalized == "/sys/bus")
            {
                entries = GetPciDevices().Count > 0 ? new[] { "pci" } : new string[0];
                return true;
            }

            if (normalized == "/sys/bus/pci")
            {
                if (GetPciDevices().Count == 0)
                    return false;

                entries = new[] { "devices" };
                return true;
            }

            if (normalized == "/sys/bus/pci/devices")
            {
                List<DeviceDescriptor> devices = GetPciDevices();
                if (devices.Count == 0)
                    return false;

                entries = new string[devices.Count];
                for (int i = 0; i < devices.Count; i++)
                    entries[i] = devices[i].Id.Address;
                return true;
            }

            if (TryParsePciDeviceDirectory(normalized, out string pciAddress))
            {
                if (FindPciDevice(pciAddress) == null)
                    return false;

                entries = new[] { "vendor", "device", "class", "subclass", "programming_interface", "modalias" };
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

                    entries = partition
                        ? new[] { "blocks", "block_size", "size_bytes" }
                        : new[] { "name", "blocks", "block_size", "size_bytes" };
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }


        private static List<DeviceDescriptor> GetPciDevices()
        {
            if (pciDiscoveryAttempted)
                return pciDevices ?? new List<DeviceDescriptor>();

            pciDiscoveryAttempted = true;
            List<DeviceDescriptor> discovered;
            if (PciSysfsProvider.TryDiscover(out discovered) && discovered != null)
                pciDevices = discovered;
            else
                pciDevices = new List<DeviceDescriptor>();

            return pciDevices;
        }

        private static DeviceDescriptor FindPciDevice(string address)
        {
            if (string.IsNullOrEmpty(address))
                return null;

            List<DeviceDescriptor> devices = GetPciDevices();
            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor device = devices[i];
                if (device != null && string.Equals(device.Id.Address, address, StringComparison.OrdinalIgnoreCase))
                    return device;
            }

            return null;
        }

        private static bool TryParsePciDeviceDirectory(string path, out string address)
        {
            address = string.Empty;
            const string prefix = "/sys/bus/pci/devices/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            if (tail.Length == 0 || tail.IndexOf('/') >= 0)
                return false;

            address = tail;
            return true;
        }

        private static bool TryParsePciDevicePath(string path, out string address, out string leaf)
        {
            address = string.Empty;
            leaf = string.Empty;
            const string prefix = "/sys/bus/pci/devices/";
            if (!path.StartsWith(prefix, StringComparison.Ordinal))
                return false;

            string tail = path.Substring(prefix.Length);
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash >= tail.Length - 1)
                return false;

            address = tail.Substring(0, slash);
            leaf = tail.Substring(slash + 1);
            return leaf.IndexOf('/') < 0;
        }

        private static string BuildPciModalias(DeviceDescriptor device)
        {
            return "pci:v0000" + device.VendorId.ToString("X4") +
                   "d0000" + device.DeviceId.ToString("X4") +
                   "sv*sd*bc" + device.ClassCode.ToString("X2") +
                   "sc" + device.Subclass.ToString("X2") +
                   "i" + device.ProgrammingInterface.ToString("X2");
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
