namespace ZonderqOS.Commands
{
    public class CmdExport : ICommand
    {
        public string Name => "export";
        public string Description => "Set environment variable (export KEY=VALUE)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: export KEY=VALUE", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string assignment = string.Join(" ", args, 1, args.Length - 1);
            int separator = assignment.IndexOf('=');
            if (separator <= 0)
            {
                WriteMessage.WriteError("Usage: export KEY=VALUE", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string key = assignment.Substring(0, separator).Trim();
            string value = assignment.Substring(separator + 1).Trim();
            if (!EnvironmentManager.TrySet(key, value))
            {
                WriteMessage.WriteError("Invalid environment variable name/value or environment limit reached.", "ENV");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.LastCommandSuccess = true;
        }
    }
}
