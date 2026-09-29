using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore.Services;

namespace ZonderqOS.Commands
{
    public sealed class CmdService : ICommand
    {
        private readonly List<ServiceStatus> statuses = new List<ServiceStatus>(16);

        public string Name => "service";
        public string Description => "List and manage configured background services";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                Help();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string action = args[1].ToLowerInvariant();
            if (action == "list" || action == "status")
            {
                ServiceManager.FillStatus(statuses);
                bool found = action == "list";
                foreach (ServiceStatus item in statuses)
                {
                    if (action == "status" && (args.Length != 3 ||
                        !string.Equals(item.Name, args[2], StringComparison.OrdinalIgnoreCase)))
                        continue;

                    found = true;
                    CommandIO.WriteLine(item.Name + "  " + (item.Running ? "running PID=" + item.Pid : "stopped") +
                        "  " + item.Type + "  enabled=" + item.Enabled);
                }
                if (action == "list" && statuses.Count == 0)
                    CommandIO.WriteLine("No configured services.");
                if (!found)
                    CommandIO.WriteLine("Service not found. Usage: service status <name>");
                CommandIO.LastCommandSuccess = found;
                return;
            }

            if (action == "help")
            {
                Help();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action != "reload" && action != "start" && action != "stop" &&
                action != "restart" && action != "enable" && action != "disable")
            {
                Help();
                CommandIO.LastCommandSuccess = false;
                return;
            }
            if ((action == "reload" && args.Length != 2) ||
                (action != "reload" && args.Length != 3))
            {
                Help();
                CommandIO.LastCommandSuccess = false;
                return;
            }
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                CommandIO.WriteLine("Only authenticated root can manage services.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string error;
            bool ok = action == "reload" ? ServiceManager.Reload(out error) :
                action == "start" ? ServiceManager.Start(args[2], out error) :
                action == "stop" ? ServiceManager.Stop(args[2], out error) :
                action == "restart" ? ServiceManager.Restart(args[2], out error) :
                ServiceManager.SetEnabled(args[2], action == "enable", out error);
            CommandIO.WriteLine(ok ? "Service operation completed." : "[ERROR] " + error);
            CommandIO.LastCommandSuccess = ok;
        }

        private static void Help()
        {
            CommandIO.WriteLine("service list | status <name> | start/stop/restart <name>");
            CommandIO.WriteLine("service enable/disable <name> | reload");
            CommandIO.WriteLine("Edit /etc/zservices/<name>.conf, then run service reload as root.");
        }
    }
}
