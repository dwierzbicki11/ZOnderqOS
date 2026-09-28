using System;

namespace ZonderqOS.Commands
{
    public sealed class CmdWhich : ICommand
    {
        public string Name => "which";
        public string Description => "Show whether a shell command is registered";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 2)
            {
                WriteMessage.WriteError("Usage: which <command>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string target = args[1];
            string[] names = Command.GetCommandNames();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], target, StringComparison.OrdinalIgnoreCase))
                {
                    CommandIO.WriteLine("builtin:" + names[i]);
                    CommandIO.LastCommandSuccess = true;
                    return;
                }
            }

            CommandIO.WriteLine(target + " not found");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
