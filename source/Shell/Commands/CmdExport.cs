namespace ZonderqOS.Commands
{
    public class CmdExport : ICommand
    {
        public string Name => "export";
        public string Description => "Set environment variable (export KEY=VALUE)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                string assignment = string.Join(" ", args, 1, args.Length - 1);
                string[] parts = assignment.Split('=');
                if (parts.Length == 2)
                {
                    string key = parts[0].Trim();
                    string val = parts[1].Trim();
                    EnvironmentManager.Set(key, val);
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Usage: export KEY=VALUE", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else
            {
                WriteMessage.WriteError("Usage: export KEY=VALUE", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}