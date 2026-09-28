using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdSysInfo : ICommand
    {
        public string Name => "sysinfo";
        public string Description => "Display kernel and system summary";

        public void Execute(string[] args, ref string currentPath)
        {
            HardwareSnapshot info = HardwareSnapshot.Capture();

            CommandIO.WriteLine("=== ZonderqOS System Information ===");
            CommandIO.WriteLine("  OS Name:         ZonderqOS Gen 3");
            CommandIO.WriteLine("  Architecture:    " + info.Architecture);
            CommandIO.WriteLine("  CPU:             " + info.CpuBrand);
            CommandIO.WriteLine("  CPU topology:    " + info.PhysicalCores + "C / " +
                                info.LogicalProcessors + "T discovered");
            CommandIO.WriteLine("  Online CPUs:     " + info.OnlineCpuCount);
            CommandIO.WriteLine("  Scheduler:       " + info.SchedulerName);
            CommandIO.WriteLine("  Memory:          " + FormatMiB(info.UsedMemoryBytes) + " / " +
                                FormatMiB(info.TotalMemoryBytes) + " used");
            CommandIO.WriteLine("  Storage:         " + info.StorageDeviceCount + " device(s), " +
                                info.StoragePartitionCount + " partition(s)");
            CommandIO.WriteLine("  File System:     VFS over FAT32 / HAL Block Devices");
            CommandIO.WriteLine("  Current Root:    " + currentPath);

            if (info.OnlineCpuCount > 0 &&
                info.OnlineCpuCount < (uint)Math.Max(1, info.LogicalProcessors))
            {
                CommandIO.WriteLine("  SMP state:       partial (" + info.OnlineCpuCount + "/" +
                                    info.LogicalProcessors + " CPUs online)");
            }
            else if (info.OnlineCpuCount > 0)
            {
                CommandIO.WriteLine("  SMP state:       online");
            }
            else
            {
                CommandIO.WriteLine("  SMP state:       unavailable");
            }

            CommandIO.LastCommandSuccess = true;
        }

        private static string FormatMiB(ulong bytes)
        {
            const ulong MiB = 1024UL * 1024UL;
            ulong whole = bytes / MiB;
            ulong tenth = (bytes % MiB) * 10UL / MiB;
            return whole + "." + tenth + " MiB";
        }
    }
}
