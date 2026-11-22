using System;
using System.Collections.Generic;
using ZonderqOS.Storage;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.Commands
{
    public class CmdDualBoot : ICommand
    {
        public string Name => "dualboot";
        public string Description => "Inspect, scan, and configure Dual-Boot chainloader entries (Kali Linux, Windows, UEFI/BIOS).";

        private static bool RequireRoot(string action)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                WriteMessage.WriteError(action + ": permission denied (root required).", "DUALBOOT");
                return false;
            }
            return true;
        }

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = true;
            int startIdx = (args != null && args.Length > 0 && string.Equals(args[0], Name, StringComparison.OrdinalIgnoreCase)) ? 1 : 0;
            string sub = (args != null && args.Length > startIdx) ? args[startIdx].ToLowerInvariant() : "status";

            if (sub == "status" || sub == "list")
            {
                PrintStatus();
                return;
            }

            if (sub == "disks" || sub == "inspect")
            {
                PrintDiskInspection();
                return;
            }

            if (sub == "scan")
            {
                if (!RequireRoot("dualboot scan"))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("[dualboot] Scanning EFI System Partitions and physical disks for foreign operating systems...");
                List<DualBootEntry> discovered = DualBootManager.DiscoverAllDualBootEntries();
                List<DualBootEntry> existing = DualBootManager.LoadSavedEntries("");
                for (int i = 0; i < discovered.Count; i++)
                {
                    DualBootManager.AddUniqueEntry(existing, discovered[i]);
                }

                if (DualBootManager.SaveEntries("", existing, out string saveErr))
                {
                    try { BootSlotManager.SyncLimineConfig(); } catch { }
                    CommandIO.WriteLine("[dualboot] Saved " + existing.Count + " Dual-Boot entry/entries to /boot/dualboot_entries.cfg and updated /boot/limine.conf.");
                }
                else
                {
                    CommandIO.WriteLine("[dualboot] Discovered " + existing.Count + " entry/entries (" + saveErr + ").");
                }

                PrintEntriesList(existing);
                return;
            }

            if (sub == "add")
            {
                if (!RequireRoot("dualboot add"))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (args == null || args.Length < startIdx + 4)
                {
                    WriteMessage.WriteError("Usage: dualboot add <efi|bios> <title> <efi_path_or_bios_drive> [partition_index]", "DUALBOOT");
                    CommandIO.WriteLine("Example: dualboot add efi \"Kali Linux\" /EFI/kali/grubx64.efi");
                    CommandIO.WriteLine("Example: dualboot add efi \"Windows Boot Manager\" /EFI/Microsoft/Boot/bootmgfw.efi");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string proto = args[startIdx + 1].ToLowerInvariant();
                if (proto != "efi" && proto != "bios")
                {
                    WriteMessage.WriteError("dualboot add: protocol must be 'efi' or 'bios'.", "DUALBOOT");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string title = args[startIdx + 2];
                string pathOrDrive = args[startIdx + 3];
                int partIdx = 0;
                if (args.Length >= startIdx + 5)
                    int.TryParse(args[startIdx + 4], out partIdx);

                List<DualBootEntry> entries = DualBootManager.LoadSavedEntries("");
                DualBootManager.AddUniqueEntry(entries, new DualBootEntry
                {
                    Protocol = proto,
                    Title = title,
                    PathOrDrive = pathOrDrive,
                    PartitionIndex = partIdx,
                    Comment = "Added via dualboot CLI"
                });

                if (DualBootManager.SaveEntries("", entries, out string err))
                {
                    try { BootSlotManager.SyncLimineConfig(); } catch { }
                    CommandIO.WriteLine("[dualboot] Added entry '" + title + "' (" + proto + " -> " + pathOrDrive + ") and synced /boot/limine.conf.");
                }
                else
                {
                    WriteMessage.WriteError("Failed to save entry: " + err, "DUALBOOT");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (sub == "remove" || sub == "rm")
            {
                if (!RequireRoot("dualboot remove"))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (args == null || args.Length < startIdx + 2 || !int.TryParse(args[startIdx + 1], out int index1Based) || index1Based < 1)
                {
                    WriteMessage.WriteError("Usage: dualboot remove <1-based-index>", "DUALBOOT");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                List<DualBootEntry> entries = DualBootManager.LoadSavedEntries("");
                if (index1Based > entries.Count)
                {
                    WriteMessage.WriteError("dualboot remove: index " + index1Based + " is out of range (1.." + entries.Count + ").", "DUALBOOT");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                DualBootEntry removed = entries[index1Based - 1];
                entries.RemoveAt(index1Based - 1);
                if (DualBootManager.SaveEntries("", entries, out string err))
                {
                    try { BootSlotManager.SyncLimineConfig(); } catch { }
                    CommandIO.WriteLine("[dualboot] Removed entry #" + index1Based + " ('" + removed.Title + "') and synced /boot/limine.conf.");
                }
                else
                {
                    WriteMessage.WriteError("Failed to update entries: " + err, "DUALBOOT");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            CommandIO.WriteLine("Usage: dualboot <status|disks|scan|add|remove>");
            CommandIO.WriteLine("  dualboot status                              Show configured Dual-Boot menu entries");
            CommandIO.WriteLine("  dualboot disks                               Inspect connected disks for Dual-Boot free space & OS partitions");
            CommandIO.WriteLine("  dualboot scan                                Scan ESP & disks for Kali, Windows, Ubuntu, Debian and update Limine");
            CommandIO.WriteLine("  dualboot add <efi|bios> <title> <path/drive> Add a custom chainload entry to Limine");
            CommandIO.WriteLine("  dualboot remove <index>                      Remove a configured Dual-Boot entry by 1-based index");
        }

        private static void PrintStatus()
        {
            CommandIO.WriteLine("=== ZonderqOS Dual-Boot Manager ===");
            List<DualBootEntry> saved = DualBootManager.LoadSavedEntries("");
            PrintEntriesList(saved);
            PrintDiskInspection();
        }

        private static void PrintEntriesList(List<DualBootEntry> entries)
        {
            if (entries == null || entries.Count == 0)
            {
                CommandIO.WriteLine("Configured Dual-Boot entries: (none)");
                CommandIO.WriteLine("Tip: Run 'dualboot scan' to auto-detect Kali Linux / Windows / UEFI bootloaders, or install in Dual-Boot mode.");
                return;
            }

            CommandIO.WriteLine("Configured Dual-Boot entries (" + entries.Count + "):");
            for (int i = 0; i < entries.Count; i++)
            {
                DualBootEntry e = entries[i];
                string target = e.Protocol == "bios"
                    ? ("BIOS drive " + e.PathOrDrive + (e.PartitionIndex > 0 ? " part " + e.PartitionIndex : ""))
                    : ("UEFI " + e.PathOrDrive);
                CommandIO.WriteLine("  [" + (i + 1) + "] " + e.Title + " -> " + target);
            }
        }

        private static void PrintDiskInspection()
        {
            List<DiskCatalogEntry> disks = DiskCatalog.DiscoverPhysicalDisks();
            CommandIO.WriteLine("--- Disk Dual-Boot Readiness ---");
            for (int i = 0; i < disks.Count; i++)
            {
                DiskCatalogEntry d = disks[i];
                if (d == null || d.IsReadOnly)
                    continue;

                DualBootDiskInspection info = DualBootManager.InspectDiskForDualBoot(d);
                CommandIO.WriteLine("  " + d.DiskId + " (" + d.Model + ", " + d.FormattedCapacity + ", " + d.PartitionTableType + "):");
                CommandIO.WriteLine("    Detected Foreign OS : " + info.DetectedOsSummary);
                CommandIO.WriteLine("    Existing ESP        : " + (info.HasExistingEfiPartition ? ("YES (LBA " + info.ExistingEfiStartLba + ")") : "NO"));
                CommandIO.WriteLine("    Unallocated Space   : " + info.FreeSpaceMegabytes + " MB (" + (info.CanAllocateFromFreeSpace ? "READY for Dual-Boot" : "insufficient/no free slot") + ")");
                CommandIO.WriteLine("    Candidate Partitions: " + info.CandidateTargetPartitions.Count + " eligible existing partition(s)");
                CommandIO.WriteLine("    Assessment          : " + info.DiagnosticNote);
            }
        }
    }
}
