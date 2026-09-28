using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdDate : ICommand
    {
        public string Name => "date";
        public string Description => "Show current system date and local time";

        public void Execute(string[] args, ref string currentPath)
        {
            DateTime now = DateTime.UtcNow.AddHours(SystemSettings.TimeZoneOffsetHours);
            int offset = SystemSettings.TimeZoneOffsetHours;
            string sign = offset >= 0 ? "+" : "-";
            int abs = offset >= 0 ? offset : -offset;

            CommandIO.WriteLine(
                now.Year.ToString("D4") + "-" +
                now.Month.ToString("D2") + "-" +
                now.Day.ToString("D2") + " " +
                now.Hour.ToString("D2") + ":" +
                now.Minute.ToString("D2") + ":" +
                now.Second.ToString("D2") +
                " UTC" + sign + abs);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
