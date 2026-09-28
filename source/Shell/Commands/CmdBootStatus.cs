using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdBootStatus : ICommand
    {
        public string Name => "bootstatus";
        public string Description => "Show previous boot-session shutdown state";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("=== ZOnderqOS Boot Session Health ===");
            CommandIO.WriteLine("Marker available: " + YesNo(BootSessionHealth.MarkerAvailable));

            if (!BootSessionHealth.HasPreviousSession)
            {
                CommandIO.WriteLine("Previous session: none recorded");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine(
                "Previous session: " +
                (BootSessionHealth.PreviousSessionWasClean ? "CLEAN" : "UNCLEAN"));
            CommandIO.WriteLine("Reason:           " + BootSessionHealth.PreviousExitReason);
            CommandIO.WriteLine("Timestamp:        " + BootSessionHealth.PreviousTimestamp);

            CommandIO.LastCommandSuccess = BootSessionHealth.PreviousSessionWasClean;
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
