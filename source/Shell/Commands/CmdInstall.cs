using System;
using ZonderqOS.SystemCore.Installer;

namespace ZonderqOS.Commands
{
    public sealed class CmdInstall : ICommand
    {
        public string Name => "install";
        public string Description => "Inspect and plan a safe ZOnderqOS installation";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string action = args[1].ToLowerInvariant();
            if (action == "scan")
            {
                Scan();
                return;
            }

            if (action == "plan")
            {
                if (args.Length != 3 || !int.TryParse(args[2], out int diskId))
                {
                    WriteMessage.WriteError("Usage: install plan <disk_id>", "INSTALL");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                PrintPlan(diskId);
                return;
            }

            if (action == "apply")
            {
                string reason;
                if (!SystemInstaller.CanApplyDestructivePlan(out reason))
                {
                    WriteMessage.WriteError(reason, "INSTALL");
                    CommandIO.WriteLine(
                        "Use 'install scan' and 'install plan <disk_id>' for dry-run inspection.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                // This path must remain unreachable until a later stage explicitly
                // implements target identity, payload deployment and bootloader install.
                WriteMessage.WriteError("Installer apply stage is not implemented.", "INSTALL");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (action == "help" || action == "-h" || action == "--help")
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            WriteMessage.WriteError("Unknown install action: " + action, "INSTALL");
            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private static void Scan()
        {
            int count = SystemInstaller.TargetCount;
            CommandIO.WriteLine("=== ZOnderqOS Installer Target Scan ===");
            CommandIO.WriteLine("Targets: " + count);

            if (count == 0)
            {
                CommandIO.WriteLine("No block devices detected.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            for (int i = 0; i < count; i++)
            {
                string name;
                InstallerLayoutPlan plan;
                string error;

                if (!SystemInstaller.TryGetTargetPlan(i, out name, out plan, out error))
                {
                    CommandIO.WriteLine("disk" + i + ": unavailable - " + error);
                    continue;
                }

                CommandIO.WriteLine(
                    "disk" + i +
                    "  " + name +
                    "  size=" + FormatBytes(plan.TotalBytes) +
                    "  block=" + plan.BlockSize +
                    "  layout=" + plan.StatusText());
            }

            string reason;
            SystemInstaller.CanApplyDestructivePlan(out reason);
            CommandIO.WriteLine("");
            CommandIO.WriteLine("APPLY: BLOCKED");
            CommandIO.WriteLine(reason);
            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintPlan(int diskId)
        {
            string name;
            InstallerLayoutPlan plan;
            string error;

            if (!SystemInstaller.TryGetTargetPlan(diskId, out name, out plan, out error))
            {
                WriteMessage.WriteError(error, "INSTALL");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("=== ZOnderqOS Installer Dry Run ===");
            CommandIO.WriteLine("Target: disk" + diskId + " (" + name + ")");
            CommandIO.WriteLine("Disk size: " + FormatBytes(plan.TotalBytes));
            CommandIO.WriteLine("Logical block size: " + plan.BlockSize);
            CommandIO.WriteLine("Layout status: " + plan.StatusText());

            if (plan.IsReady)
            {
                CommandIO.WriteLine("Planned partition table: MBR");
                CommandIO.WriteLine("Partition start LBA: " + InstallerLayoutPlan.StartLba);
                CommandIO.WriteLine("Partition blocks: " + plan.PartitionBlocks);
                CommandIO.WriteLine("Partition size: " + FormatBytes(plan.PartitionBytes));
                CommandIO.WriteLine("Filesystem: FAT32");
                CommandIO.WriteLine("Volume label: ZONDERQ");
                CommandIO.WriteLine("WARNING: applying this plan would erase the target disk.");
            }

            string reason;
            SystemInstaller.CanApplyDestructivePlan(out reason);
            CommandIO.WriteLine("Apply status: BLOCKED");
            CommandIO.WriteLine(reason);
            CommandIO.LastCommandSuccess = plan.IsReady;
        }

        private static string FormatBytes(ulong bytes)
        {
            if (bytes >= 1099511627776UL)
                return ((double)bytes / 1099511627776d).ToString("0.0") + " TiB";
            if (bytes >= 1073741824UL)
                return ((double)bytes / 1073741824d).ToString("0.0") + " GiB";
            if (bytes >= 1048576UL)
                return ((double)bytes / 1048576d).ToString("0.0") + " MiB";
            if (bytes >= 1024UL)
                return ((double)bytes / 1024d).ToString("0.0") + " KiB";
            return bytes + " B";
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  install scan");
            CommandIO.WriteLine("  install plan <disk_id>");
            CommandIO.WriteLine("  install apply <disk_id>   (blocked until target safety is proven)");
        }
    }
}
