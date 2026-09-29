using ZonderqOS;

namespace ZonderqOS.Commands
{
    public sealed class CmdConfig : ICommand
    {
        public string Name => "config";
        public string Description => "Validate or repair persistent system settings";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string action = args[1].ToLowerInvariant();

            if (action == "validate")
            {
                string summary;
                bool ok = SystemSettings.ValidatePersistedFile(out summary);
                if (ok)
                    WriteMessage.WriteOK("Settings valid: " + summary, "CFG");
                else
                    WriteMessage.WriteError("Settings invalid: " + summary, "CFG");

                CommandIO.LastCommandSuccess = ok;
                return;
            }


            if (action == "backup")
            {
                string path = args.Length > 2
                    ? PathResolver.GetAbsolutePath(currentPath, args[2])
                    : PathResolver.GetAbsolutePath(currentPath, "settings-backup.conf");

                string error;
                if (!SystemSettings.BackupTo(path, out error))
                {
                    WriteMessage.WriteError(error, "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK("Settings backup written: " + path, "CFG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "check-backup")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: config check-backup <path>", "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
                string summary;
                bool ok = SystemSettings.ValidateBackupFile(path, out summary);
                if (ok)
                    WriteMessage.WriteOK("Backup valid: " + summary, "CFG");
                else
                    WriteMessage.WriteError("Backup invalid: " + summary, "CFG");

                CommandIO.LastCommandSuccess = ok;
                return;
            }

            if (action == "restore")
            {
                if (args.Length < 3)
                {
                    WriteMessage.WriteError("Usage: config restore <path>", "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
                string error;
                if (!SystemSettings.RestoreFrom(path, out error))
                {
                    WriteMessage.WriteError(error, "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK("Settings restored from: " + path, "CFG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "repair")
            {
                if (!SecurityContext.IsAuthenticated ||
                    SecurityContext.CurrentUid != 0 ||
                    SecurityContext.CurrentUser != "root")
                {
                    WriteMessage.WriteError("config repair requires authenticated root.", "CFG");
                    SecurityLogger.LogEvent("WARN", "Unauthorized settings repair attempt.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string before;
                bool validBefore = SystemSettings.ValidatePersistedFile(out before);

                if (!SystemSettings.RepairPersistedFile())
                {
                    WriteMessage.WriteError("Settings repair failed.", "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string after;
                bool validAfter = SystemSettings.ValidatePersistedFile(out after);
                if (!validAfter)
                {
                    WriteMessage.WriteError("Settings rewrite completed but validation still fails: " + after, "CFG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                SystemLogger.Log(
                    SystemLogLevel.Warning,
                    "CFG",
                    "Persistent settings repaired by authenticated root. Previous state: " +
                    (validBefore ? "valid" : before));

                WriteMessage.WriteOK("Settings repaired: " + after, "CFG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "help" || action == "-h" || action == "--help")
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            WriteMessage.WriteError("Unknown config action: " + action, "CFG");
            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  config validate");
            CommandIO.WriteLine("  config repair   (root only)");
            CommandIO.WriteLine("  config backup [path]");
            CommandIO.WriteLine("  config check-backup <path>");
            CommandIO.WriteLine("  config restore <path>   (root only)");
        }
    }
}
