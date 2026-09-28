namespace ZonderqOS.Commands
{
    public sealed class CmdHostname : ICommand
    {
        public string Name => "hostname";
        public string Description => "Show current system hostname";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                WriteMessage.WriteError("Usage: hostname", "SYS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string host = EnvironmentManager.Get("HOSTNAME");
            CommandIO.WriteLine(string.IsNullOrEmpty(host) ? "ZOnderqOS" : host);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
