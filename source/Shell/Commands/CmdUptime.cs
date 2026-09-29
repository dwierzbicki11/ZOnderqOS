using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdUptime : ICommand
    {
        public string Name => "uptime";
        public string Description => "Show time since kernel boot";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: uptime");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            ulong seconds = BootTelemetry.UptimeSeconds <= 0d ? 0UL : (ulong)BootTelemetry.UptimeSeconds;
            ulong days = seconds / 86400UL;
            seconds %= 86400UL;
            ulong hours = seconds / 3600UL;
            seconds %= 3600UL;
            ulong minutes = seconds / 60UL;
            ulong secs = seconds % 60UL;

            string prefix = days > 0 ? days + " day" + (days == 1 ? "" : "s") + ", " : "";
            CommandIO.WriteLine("up " + prefix + hours.ToString("00") + ":" + minutes.ToString("00") + ":" + secs.ToString("00"));
            CommandIO.LastCommandSuccess = true;
        }
    }
}
