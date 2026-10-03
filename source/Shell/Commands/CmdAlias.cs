using System.Collections.Generic;
using System.Text;

namespace ZonderqOS.Commands
{
    public sealed class CmdAlias : ICommand
    {
        private const int MaxAssignmentCharacters = 4096;
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

            if (!TryBuildAssignment(args, out string assignment))
            {
                WriteMessage.WriteError($"Alias assignment exceeds {MaxAssignmentCharacters} characters.", "ALIAS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

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

        private static bool TryBuildAssignment(string[] args, out string assignment)
        {
            var builder = new StringBuilder(System.Math.Min(256, MaxAssignmentCharacters));
            for (int i = 1; i < args.Length; i++)
            {
                string value = args[i] ?? string.Empty;
                int separator = i > 1 ? 1 : 0;
                if (value.Length > MaxAssignmentCharacters - builder.Length - separator)
                {
                    assignment = string.Empty;
                    return false;
                }

                if (separator != 0)
                    builder.Append(' ');
                builder.Append(value);
            }

            assignment = builder.ToString();
            return true;
        }
    }
}
