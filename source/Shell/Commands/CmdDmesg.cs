using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdDmesg : ICommand
    {
        private const int MaxEntries = 96;
        private readonly SystemLogEntry[] _buffer = new SystemLogEntry[MaxEntries];

        public string Name => "dmesg";
        public string Description => "Show recent in-memory kernel/system log entries";

        public void Execute(string[] args, ref string currentPath)
        {
            int limit = 50;
            SystemLogLevel minimumLevel = SystemLogLevel.Debug;
            bool clearAfter = false;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];

                if (arg == "-c" || arg == "--clear")
                {
                    clearAfter = true;
                    continue;
                }

                if (arg == "-n" || arg == "--lines")
                {
                    if (i + 1 >= args.Length || !int.TryParse(args[++i], out limit) || limit < 1 || limit > MaxEntries)
                    {
                        Usage();
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                    continue;
                }

                if (arg == "--level")
                {
                    if (i + 1 >= args.Length || !TryParseLevel(args[++i], out minimumLevel))
                    {
                        Usage();
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                    continue;
                }

                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (clearAfter && (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0))
            {
                CommandIO.WriteLine("dmesg: root privileges are required for -c.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            int selectedCount = 0;
            int available = Math.Min(SystemLogger.Count, MaxEntries);

            for (int offset = 0; offset < available && selectedCount < limit; offset++)
            {
                SystemLogEntry entry;
                if (!SystemLogger.TryGetRecent(offset, out entry))
                    break;

                if (entry.Level < minimumLevel)
                    continue;

                _buffer[selectedCount++] = entry;
            }

            for (int i = selectedCount - 1; i >= 0; i--)
                CommandIO.WriteLine(SystemLogger.Format(_buffer[i]));

            if (selectedCount == 0)
                CommandIO.WriteLine("dmesg: no matching in-memory log entries.");

            if (clearAfter)
            {
                if (!SystemLogger.Clear())
                {
                    CommandIO.WriteLine("dmesg: log output shown, but clearing the persistent log failed.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("dmesg: log buffer cleared.");
            }

            CommandIO.LastCommandSuccess = true;
        }

        private static bool TryParseLevel(string value, out SystemLogLevel level)
        {
            if (string.Equals(value, "debug", StringComparison.OrdinalIgnoreCase))
            {
                level = SystemLogLevel.Debug;
                return true;
            }

            if (string.Equals(value, "info", StringComparison.OrdinalIgnoreCase))
            {
                level = SystemLogLevel.Info;
                return true;
            }

            if (string.Equals(value, "warn", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(value, "warning", StringComparison.OrdinalIgnoreCase))
            {
                level = SystemLogLevel.Warning;
                return true;
            }

            if (string.Equals(value, "error", StringComparison.OrdinalIgnoreCase))
            {
                level = SystemLogLevel.Error;
                return true;
            }

            if (string.Equals(value, "critical", StringComparison.OrdinalIgnoreCase))
            {
                level = SystemLogLevel.Critical;
                return true;
            }

            level = SystemLogLevel.Debug;
            return false;
        }

        private static void Usage()
        {
            CommandIO.WriteLine("Usage: dmesg [-n 1..96] [--level debug|info|warn|error|critical] [-c]");
        }
    }
}
