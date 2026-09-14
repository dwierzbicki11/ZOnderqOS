using System;
using System.IO;
using Cosmos.Kernel.Core.Memory;

namespace ZonderqOS
{
    /// <summary>
    /// Dependency-light recovery console used after a failed boot or by authenticated root.
    /// Automatic boot recovery is intentionally read-only to avoid turning a boot failure
    /// into an unauthenticated administrative shell.
    /// </summary>
    public static class RecoveryMode
    {
        private const int MaxListEntries = 80;
        private const int MaxViewLines = 80;
        private const int MaxLogLines = 32;
        private const string SettingsPath = "/etc/zonderq/settings.conf";

        public static void Run(string reason, Exception cause, bool authenticatedAdmin)
        {
            string safeReason = string.IsNullOrEmpty(reason) ? "RECOVERY" : reason;
            string currentPath = "/";

            SystemLogger.Log(SystemLogLevel.Warning, "RECOVERY",
                "Recovery mode entered: " + safeReason + (authenticatedAdmin ? " [admin]" : " [read-only]"));

            DrawBanner(safeReason, cause, authenticatedAdmin);

            while (true)
            {
                try
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.Write("recovery:" + currentPath + "# ");
                    Console.ForegroundColor = ConsoleColor.White;

                    string input = Console.ReadLine();
                    if (input == null)
                        continue;

                    input = input.Trim();
                    if (input.Length == 0)
                        continue;

                    string[] parts = SplitCommand(input);
                    if (parts.Length == 0)
                        continue;

                    string command = parts[0].ToLowerInvariant();
                    if (command == "help" || command == "?")
                    {
                        ShowHelp(authenticatedAdmin);
                    }
                    else if (command == "status")
                    {
                        ShowStatus(safeReason, cause, authenticatedAdmin);
                    }
                    else if (command == "logs")
                    {
                        int count = ParseCount(parts, 12, MaxLogLines);
                        ShowRecentLogs(count);
                    }
                    else if (command == "ls")
                    {
                        string target = parts.Length > 1 ? ResolvePath(currentPath, parts[1]) : currentPath;
                        ListDirectory(target);
                    }
                    else if (command == "cd")
                    {
                        string target = parts.Length > 1 ? ResolvePath(currentPath, parts[1]) : "/";
                        if (Directory.Exists(target))
                            currentPath = NormalizePath(target);
                        else
                            WriteRecoveryError("Directory not found: " + target);
                    }
                    else if (command == "view" || command == "cat")
                    {
                        if (parts.Length < 2)
                            WriteRecoveryError("Usage: view <path>");
                        else
                            ViewFile(ResolvePath(currentPath, parts[1]));
                    }
                    else if (command == "disk" || command == "lsblk")
                    {
                        ShowDisks();
                    }
                    else if (command == "net")
                    {
                        ShowNetwork();
                    }
                    else if (command == "clear")
                    {
                        Console.Clear();
                        DrawBanner(safeReason, cause, authenticatedAdmin);
                    }
                    else if (command == "reset-settings")
                    {
                        ResetSettings(parts, authenticatedAdmin);
                    }
                    else if (command == "exit")
                    {
                        if (!authenticatedAdmin)
                        {
                            WriteRecoveryError("Automatic recovery cannot exit into the failed boot path.");
                            continue;
                        }

                        SystemLogger.Log(SystemLogLevel.Info, "RECOVERY", "Administrator exited manual recovery mode.");
                        RestoreConsole();
                        return;
                    }
                    else if (command == "reboot")
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "RECOVERY", "Reboot requested from recovery mode.");
                        Cosmos.Kernel.System.Power.Reboot();
                    }
                    else if (command == "shutdown" || command == "poweroff")
                    {
                        SystemLogger.Log(SystemLogLevel.Warning, "RECOVERY", "Shutdown requested from recovery mode.");
                        Cosmos.Kernel.System.Power.Shutdown();
                    }
                    else
                    {
                        WriteRecoveryError("Unknown recovery command: " + command + ". Type 'help'.");
                    }
                }
                catch (Exception ex)
                {
                    SystemLogger.Log(SystemLogLevel.Error, "RECOVERY", "Recovery command failed: " + ex.Message);
                    WriteRecoveryError("Command failed: " + ex.Message);
                }
            }
        }

        private static void DrawBanner(string reason, Exception cause, bool authenticatedAdmin)
        {
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("============================================================");
                Console.WriteLine("                 ZOnderqOS RECOVERY MODE");
                Console.WriteLine("============================================================");
                Console.ForegroundColor = ConsoleColor.White;
                Console.WriteLine("Reason : " + reason);
                Console.WriteLine("Access : " + (authenticatedAdmin ? "authenticated root / maintenance" : "read-only boot recovery"));
                if (cause != null)
                    Console.WriteLine("Cause  : " + cause.GetType().Name + ": " + cause.Message);
                Console.WriteLine("Type 'help' for available recovery commands.");
                Console.WriteLine();
            }
            catch
            {
            }
        }

        private static void ShowHelp(bool authenticatedAdmin)
        {
            Console.WriteLine("help                 Show this command list");
            Console.WriteLine("status               Show boot, RAM, network and logger state");
            Console.WriteLine("logs [count]         Show recent in-memory system log entries");
            Console.WriteLine("ls [path]            List a directory (bounded)");
            Console.WriteLine("cd [path]            Change recovery working directory");
            Console.WriteLine("view <path>          Read first " + MaxViewLines + " lines of a text file");
            Console.WriteLine("disk                 List detected block devices");
            Console.WriteLine("net                  Show network interfaces if initialized");
            Console.WriteLine("clear                Clear the recovery console");
            if (authenticatedAdmin)
            {
                Console.WriteLine("reset-settings CONFIRM  Remove saved system settings");
                Console.WriteLine("exit                 Return to the authenticated shell");
            }
            Console.WriteLine("reboot               Restart the machine");
            Console.WriteLine("shutdown             Power off the machine");
        }

        private static void ShowStatus(string reason, Exception cause, bool authenticatedAdmin)
        {
            Console.WriteLine("Recovery reason : " + reason);
            Console.WriteLine("Access mode     : " + (authenticatedAdmin ? "ADMIN" : "READ-ONLY"));
            Console.WriteLine("Authenticated   : " + SecurityContext.IsAuthenticated);
            Console.WriteLine("Current user    : " + (SecurityContext.CurrentUser ?? "none"));
            Console.WriteLine("System log RAM  : " + SystemLogger.Count + " entries");
            Console.WriteLine("Network ready   : " + Network.IsReady);
            Console.WriteLine("Network devices : " + (Network.Devices == null ? 0 : Network.Devices.Count));

            try
            {
                ulong total = PageAllocator.TotalPageCount;
                ulong free = PageAllocator.FreePageCount;
                ulong used = total >= free ? total - free : 0;
                Console.WriteLine("Memory pages    : used=" + used + " free=" + free + " total=" + total);
            }
            catch
            {
                Console.WriteLine("Memory pages    : unavailable");
            }

            if (cause != null)
                Console.WriteLine("Failure         : " + cause.GetType().Name + ": " + cause.Message);
        }

        private static void ShowRecentLogs(int requested)
        {
            int available = SystemLogger.Count;
            int take = Math.Min(Math.Max(1, requested), Math.Min(MaxLogLines, available));
            if (take <= 0)
            {
                Console.WriteLine("No in-memory system log entries available.");
                return;
            }

            for (int offset = take - 1; offset >= 0; offset--)
            {
                SystemLogEntry entry;
                if (SystemLogger.TryGetRecent(offset, out entry))
                    Console.WriteLine(SystemLogger.Format(entry));
            }
        }

        private static void ListDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                WriteRecoveryError("Directory not found: " + path);
                return;
            }

            int shown = 0;
            string[] directories = Directory.GetDirectories(path);
            for (int i = 0; i < directories.Length && shown < MaxListEntries; i++, shown++)
                Console.WriteLine("[D] " + BaseName(directories[i]));

            string[] files = Directory.GetFiles(path);
            for (int i = 0; i < files.Length && shown < MaxListEntries; i++, shown++)
                Console.WriteLine("[F] " + BaseName(files[i]));

            if (shown >= MaxListEntries && directories.Length + files.Length > shown)
                Console.WriteLine("... output truncated after " + MaxListEntries + " entries");
            else if (shown == 0)
                Console.WriteLine("(empty)");
        }

        private static void ViewFile(string path)
        {
            if (!File.Exists(path))
            {
                WriteRecoveryError("File not found: " + path);
                return;
            }

            using (StreamReader reader = new StreamReader(path))
            {
                int line = 0;
                string text;
                while (line < MaxViewLines && (text = reader.ReadLine()) != null)
                {
                    Console.WriteLine(text);
                    line++;
                }

                if (!reader.EndOfStream)
                    Console.WriteLine("... file output truncated after " + MaxViewLines + " lines");
            }
        }

        private static void ShowDisks()
        {
            try
            {
                Disk.ListDisks();
            }
            catch (Exception ex)
            {
                WriteRecoveryError("Disk subsystem unavailable: " + ex.Message);
            }
        }

        private static void ShowNetwork()
        {
            try
            {
                if (Network.Devices == null || Network.Devices.Count == 0)
                {
                    Console.WriteLine("Network stack has no detected devices or was not initialized.");
                    return;
                }

                Network.ShowInterfaceInfo();
            }
            catch (Exception ex)
            {
                WriteRecoveryError("Network subsystem unavailable: " + ex.Message);
            }
        }

        private static void ResetSettings(string[] parts, bool authenticatedAdmin)
        {
            if (!authenticatedAdmin || !SecurityContext.IsAuthenticated ||
                SecurityContext.CurrentUid != 0 || SecurityContext.CurrentUser != "root")
            {
                WriteRecoveryError("reset-settings requires an authenticated root session.");
                return;
            }

            if (parts.Length < 2 || parts[1] != "CONFIRM")
            {
                Console.WriteLine("This removes saved GUI/network/system preferences.");
                Console.WriteLine("Run: reset-settings CONFIRM");
                return;
            }

            try
            {
                if (File.Exists(SettingsPath))
                    File.Delete(SettingsPath);

                SystemLogger.Log(SystemLogLevel.Warning, "RECOVERY",
                    "System settings reset by authenticated root from recovery mode.");
                SecurityLogger.LogEvent("WARN", "System settings reset from recovery mode by root.");
                Console.WriteLine("Saved settings removed. Defaults will be recreated on next load/boot.");
            }
            catch (Exception ex)
            {
                WriteRecoveryError("Could not reset settings: " + ex.Message);
            }
        }

        private static string[] SplitCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new string[0];

            // Recovery commands deliberately use a tiny parser: first token + one path/value.
            // This avoids pipelines/redirection or normal-shell mutation commands in safe mode.
            int firstSpace = input.IndexOf(' ');
            if (firstSpace < 0)
                return new[] { input };

            string command = input.Substring(0, firstSpace).Trim();
            string argument = input.Substring(firstSpace + 1).Trim();
            if (argument.Length >= 2 &&
                ((argument[0] == '"' && argument[argument.Length - 1] == '"') ||
                 (argument[0] == '\'' && argument[argument.Length - 1] == '\'')))
            {
                argument = argument.Substring(1, argument.Length - 2);
            }

            return argument.Length == 0 ? new[] { command } : new[] { command, argument };
        }

        private static int ParseCount(string[] parts, int defaultValue, int maxValue)
        {
            if (parts.Length < 2)
                return defaultValue;

            int value;
            if (!int.TryParse(parts[1], out value))
                return defaultValue;
            if (value < 1)
                value = 1;
            if (value > maxValue)
                value = maxValue;
            return value;
        }

        private static string ResolvePath(string currentPath, string requested)
        {
            try
            {
                return NormalizePath(PathResolver.GetAbsolutePath(currentPath, requested));
            }
            catch
            {
                if (string.IsNullOrEmpty(requested))
                    return currentPath;
                return requested[0] == '/' ? NormalizePath(requested) : NormalizePath(currentPath + "/" + requested);
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "/";

            path = path.Replace('\\', '/');
            while (path.Length > 1 && path.EndsWith("/"))
                path = path.Substring(0, path.Length - 1);
            return path.Length == 0 ? "/" : path;
        }

        private static string BaseName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalized = path.Replace('\\', '/');
            int index = normalized.LastIndexOf('/');
            return index >= 0 && index + 1 < normalized.Length ? normalized.Substring(index + 1) : normalized;
        }

        private static void WriteRecoveryError(string message)
        {
            try
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[RECOVERY] " + message);
                Console.ForegroundColor = ConsoleColor.White;
            }
            catch
            {
            }
        }

        private static void RestoreConsole()
        {
            try
            {
                Console.BackgroundColor = ConsoleColor.Black;
                Console.ForegroundColor = ConsoleColor.White;
                Console.Clear();
            }
            catch
            {
            }
        }
    }
}
