using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdKill : ICommand
    {
        public string Name => "kill";
        public string Description => "Send cooperative SIGTERM or SIGINT to a process";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args == null || args.Length == 0)
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length == 2 && args[1] == "-l")
            {
                CommandIO.WriteLine(" 2  SIGINT   cooperative interrupt");
                CommandIO.WriteLine("15  SIGTERM  cooperative termination");
                CommandIO.WriteLine(" 9  SIGKILL  unavailable (no safe forced thread termination)");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            ProcessSignal signal = ProcessSignal.Terminate;
            string pidText;

            if (args.Length == 2)
            {
                pidText = args[1];
            }
            else if (args.Length == 3)
            {
                if (args[1] == "-9" ||
                    args[1].Equals("-KILL", StringComparison.OrdinalIgnoreCase) ||
                    args[1].Equals("-SIGKILL", StringComparison.OrdinalIgnoreCase))
                {
                    CommandIO.WriteLine("kill: SIGKILL is not supported; managed threads cannot be forcibly terminated safely.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!ProcessSignals.TryParse(args[1], out signal))
                {
                    Usage();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
                pidText = args[2];
            }
            else
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!int.TryParse(pidText, out int pid) || pid <= 0)
            {
                CommandIO.WriteLine("kill: invalid PID.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            bool success = ProcessManager.SendSignal(pid, signal);
            CommandIO.WriteLine(success
                ? "kill: sent " + ProcessSignals.Name(signal) + " to PID " + pid + "."
                : "kill: no active process with PID " + pid + ".");
            CommandIO.LastCommandSuccess = success;
        }

        private static void Usage()
        {
            CommandIO.WriteLine("Usage: kill [-TERM|-15|-INT|-2] <pid>");
            CommandIO.WriteLine("       kill -l");
        }
    }
}
