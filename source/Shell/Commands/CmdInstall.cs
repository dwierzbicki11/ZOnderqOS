using System;
using System.Collections.Generic;
using ZonderqOS.Storage;

namespace ZonderqOS.Commands
{
    /// <summary>
    /// CLI companion to the graphical ZonderqOS Installer ('zinstall').
    /// Supports listing detected physical disks and executing a verified installation
    /// when explicit disk confirmation is supplied.
    /// </summary>
    public sealed class CmdInstall : ICommand
    {
        public string Name => "zinstall";
        public string Description => "Inspect disks or install ZonderqOS (zinstall list | zinstall run --disk DISK-X --confirm DISK-X --user <name> --host <host> --pass <password>)";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = false;

            if (args == null || args.Length < 2 ||
                string.Equals(args[1], "list", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(args[1], "status", StringComparison.OrdinalIgnoreCase))
            {
                PrintDisks();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (string.Equals(args[1], "run", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteInstallCommand(args);
                return;
            }

            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  zinstall list");
            CommandIO.WriteLine("  zinstall run --disk DISK-X --confirm DISK-X --user <username> --host <hostname> --pass <password> [--single | --dualboot [--part-lba <lba>]]");
        }

        private static void PrintDisks()
        {
            CommandIO.WriteLine("=== ZonderqOS Installer - Physical Disk Inventory ===");
            CommandIO.WriteLine("Firmware Mode: " + LimineBootMediaDetector.FirmwareModeName +
                                " | Live RAM Session: " + (Disk.IsLiveSession ? "Yes" : "No"));

            List<DiskCatalogEntry> disks = DiskCatalog.DiscoverPhysicalDisks();
            if (disks.Count == 0)
            {
                CommandIO.WriteLine("No physical block devices detected.");
                return;
            }

            for (int i = 0; i < disks.Count; i++)
            {
                DiskCatalogEntry d = disks[i];
                CommandIO.WriteLine(
                    "[" + d.DiskId + "] Model: " + d.Model +
                    " | Size: " + d.FormattedCapacity +
                    " | Bus: " + d.InterfaceType +
                    " | Dev: " + d.DeviceName);
                CommandIO.WriteLine(
                    "       Table: " + d.PartitionTableType +
                    " | Partitions: " + d.PartitionsSummary +
                    " | FS: " + d.FilesystemsSummary);
                CommandIO.WriteLine(
                    "       Existing Data: " + (d.HasExistingData ? "Yes" : "No") +
                    " | Install Media: " + (d.IsInstallationMedia ? "Yes" : "No") +
                    " | Can Install: " + (d.CanInstall ? "YES" : ("NO (" + d.BlockReason + ")")));
            }
        }

        private static void ExecuteInstallCommand(string[] args)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                WriteMessage.WriteError("Root privileges are required to install ZonderqOS.", "INSTALL");
                return;
            }

            string diskId = null;
            string confirmId = null;
            string username = null;
            string hostname = "zonderq-pc";
            string password = null;
            bool singlePartition = false;
            bool dualBoot = false;
            ulong dualBootPartLba = 0UL;
            bool simulateError = false;

            for (int i = 2; i < args.Length; i++)
            {
                string flag = args[i];
                if (string.Equals(flag, "--disk", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    diskId = args[++i];
                else if (string.Equals(flag, "--confirm", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    confirmId = args[++i];
                else if (string.Equals(flag, "--user", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    username = args[++i];
                else if (string.Equals(flag, "--host", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    hostname = args[++i];
                else if (string.Equals(flag, "--pass", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    password = args[++i];
                else if (string.Equals(flag, "--single", StringComparison.OrdinalIgnoreCase))
                    singlePartition = true;
                else if (string.Equals(flag, "--dualboot", StringComparison.OrdinalIgnoreCase))
                    dualBoot = true;
                else if (string.Equals(flag, "--part-lba", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    ulong.TryParse(args[++i], out dualBootPartLba);
                else if (string.Equals(flag, "--simulate-write-error", StringComparison.OrdinalIgnoreCase))
                    simulateError = true;
            }

            if (string.IsNullOrWhiteSpace(diskId) || string.IsNullOrWhiteSpace(confirmId) ||
                string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
            {
                WriteMessage.WriteError("Missing required flags: --disk, --confirm, --user, --pass", "INSTALL");
                return;
            }

            List<DiskCatalogEntry> disks = DiskCatalog.DiscoverPhysicalDisks();
            DiskCatalogEntry target = null;
            for (int i = 0; i < disks.Count; i++)
            {
                if (string.Equals(disks[i].DiskId, diskId.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    target = disks[i];
                    break;
                }
            }

            if (target == null)
            {
                WriteMessage.WriteError("Target disk '" + diskId + "' was not found.", "INSTALL");
                return;
            }

            if (!target.CanInstall)
            {
                WriteMessage.WriteError("Cannot install on " + target.DiskId + ": " + target.BlockReason, "INSTALL");
                return;
            }

            char[] passBuf = password.ToCharArray();
            char[] confBuf = password.ToCharArray();
            password = null;

            var req = new InstallationRequest
            {
                TargetDiskToken = target.IdentityToken,
                TypedDiskConfirmation = confirmId,
                PartitionLayout = dualBoot
                    ? InstallationPartitionLayout.DualBootAlongsideExistingOs
                    : (singlePartition
                        ? InstallationPartitionLayout.SingleSystemPartition
                        : InstallationPartitionLayout.DualEfiAndSystem),
                DualBootTargetPartitionStartLba = dualBoot ? dualBootPartLba : 0UL,
                Username = username,
                Hostname = hostname,
                PasswordBuffer = passBuf,
                PasswordLength = passBuf.Length,
                ConfirmPasswordBuffer = confBuf,
                ConfirmPasswordLength = confBuf.Length,
                SimulateInterruptedCopyError = simulateError
            };

            var tracker = new InstallerStepTracker();
            int lastStep = -1;
            bool ok = SystemInstallerEngine.ExecuteInstallation(
                req,
                tracker,
                () =>
                {
                    if (tracker.CurrentStepIndex != lastStep)
                    {
                        lastStep = tracker.CurrentStepIndex;
                        CommandIO.WriteLine("[INSTALL " + tracker.ProgressPercent + "%] " + tracker.CurrentStepName);
                    }
                },
                out string error);

            req.ClearSensitiveData();

            if (ok && tracker.IsCompleted && !tracker.HasFailed)
            {
                WriteMessage.WriteOK("ZonderqOS has been successfully installed.", "INSTALL");
                CommandIO.LastCommandSuccess = true;
            }
            else
            {
                WriteMessage.WriteError("Installation failed: " + (error ?? tracker.ErrorMessage), "INSTALL");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
