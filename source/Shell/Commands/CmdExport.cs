using System.Text;

namespace ZonderqOS.Commands
{
    public class CmdExport : ICommand
    {
        private const int MaxAssignmentCharacters = 4161;

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

            if (!TryBuildAssignment(args, out string assignment))
            {
                WriteMessage.WriteError($"Environment assignment exceeds {MaxAssignmentCharacters} characters.", "ENV");
                CommandIO.LastCommandSuccess = false;
                return;
            }

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
