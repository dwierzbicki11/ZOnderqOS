using System.Collections.Generic;

namespace ZonderqOS.Commands
{
    public class CmdEnv : ICommand
    {
        private readonly KeyValuePair<string, string>[] variables =
            new KeyValuePair<string, string>[EnvironmentManager.MaxVariables];

        public string Name => "env";
        public string Description => "Print all runtime environment variables";

        public void Execute(string[] args, ref string currentPath)
        {
            int count = EnvironmentManager.CopyTo(variables);
            for (int i = 0; i < count; i++)
            {
                CommandIO.WriteLine(variables[i].Key + "=" + variables[i].Value);
                variables[i] = default(KeyValuePair<string, string>);
            }

            CommandIO.LastCommandSuccess = true;
        }
    }
}
