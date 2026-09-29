using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdBootMode : ICommand
    {
        public string Name => "bootmode";
        public string Description => "Show or change default boot mode (gui|console)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 1 || (args.Length == 2 && string.Equals(args[1], "status", StringComparison.OrdinalIgnoreCase)))
            {
                CommandIO.WriteLine("Default boot mode: " + (SystemSettings.BootToGui ? "gui" : "console"));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (args.Length != 2)
            {
                PrintUsage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string mode = args[1] ?? string.Empty;
            bool enabled;
            if (string.Equals(mode, "gui", StringComparison.OrdinalIgnoreCase))
                enabled = true;
            else if (string.Equals(mode, "console", StringComparison.OrdinalIgnoreCase))
                enabled = false;
            else
            {
                PrintUsage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                CommandIO.WriteLine("bootmode: root privileges are required to change the persistent boot mode.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!SystemSettings.SetBootToGui(enabled))
            {
                CommandIO.WriteLine("bootmode: failed to save /etc/zonderq/settings.conf.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("Default boot mode set to: " + (enabled ? "gui" : "console"));
            CommandIO.WriteLine("The change will apply after reboot.");
            CommandIO.LastCommandSuccess = true;
        }

        private static void PrintUsage()
        {
            CommandIO.WriteLine("Usage: bootmode [status|gui|console]");
        }
    }
}
