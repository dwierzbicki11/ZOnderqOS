using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdViewLog : ICommand
    {
        private const int MaxLines = 200;
        private readonly string[] buffer = new string[MaxLines];

        public string Name => "viewlog";
        public string Description => "Show bounded tails of system/auth/guardian logs";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1 &&
                (args[1] == "help" || args[1] == "-h" || args[1] == "--help"))
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (args.Length > 1 && args[1].Equals("sources", StringComparison.OrdinalIgnoreCase))
            {
                CommandIO.WriteLine("system   -> " + SystemLogger.LogPath);
                CommandIO.WriteLine("auth     -> /var/log/auth.log");
                CommandIO.WriteLine("guardian -> /sysmon.log");
                CommandIO.WriteLine("error    -> /var/error_log.txt (legacy)");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string source = args.Length > 1 ? args[1] : "system";
            if (!LogQuery.IsKnownSource(source))
            {
                WriteMessage.WriteError("Unknown log source: " + source, "LOG");
                PrintHelp();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            int count = 40;
            if (args.Length > 2)
            {
                if (!int.TryParse(args[2], out count))
                {
                    WriteMessage.WriteError("Invalid line count: " + args[2], "LOG");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (count < 1)
                    count = 1;
                if (count > MaxLines)
                    count = MaxLines;
            }

            string filter = args.Length > 3
                ? string.Join(" ", args, 3, args.Length - 3)
                : string.Empty;

            string path = LogQuery.ResolveSource(source);
            if (!System.IO.File.Exists(path))
            {
                WriteMessage.WriteInfo("Log source is empty or unavailable: " + path, "LOG");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError("Permission denied: cannot read " + path, "SEC");
                SecurityLogger.LogEvent(
                    "WARN",
                    "Unauthorized viewlog attempt on " + path + " by " + SecurityContext.CurrentUser);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            long bytes;
            string error;
            int lines = LogQuery.ReadTail(path, buffer, count, filter, out bytes, out error);
            if (!string.IsNullOrEmpty(error))
            {
                WriteMessage.WriteError("Log query failed: " + error, "LOG");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine(
                "=== " + source.ToUpperInvariant() +
                " | lines=" + lines +
                " | bytes=" + bytes +
                (string.IsNullOrEmpty(filter) ? "" : " | filter=" + filter) +
                " ===");

            for (int i = 0; i < lines; i++)
                CommandIO.WriteLine(buffer[i] ?? string.Empty);

            if (lines == 0)
                CommandIO.WriteLine("(no matching entries)");

            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  viewlog [system|auth|guardian|error] [1-200] [filter]");
            CommandIO.WriteLine("  viewlog sources");
        }
    }
}
