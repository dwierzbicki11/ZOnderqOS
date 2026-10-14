using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdKillall : ICommand
    {
        public string Name => "killall";
        public string Description => "Send SIGTERM or SIGINT to all processes with an exact name";

        public void Execute(string[] args, ref string currentPath)
        {
            ProcessSignal signal = ProcessSignal.Terminate;
            string processName;

            if (args.Length == 2)
            {
                processName = args[1];
            }
            else if (args.Length == 4 && args[1] == "-s")
            {
                if (args[2].Equals("KILL", StringComparison.OrdinalIgnoreCase) ||
                    args[2].Equals("SIGKILL", StringComparison.OrdinalIgnoreCase) ||
                    args[2] == "9")
                {
                    CommandIO.WriteLine("killall: SIGKILL is not available yet.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!ProcessSignals.TryParse(args[2], out signal))
                {
                    Usage();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                processName = args[3];
            }
            else
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (string.IsNullOrEmpty(processName))
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            int matched = 0;
            int signalled = 0;

            for (int i = 0; i < processes.Count; i++)
            {
                KernelProcess process = processes[i];
                if (!process.IsRunning ||
                    !string.Equals(process.Name, processName, StringComparison.OrdinalIgnoreCase))
                    continue;

                matched++;
                if (ProcessManager.SendSignal(process.PID, signal))
                    signalled++;
            }

            if (matched == 0)
            {
                CommandIO.WriteLine("killall: no active process named '" + processName + "'.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("killall: sent " + ProcessSignals.Name(signal) + " to " +
                signalled + "/" + matched + " process(es).");
            CommandIO.LastCommandSuccess = signalled == matched;
        }

        private static void Usage()
        {
            CommandIO.WriteLine("Usage: killall [-s TERM|INT] <process-name>");
        }
    }
}
