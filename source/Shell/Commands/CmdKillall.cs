using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdKillall : ICommand
    {
        public string Name => "killall";
        public string Description => "Request cancellation of all running processes with an exact name";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 2 || string.IsNullOrEmpty(args[1]))
            {
                CommandIO.WriteLine("Usage: killall <process-name>");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            int matched = 0;
            int signalled = 0;

            // Operate on the snapshot: ProcessManager.Kill may change the live registry.
            for (int i = 0; i < processes.Count; i++)
            {
                KernelProcess process = processes[i];
                if (!process.IsRunning ||
                    !string.Equals(process.Name, args[1], StringComparison.OrdinalIgnoreCase))
                    continue;

                matched++;
                if (ProcessManager.Kill(process.PID))
                    signalled++;
            }

            if (matched == 0)
            {
                CommandIO.WriteLine("killall: no active process named '" + args[1] + "'.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("killall: cancellation requested for " + signalled + "/" + matched + " process(es).");
            CommandIO.LastCommandSuccess = signalled == matched;
        }
    }
}
