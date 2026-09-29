namespace ZonderqOS.Commands
{
    public sealed class CmdSystemctl : ICommand
    {
        public string Name => "systemctl";
        public string Description => "Lightweight systemctl-compatible frontend for ZOnderqOS services";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                CommandIO.WriteLine("Usage: systemctl list-units | status/start/stop/restart/enable/disable <name> | daemon-reload");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string[] translated;

            if (args[1] == "list-units")
            {
                translated = new[] { "service", "list" };
            }
            else if (args[1] == "daemon-reload")
            {
                translated = new[] { "service", "reload" };
            }
            else
            {
                translated = new string[args.Length];
                translated[0] = "service";
                for (int i = 1; i < args.Length; i++)
                    translated[i] = args[i];
            }

            new CmdService().Execute(translated, ref currentPath);
        }
    }
}
