using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdLscpu : ICommand
    {
        public string Name => "lscpu";
        public string Description => "Show CPU architecture, topology, cache and scheduler information";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: lscpu");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            HardwareSnapshot h = HardwareSnapshot.Capture();
            CommandIO.WriteLine("Architecture:             " + h.Architecture);
            CommandIO.WriteLine("Vendor ID:                " + h.CpuVendor);
            CommandIO.WriteLine("Model name:               " + h.CpuBrand);
            CommandIO.WriteLine("CPU(s):                   " + h.OnlineCpuCount);
            CommandIO.WriteLine("Core(s):                  " + h.PhysicalCores);
            CommandIO.WriteLine("Thread(s) per core:       " + h.ThreadsPerCore);
            CommandIO.WriteLine("Scheduler:                " + h.SchedulerName);
            CommandIO.WriteLine("Scheduler thread slots:   " + h.SchedulerThreadSlots);
            CommandIO.WriteLine("Base MHz:                 " + ValueOrUnknown(h.BaseMHz));
            CommandIO.WriteLine("Max MHz:                  " + ValueOrUnknown(h.MaxMHz));
            CommandIO.WriteLine("L1 cache:                 " + FormatBytes(h.L1Bytes));
            CommandIO.WriteLine("L2 cache:                 " + FormatBytes(h.L2Bytes));
            CommandIO.WriteLine("L3 cache:                 " + FormatBytes(h.L3Bytes));
            CommandIO.WriteLine("Virtualization:           " + (h.VirtualizationSupported ? "supported" : "not reported"));
            CommandIO.WriteLine("Hypervisor:               " + (h.HypervisorPresent ? "present" : "not detected"));
            CommandIO.WriteLine("Flags:                    " + h.CpuFeatures);
            CommandIO.LastCommandSuccess = true;
        }

        private static string ValueOrUnknown(int value) => value > 0 ? value.ToString() : "N/A";

        private static string FormatBytes(ulong bytes)
        {
            if (bytes == 0) return "N/A";
            if (bytes >= 1024UL * 1024UL) return (bytes / (1024UL * 1024UL)) + " MiB";
            if (bytes >= 1024UL) return (bytes / 1024UL) + " KiB";
            return bytes + " B";
        }
    }
}
