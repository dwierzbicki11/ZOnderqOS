using System;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdKill : ICommand
    {
        public string Name => "kill";
        public string Description => "Send a process signal: kill [-TERM|-INT|-0] <pid>";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length == 2 && args[1] == "-l")
            {
                CommandIO.WriteLine("0 CHECK");
                CommandIO.WriteLine("2 INT");
                CommandIO.WriteLine("15 TERM");
                CommandIO.WriteLine("9 KILL (unsupported: no safe managed hard-abort primitive)");
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
                string signalText = args[1];
                if (!signalText.StartsWith("-", StringComparison.Ordinal) ||
                    !ProcessSignalNames.TryParse(signalText.Substring(1), out signal))
                {
                    PrintUsage();
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                pidText = args[2];
            }
            else
            {
                PrintUsage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!int.TryParse(pidText, out int pid) || pid <= 0)
            {
                CommandIO.WriteLine("kill: invalid PID: " + pidText);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            ProcessSignalResult result = ProcessManager.SendSignal(pid, signal);
            switch (result)
            {
                case ProcessSignalResult.Sent:
                    CommandIO.WriteLine("kill: sent SIG" + ProcessSignalNames.Name(signal) + " to PID " + pid);
                    CommandIO.LastCommandSuccess = true;
                    break;

                case ProcessSignalResult.Exists:
                    CommandIO.LastCommandSuccess = true;
                    break;

                case ProcessSignalResult.Unsupported:
                    CommandIO.WriteLine("kill: SIG" + ProcessSignalNames.Name(signal) +
                        " is not safely supported by the current managed runtime.");
                    CommandIO.LastCommandSuccess = false;
                    break;

                default:
                    CommandIO.WriteLine("kill: process " + pid + " does not exist.");
                    CommandIO.LastCommandSuccess = false;
                    break;
            }
        }

        private static void PrintUsage()
        {
            CommandIO.WriteLine("Usage: kill <pid>");
            CommandIO.WriteLine("       kill -TERM <pid>");
            CommandIO.WriteLine("       kill -INT <pid>");
            CommandIO.WriteLine("       kill -0 <pid>");
            CommandIO.WriteLine("       kill -l");
        }
    }
}
