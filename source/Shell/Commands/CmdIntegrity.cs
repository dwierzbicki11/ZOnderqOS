using System;
using System.Collections.Generic;
using ZonderqOS.Security;

namespace ZonderqOS.Commands
{
    public sealed class CmdIntegrity : ICommand
    {
        public string Name => "integrity";
        public string Description => "Scan and verify system file integrity and anti-tamper baselines";

        public void Execute(string[] args, ref string currentPath)
        {
            IntegrityGuard.Initialize();

            string action = args.Length > 1 ? args[1].ToLowerInvariant() : "check";

            if (action == "baseline" || action == "update")
            {
                if (SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("Access denied. Root privileges required to update integrity baseline.", "SEC");
                    SecurityLogger.LogEvent("WARN", "Unauthorized attempt to re-baseline system integrity.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                IntegrityGuard.UpdateBaseline();
                CommandIO.WriteLine("System integrity baseline updated successfully.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "status")
            {
                IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
                if (modified == 0 && missing == 0 && permIssues == 0)
                {
                    CommandIO.WriteLine("[OK] Host integrity verified: all " + total + " monitored files match baseline.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    CommandIO.WriteLine("[ALERT] Integrity violations detected: " + modified + " modified, " + missing + " missing, " + permIssues + " permission issues.");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            // Default: check / scan
            CommandIO.WriteLine("=== ZOnderqOS File Integrity & Anti-Tamper Scan ===");
            IntegrityGuard.ScanAll();

            List<FileIntegrityRecord> records = IntegrityGuard.GetRecords();
            CommandIO.WriteLine(string.Format("{0,-24} {1,-8} {2,-16} {3}", "FILE", "CHMOD", "STATUS", "SHA-256 (PREFIX)"));
            CommandIO.WriteLine(new string('-', 68));

            int violations = 0;
            for (int i = 0; i < records.Count; i++)
            {
                FileIntegrityRecord r = records[i];
                string statusStr = "OK";
                if (r.Status == IntegrityStatus.Modified) { statusStr = "MODIFIED [!]"; violations++; }
                else if (r.Status == IntegrityStatus.Missing) { statusStr = "MISSING [!]"; violations++; }
                else if (r.Status == IntegrityStatus.PermissionMismatch) { statusStr = "PERM ERR [!]"; violations++; }

                string hashPrefix = !string.IsNullOrEmpty(r.CurrentHash) && r.CurrentHash.Length >= 12
                    ? r.CurrentHash.Substring(0, 12) + "..."
                    : "---";

                CommandIO.WriteLine(string.Format("{0,-24} {1,-8} {2,-16} {3}", r.Path, r.CurrentPerms, statusStr, hashPrefix));
            }

            CommandIO.WriteLine(new string('-', 68));
            if (violations == 0)
            {
                CommandIO.WriteLine("Result: PASS (0 violations detected)");
                CommandIO.LastCommandSuccess = true;
            }
            else
            {
                CommandIO.WriteLine("Result: ALERT (" + violations + " violation(s) detected!)");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
