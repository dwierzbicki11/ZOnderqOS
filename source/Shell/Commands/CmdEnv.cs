using System.Collections.Generic;

namespace ZonderqOS.Commands
{
    public class CmdEnv : ICommand
    {
        private readonly List<KeyValuePair<string, string>> variables =
            new List<KeyValuePair<string, string>>(32);

        public string Name => "env";
        public string Description => "Print all runtime environment variables";

        public void Execute(string[] args, ref string currentPath)
        {
            EnvironmentManager.CopyTo(variables);
            for (int i = 0; i < variables.Count; i++)
                CommandIO.WriteLine(variables[i].Key + "=" + variables[i].Value);

            CommandIO.LastCommandSuccess = true;
        }
    }
}
