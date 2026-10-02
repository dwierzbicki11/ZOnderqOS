using System.Collections.Generic;

namespace ZonderqOS.Commands
{
    public sealed class CmdAlias : ICommand
    {
        private readonly KeyValuePair<string,string>[] aliases = new KeyValuePair<string,string>[AliasManager.Capacity];
        public string Name => "alias";
        public string Description => "List or define shell aliases";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 1)
            {
                int count = AliasManager.CopyTo(aliases);
                for (int i = 0; i < count; i++)
                {
                    CommandIO.WriteLine(aliases[i].Key + "='" + aliases[i].Value + "'");
                    aliases[i] = default(KeyValuePair<string,string>);
                }
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string assignment = string.Join(" ", args, 1, args.Length - 1);
            int eq = assignment.IndexOf('=');
            if (eq <= 0)
            {
                WriteMessage.WriteError("Usage: alias name=command", "ALIAS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string name = assignment.Substring(0, eq).Trim();
            string value = assignment.Substring(eq + 1).Trim().Trim('"', '\'');
            if (!AliasManager.TrySet(name, value))
            {
                WriteMessage.WriteError("Invalid alias or alias limit reached.", "ALIAS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.LastCommandSuccess = true;
        }
    }
}
