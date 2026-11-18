using System;
using System.Collections.Generic;
using System.IO;
using ZonderqOS.Security;
using ZonderqOS.SystemCore;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.Commands
{
    public sealed class CmdUpdate : ICommand
    {
        public string Name => "zupdate";
        public string Description => "System update manager: check, verify, install and rollback OS & kernel updates";

        public void Execute(string[] args, ref string currentPath)
        {
            UpdateManager.Initialize();
            BootSlotManager.Initialize();

            if (args.Length < 2)
            {
                PrintStatusOverview();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string subcmd = args[1].ToLowerInvariant();

            if (subcmd == "status")
            {
                PrintStatusOverview();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "slots" || subcmd == "bootslots")
            {
                PrintSlotsDiagnostics();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "check")
            {
                bool force = args.Length > 2 && args[2].ToLowerInvariant() == "--force";
                CommandIO.WriteLine("Scanning USB devices and /updates...");

                UpdatePackageInfo pkg;
                string msg;
                bool found = UpdateManager.CheckForUpdates(force, out pkg, out msg);

                if (found && pkg != null)
                {
                    WriteMessage.WriteOK("Verified USB update found!", "UPDATE");
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("  Title:         " + pkg.Title);
                    CommandIO.WriteLine("  Target version: " + pkg.Version + " (Build " + pkg.BuildNumber + ")");
                    CommandIO.WriteLine("  Architecture:  " + pkg.Architecture);
                    CommandIO.WriteLine("  Severity:      " + pkg.Severity);
                    CommandIO.WriteLine("  Release date:  " + pkg.ReleaseDate);
                    CommandIO.WriteLine("  Digital signature: " + (pkg.IsSigned ? (pkg.IsSignatureValid ? "TRUSTED (SHA-256 / Ed25519)" : "INVALID!") : "MISSING"));
                    CommandIO.WriteLine("  Kernel checksum: " + (string.IsNullOrEmpty(pkg.KernelSha256) ? "Not specified" : pkg.KernelSha256));
                    CommandIO.WriteLine("  Kernel file:   " + (string.IsNullOrEmpty(pkg.KernelImagePath) ? "Missing from package" : pkg.KernelImagePath));
                    CommandIO.WriteLine("  File count:    " + (pkg.Files != null ? pkg.Files.Count.ToString() : "0"));
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Changelog:");
                    for (int i = 0; i < pkg.Changelog.Length; i++)
                    {
                        CommandIO.WriteLine("  * " + pkg.Changelog[i]);
                    }
                    CommandIO.WriteLine("");
                    CommandIO.WriteLine("Run 'zupdate verify' to check SHA-256 or 'zupdate install' to install.");
                }
                else
                {
                    WriteMessage.WriteInfo(msg, "UPDATE");
                }
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "verify")
            {
                UpdatePackageInfo pkg;
                if (!UpdateManager.TryLoadDiskManifest(out pkg) || pkg == null)
                {
                    pkg = UpdateManager.AvailablePackage;
                }

                if (pkg == null)
                {
                    WriteMessage.WriteError("No package to verify. Insert a USB drive containing /updates/update.manifest.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Verifying package signature " + pkg.Version + "...");
                string error;
                bool ok = UpdateManager.VerifyPackageIntegrity(pkg, (pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Package integrity and authenticity verified [PASS]", "UPDATE");
                    CommandIO.WriteLine("  Kernel SHA-256: " + pkg.ExpectedSha256);
                    CommandIO.WriteLine("  Digital signature: " + (pkg.IsSigned ? "TRUSTED (SHA-256 / Ed25519)" : "Missing (local package)"));
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Verification failed: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (subcmd == "apply" || subcmd == "install")
            {
                if (!RequireRootPrivilege(subcmd, "Root permissions are required to install updates."))
                    return;

                UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
                if (pkg == null)
                {
                    string chkMsg;
                    UpdateManager.CheckForUpdates(false, out pkg, out chkMsg);
                }

                if (pkg == null)
                {
                    WriteMessage.WriteInfo("No pending USB updates. Run 'zupdate check --force'.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string inactiveSlot = BootSlotManager.GetInactiveSlot();
                CommandIO.WriteLine($"Installing version {pkg.Version} to separate boot slot {inactiveSlot}...");

                string error;
                bool ok = UpdateManager.ApplyUpdate(pkg, (pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK($"Update installed in slot {inactiveSlot}! The new kernel will boot after restart.", "UPDATE");
                    CommandIO.WriteLine("Run 'reboot' to start the updated system.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Update failed: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (subcmd == "rollback")
            {
                if (!RequireRootPrivilege(subcmd, "Root permissions are required for rollback."))
                    return;

                if (!UpdateManager.HasRollbackSnapshot())
                {
                    WriteMessage.WriteError("No recovery snapshot available.", "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("Rolling back to the previous A/B slot...");
                string error;
                bool ok = UpdateManager.Rollback((pct, status) =>
                {
                    CommandIO.WriteLine(string.Format("  [{0,3}%] {1}", pct, status));
                }, out error);

                if (ok)
                {
                    WriteMessage.WriteOK("Previous system state and boot slot restored (" + BootSlotManager.ActiveSlot + "). Restart required.", "UPDATE");
                    CommandIO.WriteLine("Run 'reboot' to start the previous version.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Rollback failed: " + error, "UPDATE");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (subcmd == "history")
            {
                List<string> hist = UpdateManager.GetHistory();
                CommandIO.WriteLine("=== ZOnderqOS Update History ===");
                if (hist.Count == 0)
                {
                    CommandIO.WriteLine("  No entries in /var/log/update.log");
                }
                else
                {
                    for (int i = 0; i < hist.Count; i++)
                    {
                        CommandIO.WriteLine("  " + hist[i]);
                    }
                }
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "drives" || subcmd == "storage")
            {
                StorageEnvironmentInfo env = UpdateManager.GetStorageEnvironmentInfo();
                CommandIO.WriteLine("=== ZOnderqOS Update Environment and Storage ===");
                CommandIO.WriteLine("  System partition:      " + env.PartitionLabel + " (mounted at '/')");
                CommandIO.WriteLine("  Device type:           " + env.DeviceType);
                CommandIO.WriteLine("  Active boot slot:     Slot " + env.ActiveBootSlot);
                CommandIO.WriteLine("  Running kernel:        " + env.RunningKernelVersion + " (Build " + env.RunningKernelBuild + ")");
                CommandIO.WriteLine("  /updates directory:    " + (env.HasUpdatesDir ? "AVAILABLE" : "MISSING"));
                CommandIO.WriteLine("  Manifest file:         " + (env.HasManifest ? "DETECTED (" + env.ManifestPath + ")" : "MISSING"));
                CommandIO.WriteLine("  Payload directory:     " + (env.HasPayload ? "DETECTED (" + env.PayloadPath + ", files: " + env.PayloadFileCount + ")" : "MISSING"));
                CommandIO.WriteLine("  Kernel image:          " + (env.HasKernelBinary ? "DETECTED (" + env.KernelBinaryPath + ")" : "MISSING"));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (subcmd == "stage" || subcmd == "confirm" || subcmd == "clean")
            {
                if (!RequireRootPrivilege(subcmd, "Root permissions are required for update operations."))
                    return;

                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private static bool HasRootPrivilege() => SecurityContext.IsAuthenticated && SecurityContext.CurrentUid == 0;

        private static bool RequireRootPrivilege(string subcmd, string deniedMessage)
        {
            if (HasRootPrivilege())
                return true;

            WriteMessage.WriteError(deniedMessage, "UPDATE");
            SecurityLogger.LogEvent("WARN", $"Unauthorized zupdate '{subcmd}' attempt by {SecurityContext.CurrentUser ?? "?"} (uid={SecurityContext.CurrentUid}, auth={SecurityContext.IsAuthenticated})");
            CommandIO.LastCommandSuccess = false;
            return false;
        }

        private void PrintStatusOverview()
        {
            CommandIO.WriteLine("=== ZonderqOS System Updater & Boot Diagnostics ===");
            CommandIO.WriteLine("  Running kernel:        " + KernelBuildInfo.KernelVersion + " (Build " + KernelBuildInfo.KernelBuild + ")");
            CommandIO.WriteLine("  Architecture:          " + KernelBuildInfo.Architecture + " (" + KernelBuildInfo.CosmosVersion + ")");
            CommandIO.WriteLine("  Build date:            " + KernelBuildInfo.CompileTimestamp);
            CommandIO.WriteLine("  Active slot:           Slot " + BootSlotManager.ActiveSlot + " (" + KernelBuildInfo.RunningSlotPath + ")");
            CommandIO.WriteLine("  Current slot status:" + (KernelBuildInfo.IsHealthyAndConfirmed ? "CONFIRMED (HEALTHY)" : "PENDING CONFIRMATION"));
            CommandIO.WriteLine("  Version in /etc:       " + UpdateManager.CurrentVersion + " (Build " + UpdateManager.CurrentBuild + ")");

            // Detection of mismatch between booted kernel and disk version
            if (!string.Equals(KernelBuildInfo.KernelVersion, UpdateManager.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                CommandIO.WriteLine("");
                WriteMessage.WriteInfo("[WARNING] VERSION MISMATCH: Running kernel (" + KernelBuildInfo.KernelVersion +
                    ") differs from the version on disk (" + UpdateManager.CurrentVersion + ").", "UPDATE");
                CommandIO.WriteLine("  Restart ('reboot') is required to boot the updated slot.");
            }

            if (!string.IsNullOrEmpty(BootSlotManager.PendingSlot))
            {
                WriteMessage.WriteInfo("[WARNING] Pending boot slot: Slot " + BootSlotManager.PendingSlot +
                    " (Boot attempts: " + BootSlotManager.BootAttempts + "/" + BootSlotManager.MaxBootAttempts + ")", "BOOT");
            }

            CommandIO.WriteLine("");
            CommandIO.WriteLine("  Last check:            " + UpdateManager.LastCheckTime);
            CommandIO.WriteLine("  Update source:         " + UpdateManager.SourceMode.ToString().ToUpperInvariant());
            CommandIO.WriteLine("  Restart required:      " + (UpdateManager.IsRebootRequired ? "YES (new kernel pending in slot " + BootSlotManager.ActiveSlot + ")" : "No"));
            CommandIO.WriteLine("  Restore point:         " + (UpdateManager.HasRollbackSnapshot() ? "Available (A/B snapshot)" : "None"));
            CommandIO.WriteLine("");
            PrintHelp();
        }

        private void PrintSlotsDiagnostics()
        {
            CommandIO.WriteLine("=== A/B Boot Slot Diagnostics (Limine) ===");
            CommandIO.WriteLine("  Active slot:    Slot " + BootSlotManager.ActiveSlot);
            CommandIO.WriteLine("  Fallback slot:  Slot " + BootSlotManager.FallbackSlot);
            CommandIO.WriteLine("  Pending slot:   " + (string.IsNullOrEmpty(BootSlotManager.PendingSlot) ? "None (stable)" : "Slot " + BootSlotManager.PendingSlot));
            CommandIO.WriteLine("  Attempt count:  " + BootSlotManager.BootAttempts + " / " + BootSlotManager.MaxBootAttempts);
            CommandIO.WriteLine("");
            CommandIO.WriteLine("SLOT A:");
            CommandIO.WriteLine("  Path:           " + BootSlotManager.SlotAPath);
            CommandIO.WriteLine("  File present:   " + (File.Exists(BootSlotManager.SlotAPath) ? "YES" : "MISSING"));
            CommandIO.WriteLine("  Health status:  " + BootSlotManager.SlotAStatus);
            CommandIO.WriteLine("  Kernel version: " + BootSlotManager.SlotAVersion + " (Build " + BootSlotManager.SlotABuild + ")");
            CommandIO.WriteLine("");
            CommandIO.WriteLine("SLOT B:");
            CommandIO.WriteLine("  Path:           " + BootSlotManager.SlotBPath);
            CommandIO.WriteLine("  File present:   " + (File.Exists(BootSlotManager.SlotBPath) ? "YES" : "MISSING"));
            CommandIO.WriteLine("  Health status:  " + BootSlotManager.SlotBStatus);
            CommandIO.WriteLine("  Kernel version: " + (string.IsNullOrEmpty(BootSlotManager.SlotBVersion) ? "Empty" : BootSlotManager.SlotBVersion + " (Build " + BootSlotManager.SlotBBuild + ")"));
            CommandIO.WriteLine("");
            CommandIO.WriteLine("Limine configuration:");
            CommandIO.WriteLine("  File in /boot:  " + (File.Exists(BootSlotManager.LimineBootConfig) ? "SYNCHRONIZED" : "None"));
            CommandIO.WriteLine("  File in /:     " + (File.Exists(BootSlotManager.LimineRootConfig) ? "SYNCHRONIZED" : "None"));
        }

        private void PrintHelp()
        {
            CommandIO.WriteLine("Available zupdate commands:");
            CommandIO.WriteLine("  zupdate status                 - Show kernel, active slot and version");
            CommandIO.WriteLine("  zupdate slots                  - Diagnose A/B boot slots");
            CommandIO.WriteLine("  zupdate check [--force]        - Check USB update packages");
            CommandIO.WriteLine("  zupdate verify                 - Verify SHA-256 and package signature");
            CommandIO.WriteLine("  zupdate install                - Install to inactive slot safely");
            CommandIO.WriteLine("  zupdate rollback               - Roll back to the previous slot");
            CommandIO.WriteLine("  zupdate history                - Show installed update history");
            CommandIO.WriteLine("  zupdate drives                 - Inspect partitions and update directories");
        }
    }
}
