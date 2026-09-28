using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdCrashInfo : ICommand
    {
        public string Name => "crashinfo";
        public string Description => "Show or clear the last persistent kernel panic report";

        public void Execute(string[] args, ref string currentPath)
        {
            string action = args.Length > 1 ? args[1].ToLowerInvariant() : "show";

            if (action == "show")
            {
                string report;
                string error;
                if (!CrashReportStore.TryRead(out report, out error))
                {
                    WriteMessage.WriteInfo(error, "CRASH");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine(report);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "clear")
            {
                string error;
                if (!CrashReportStore.TryClear(out error))
                {
                    WriteMessage.WriteError(error, "CRASH");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                SystemLogger.Log(SystemLogLevel.Warning, "CRASH", "Persistent panic report cleared by root.");
                WriteMessage.WriteOK("Persistent panic report cleared.", "CRASH");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "help" || action == "-h" || action == "--help")
            {
                PrintHelp();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            WriteMessage.WriteError("Unknown crashinfo action: " + action, "CRASH");
            PrintHelp();
            CommandIO.LastCommandSuccess = false;
        }

        private static void PrintHelp()
        {
            CommandIO.WriteLine("Usage:");
            CommandIO.WriteLine("  crashinfo show");
            CommandIO.WriteLine("  crashinfo clear   (root only)");
        }
    }
}
