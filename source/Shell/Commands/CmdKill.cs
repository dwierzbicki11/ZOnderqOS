using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdKill : ICommand
    {
        public string Name => "kill";
        public string Description => "Send SIGTERM or SIGINT to a background process";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 2 && args[1] == "-l")
            {
                CommandIO.WriteLine(" 2  SIGINT   cooperative interrupt");
                CommandIO.WriteLine("15  SIGTERM  cooperative termination");
                CommandIO.WriteLine(" 9  SIGKILL  unavailable: forced scheduler termination is not implemented");
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
                    args[1].Equals("-KILL", System.StringComparison.OrdinalIgnoreCase) ||
                    args[1].Equals("-SIGKILL", System.StringComparison.OrdinalIgnoreCase))
                {
                    CommandIO.WriteLine("kill: SIGKILL is not available yet; the scheduler has no safe forced-thread termination primitive.");
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
            if (!success)
            {
                CommandIO.WriteLine("kill: no active process with PID " + pid + ".");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("kill: sent " + ProcessSignals.Name(signal) + " to PID " + pid + ".");
            CommandIO.LastCommandSuccess = true;
        }

        private static void Usage()
        {
            CommandIO.WriteLine("Usage: kill [-TERM|-15|-INT|-2] <pid>");
            CommandIO.WriteLine("       kill -l");
        }
    }
}
