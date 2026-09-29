using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdPgrep : ICommand
    {
        public string Name => "pgrep";
        public string Description => "Find running processes by name";

        public void Execute(string[] args, ref string currentPath)
        {
            bool exact = false;
            string pattern = null;

            if (args.Length == 2)
                pattern = args[1];
            else if (args.Length == 3 && (args[1] == "-x" || args[1] == "--exact"))
            {
                exact = true;
                pattern = args[2];
            }
            else
            {
                CommandIO.WriteLine("Usage: pgrep [-x] <name-pattern>");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (string.IsNullOrEmpty(pattern))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            bool found = false;

            for (int i = 0; i < processes.Count; i++)
            {
                KernelProcess process = processes[i];
                if (!process.IsRunning)
                    continue;

                string name = process.Name ?? string.Empty;
                bool match = exact
                    ? string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase)
                    : name.IndexOf(pattern, StringComparison.OrdinalIgnoreCase) >= 0;

                if (!match)
                    continue;

                CommandIO.WriteLine(process.PID + " " + name);
                found = true;
            }

            CommandIO.LastCommandSuccess = found;
        }
    }
}
