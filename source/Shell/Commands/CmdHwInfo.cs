using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdHwInfo : ICommand
    {
        public string Name => "hwinfo";
        public string Description => "Display CPU, memory and scheduler hardware information";

        public void Execute(string[] args, ref string currentPath)
        {
            string section = args.Length > 1 ? args[1].ToLowerInvariant() : "all";
            if (section == "-h" || section == "--help" || section == "help")
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (section != "all" && section != "cpu" && section != "memory" && section != "scheduler" && section != "storage" && section != "pci")
            {
                CommandIO.WriteLine("hwinfo: unknown section '" + section + "'");
                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            HardwareSnapshot info = HardwareSnapshot.Capture();

            CommandIO.WriteLine("=== ZonderqOS Hardware Inspector ===");
            if (section == "all" || section == "cpu")
                PrintCpu(info);
            if (section == "all" || section == "memory")
                PrintMemory(info);
            if (section == "all" || section == "scheduler")
                PrintScheduler(info);
            if (section == "all" || section == "storage")
                PrintStorage(info);
            if (section == "all" || section == "pci")
                PrintPci(info);

            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintCpu(HardwareSnapshot info)
        {
            CommandIO.WriteLine("[CPU]");
            CommandIO.WriteLine("  Architecture:     " + info.Architecture);
            CommandIO.WriteLine("  Vendor:           " + info.CpuVendor);
            CommandIO.WriteLine("  Model:            " + info.CpuBrand);
            CommandIO.WriteLine("  Cores/Logical:    " + info.PhysicalCores + "/" + info.LogicalProcessors);
            CommandIO.WriteLine("  Threads/Core:     " + info.ThreadsPerCore);

            if (info.BaseMHz > 0 || info.MaxMHz > 0 || info.BusMHz > 0)
                CommandIO.WriteLine("  Clock MHz:        base=" + info.BaseMHz + " max=" + info.MaxMHz + " bus=" + info.BusMHz);
            else
                CommandIO.WriteLine("  Clock MHz:        unavailable");

            CommandIO.WriteLine("  Cache L1/L2/L3:   " +
                FormatBytes(info.L1Bytes) + " / " +
                FormatBytes(info.L2Bytes) + " / " +
                FormatBytes(info.L3Bytes));
            CommandIO.WriteLine("  Hypervisor:       " + YesNo(info.HypervisorPresent));
            CommandIO.WriteLine("  Virtualization:   " + YesNo(info.VirtualizationSupported));
            CommandIO.WriteLine("  Features:         " + info.CpuFeatures);
        }

        private static void PrintMemory(HardwareSnapshot info)
        {
            CommandIO.WriteLine("[MEMORY]");
            CommandIO.WriteLine("  Total:            " + FormatBytes(info.TotalMemoryBytes));
            CommandIO.WriteLine("  Used:             " + FormatBytes(info.UsedMemoryBytes));
            CommandIO.WriteLine("  Free:             " + FormatBytes(info.FreeMemoryBytes));

            ulong percent = info.TotalMemoryBytes == 0
                ? 0
                : Math.Min(100UL, info.UsedMemoryBytes * 100UL / info.TotalMemoryBytes);
            CommandIO.WriteLine("  Used percent:     " + percent + "%");
        }

        private static void PrintScheduler(HardwareSnapshot info)
        {
            CommandIO.WriteLine("[SCHEDULER]");
            CommandIO.WriteLine("  Name:             " + info.SchedulerName);
            CommandIO.WriteLine("  Online CPUs:      " + info.OnlineCpuCount);
            CommandIO.WriteLine("  Thread slots:     " + info.SchedulerThreadSlots);

            if (info.OnlineCpuCount > 0 && info.LogicalProcessors > 0 &&
                info.OnlineCpuCount < (uint)info.LogicalProcessors)
            {
                CommandIO.WriteLine("  Note:             not all discovered logical CPUs are scheduler-online");
            }
        }

        private static void PrintStorage(HardwareSnapshot info)
        {
            CommandIO.WriteLine("[STORAGE]");
            CommandIO.WriteLine("  Block devices:    " + info.StorageDeviceCount);
            CommandIO.WriteLine("  Partitions:       " + info.StoragePartitionCount);
            CommandIO.WriteLine("  AHCI controllers: " + info.AhciControllerCount);
            CommandIO.WriteLine("  NVMe controllers: " + info.NvmeControllerCount);
        }

        private static void PrintPci(HardwareSnapshot info)
        {
            CommandIO.WriteLine("[PCI]");
            CommandIO.WriteLine("  Functions:        " + info.PciDeviceCount);
            CommandIO.WriteLine("  Details:          run 'lspci'");
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage: hwinfo [all|cpu|memory|scheduler|storage|pci]");
        }

        private static string YesNo(bool value) => value ? "yes" : "no";

        private static string FormatBytes(ulong bytes)
        {
            const ulong KiB = 1024UL;
            const ulong MiB = 1024UL * KiB;
            const ulong GiB = 1024UL * MiB;

            if (bytes >= GiB)
                return FormatFixed(bytes, GiB, " GiB");
            if (bytes >= MiB)
                return FormatFixed(bytes, MiB, " MiB");
            if (bytes >= KiB)
                return FormatFixed(bytes, KiB, " KiB");
            return bytes + " B";
        }

        private static string FormatFixed(ulong bytes, ulong unit, string suffix)
        {
            ulong whole = bytes / unit;
            ulong tenth = (bytes % unit) * 10UL / unit;
            return whole + "." + tenth + suffix;
        }
    }
}
