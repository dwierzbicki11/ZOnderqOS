using System.Diagnostics;

namespace ZonderqOS.Commands
{
    public sealed class CmdUptime : ICommand
    {
        public string Name => "uptime";
        public string Description => "Show kernel uptime";

        public void Execute(string[] args, ref string currentPath)
        {
            long frequency = Stopwatch.Frequency;
            long ticks = Stopwatch.GetTimestamp();

            if (frequency <= 0 || ticks < 0)
            {
                WriteMessage.WriteError("Uptime counter unavailable.", "SYS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            long totalSeconds = ticks / frequency;
            long days = totalSeconds / 86400;
            totalSeconds %= 86400;
            long hours = totalSeconds / 3600;
            totalSeconds %= 3600;
            long minutes = totalSeconds / 60;
            long seconds = totalSeconds % 60;

            CommandIO.WriteLine(
                "up " + days + "d " +
                hours.ToString("D2") + ":" +
                minutes.ToString("D2") + ":" +
                seconds.ToString("D2"));
            CommandIO.LastCommandSuccess = true;
        }
    }
}
