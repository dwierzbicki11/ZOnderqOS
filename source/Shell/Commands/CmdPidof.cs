using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdPidof : ICommand
    {
        public string Name => "pidof";
        public string Description => "Print PIDs of running processes with an exact name";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 2 || string.IsNullOrEmpty(args[1]))
            {
                CommandIO.WriteLine("Usage: pidof <process-name>");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            bool found = false;
            string line = string.Empty;

            for (int i = 0; i < processes.Count; i++)
            {
                KernelProcess process = processes[i];
                if (!process.IsRunning ||
                    !string.Equals(process.Name, args[1], StringComparison.OrdinalIgnoreCase))
                    continue;

                if (found)
                    line += " ";
                line += process.PID.ToString();
                found = true;
            }

            if (found)
                CommandIO.WriteLine(line);

            CommandIO.LastCommandSuccess = found;
        }
    }
}
