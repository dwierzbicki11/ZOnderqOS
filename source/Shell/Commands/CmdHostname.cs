using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdHostname : ICommand
    {
        public string Name => "hostname";
        public string Description => "Show or change the persistent system hostname";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 1)
            {
                CommandIO.WriteLine(EnvironmentManager.Get("HOSTNAME"));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string error;
            bool ok;
            if (args.Length == 2 && args[1] == "reload")
            {
                if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
                {
                    CommandIO.WriteLine("Only authenticated root can reload hostname.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
                ok = SystemIdentity.ReloadHostname(out error);
            }
            else if (args.Length == 3 && args[1] == "set")
                ok = SystemIdentity.SetHostname(args[2], out error);
            else
            {
                CommandIO.WriteLine("Usage: hostname | hostname set <name> | hostname reload");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine(ok ? "Hostname: " + EnvironmentManager.Get("HOSTNAME") : "[ERROR] " + error);
            CommandIO.LastCommandSuccess = ok;
        }
    }
}
