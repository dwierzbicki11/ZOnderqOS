using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdNeofetch : ICommand
    {
        public string Name => "neofetch";
        public string Description => "Display system information and stylized ASCII logo";

        public void Execute(string[] args, ref string currentPath)
        {
            HardwareSnapshot info = HardwareSnapshot.Capture();
            ulong seconds = BootTelemetry.UptimeSeconds <= 0d ? 0UL : (ulong)BootTelemetry.UptimeSeconds;
            ulong hours = seconds / 3600UL;
            ulong mins = (seconds % 3600UL) / 60UL;
            ulong secs = seconds % 60UL;

            string uptimeStr = (hours > 0 ? hours + "h " : "") + mins + "m " + secs + "s";
            string username = SecurityContext.IsAuthenticated && !string.IsNullOrEmpty(SecurityContext.CurrentUser)
                ? SecurityContext.CurrentUser
                : "zonderq";
            string userStr = username + "@zonderqos";
            string separator = "----------------------------------";

            ulong memUsedMb = info.UsedMemoryBytes / (1024UL * 1024UL);
            ulong memTotalMb = info.TotalMemoryBytes / (1024UL * 1024UL);

            string[] logo =
            {
                "    ______  ____  ",
                "   /_  / / / / /  ",
                "    / / /_/ / /   ",
                "   / / ____/ /    ",
                "  / / /   / /___  ",
                " /_/_/   /_____/  ",
                "                  ",
                "                  "
            };

            string[] stats =
            {
                userStr,
                separator,
                "OS:       ZOnderqOS " + global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentVersion + " (Build " + global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentBuild + ")",
                "Kernel:   Cosmos v3.0.95-stock",
                "Uptime:   " + uptimeStr,
                "CPU:      " + info.CpuBrand,
                "Memory:   " + memUsedMb + " MiB / " + memTotalMb + " MiB",
                "Theme:    " + global::ZonderqOS.SystemSettings.AccentThemeName,
                "Desktop:  " + global::ZonderqOS.SystemSettings.DesktopBackgroundName,
                "Palette:  [#] [#] [#] [#] [#] [#] [#] [#]"
            };

            int lines = logo.Length > stats.Length ? logo.Length : stats.Length;
            for (int i = 0; i < lines; i++)
            {
                string left = i < logo.Length ? logo[i] : "                  ";
                string right = i < stats.Length ? stats[i] : "";
                CommandIO.WriteLine(left + "  " + right);
            }

            CommandIO.LastCommandSuccess = true;
        }
    }
}
