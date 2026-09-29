using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdDmesg : ICommand
    {
        private const int MaxEntries = 96;
        private readonly SystemLogEntry[] buffer = new SystemLogEntry[MaxEntries];

        public string Name => "dmesg";
        public string Description => "Show recent in-memory kernel/system messages";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 2 && args[1] == "-c")
            {
                if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
                {
                    CommandIO.WriteLine("dmesg: clearing the kernel log requires root.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                bool ok = SystemLogger.Clear();
                CommandIO.WriteLine(ok ? "dmesg: log cleared." : "dmesg: log buffer cleared; persistent log clear failed.");
                CommandIO.LastCommandSuccess = ok;
                return;
            }

            int requested = 40;
            string filter = string.Empty;

            if (args.Length >= 2)
            {
                if (!int.TryParse(args[1], out requested))
                {
                    PrintUsage();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (requested < 1) requested = 1;
                if (requested > MaxEntries) requested = MaxEntries;
            }

            if (args.Length >= 3)
                filter = string.Join(" ", args, 2, args.Length - 2);

            int written = 0;
            int available = Math.Min(SystemLogger.Count, MaxEntries);

            // SystemLogger indexes from newest to oldest. Fill backwards so output
            // remains chronological like a normal kernel log viewer.
            for (int offset = 0; offset < available && written < requested; offset++)
            {
                if (!SystemLogger.TryGetRecent(offset, out SystemLogEntry entry))
                    break;

                string line = SystemLogger.Format(entry);
                if (!string.IsNullOrEmpty(filter) &&
                    line.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                buffer[written++] = entry;
            }

            for (int i = written - 1; i >= 0; i--)
                CommandIO.WriteLine(SystemLogger.Format(buffer[i]));

            if (written == 0)
                CommandIO.WriteLine("(no matching kernel messages)");

            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintUsage()
        {
            CommandIO.WriteLine("Usage: dmesg [1-96] [filter]");
            CommandIO.WriteLine("       dmesg -c");
        }
    }
}
