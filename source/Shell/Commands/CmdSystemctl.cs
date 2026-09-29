using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore.Services;

namespace ZonderqOS.Commands
{
    public sealed class CmdSystemctl : ICommand
    {
        private readonly List<ServiceStatus> statuses = new List<ServiceStatus>(16);

        public string Name => "systemctl";
        public string Description => "Manage ZOnderqOS configured services";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                Help();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string action = args[1].ToLowerInvariant();

            if (action == "list-units" || action == "list")
            {
                if (args.Length != 2)
                {
                    Help();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                ServiceManager.FillStatus(statuses);
                CommandIO.WriteLine("UNIT                     LOAD     ACTIVE    TYPE        ENABLED");
                for (int i = 0; i < statuses.Count; i++)
                {
                    ServiceStatus item = statuses[i];
                    CommandIO.WriteLine(
                        (item.Name + ".service").PadRight(25) +
                        "loaded".PadRight(9) +
                        (item.Running ? "active" : "inactive").PadRight(10) +
                        item.Type.PadRight(12) +
                        (item.Enabled ? "enabled" : "disabled"));
                }

                if (statuses.Count == 0)
                    CommandIO.WriteLine("No configured services.");

                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (action == "status")
            {
                if (args.Length != 3)
                {
                    Help();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string name = NormalizeUnit(args[2]);
                ServiceManager.FillStatus(statuses);
                for (int i = 0; i < statuses.Count; i++)
                {
                    ServiceStatus item = statuses[i];
                    if (!string.Equals(item.Name, name, StringComparison.OrdinalIgnoreCase))
                        continue;

                    CommandIO.WriteLine("● " + item.Name + ".service");
                    CommandIO.WriteLine("   Loaded: loaded (/etc/zservices/" + item.Name + ".conf; " +
                        (item.Enabled ? "enabled" : "disabled") + ")");
                    CommandIO.WriteLine("   Active: " + (item.Running ? "active (running)" : "inactive (dead)"));
                    CommandIO.WriteLine("   Type:   " + item.Type);
                    if (item.Running)
                        CommandIO.WriteLine("   Main PID: " + item.Pid);
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                CommandIO.WriteLine("Unit " + name + ".service could not be found.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (action == "daemon-reload")
            {
                if (args.Length != 2)
                {
                    Help();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                ExecuteRootOperation(() =>
                {
                    string error;
                    bool ok = ServiceManager.Reload(out error);
                    return new OperationResult(ok, error);
                });
                return;
            }

            if (action == "start" || action == "stop" || action == "restart" ||
                action == "enable" || action == "disable")
            {
                if (args.Length != 3)
                {
                    Help();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string name = NormalizeUnit(args[2]);
                ExecuteRootOperation(() =>
                {
                    string error;
                    bool ok = action == "start" ? ServiceManager.Start(name, out error) :
                              action == "stop" ? ServiceManager.Stop(name, out error) :
                              action == "restart" ? ServiceManager.Restart(name, out error) :
                              ServiceManager.SetEnabled(name, action == "enable", out error);
                    return new OperationResult(ok, error);
                });
                return;
            }

            Help();
            CommandIO.LastCommandSuccess = false;
        }

        private static void ExecuteRootOperation(Func<OperationResult> operation)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                CommandIO.WriteLine("systemctl: root privileges are required.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            OperationResult result = operation();
            CommandIO.WriteLine(result.Success ? "Operation completed." : "systemctl: " + result.Error);
            CommandIO.LastCommandSuccess = result.Success;
        }

        private static string NormalizeUnit(string value)
        {
            if (value != null && value.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
                return value.Substring(0, value.Length - 8);
            return value ?? string.Empty;
        }

        private static void Help()
        {
            CommandIO.WriteLine("systemctl list-units");
            CommandIO.WriteLine("systemctl status <name>[.service]");
            CommandIO.WriteLine("systemctl start|stop|restart <name>[.service]");
            CommandIO.WriteLine("systemctl enable|disable <name>[.service]");
            CommandIO.WriteLine("systemctl daemon-reload");
        }

        private readonly struct OperationResult
        {
            public OperationResult(bool success, string error)
            {
                Success = success;
                Error = error ?? string.Empty;
            }

            public bool Success { get; }
            public string Error { get; }
        }
    }
}
