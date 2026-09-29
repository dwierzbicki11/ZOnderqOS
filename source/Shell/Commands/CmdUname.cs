using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdUname : ICommand
    {
        public string Name => "uname";
        public string Description => "Show kernel and architecture information";

        public void Execute(string[] args, ref string currentPath)
        {
            HardwareSnapshot h = HardwareSnapshot.Capture();

            if (args.Length == 1)
            {
                CommandIO.WriteLine("ZOnderqOS");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (args.Length != 2)
            {
                Usage();
                CommandIO.LastCommandSuccess = false;
                return;
            }

            switch (args[1])
            {
                case "-s":
                case "--kernel-name":
                    CommandIO.WriteLine("ZOnderqOS");
                    break;
                case "-r":
                case "--kernel-release":
                    CommandIO.WriteLine("Gen3");
                    break;
                case "-m":
                case "--machine":
                    CommandIO.WriteLine(h.Architecture);
                    break;
                case "-a":
                case "--all":
                    CommandIO.WriteLine("ZOnderqOS Gen3 " + h.Architecture + " CosmosKernel scheduler=" + h.SchedulerName);
                    break;
                default:
                    Usage();
                    CommandIO.LastCommandSuccess = false;
                    return;
            }

            CommandIO.LastCommandSuccess = true;
        }

        private static void Usage()
        {
            CommandIO.WriteLine("Usage: uname [-a|-s|-r|-m]");
        }
    }
}
