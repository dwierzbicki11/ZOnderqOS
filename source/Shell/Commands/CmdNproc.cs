using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.Commands
{
    public sealed class CmdNproc : ICommand
    {
        public string Name => "nproc";
        public string Description => "Print number of logical CPUs available to the scheduler";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: nproc");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine(SchedulerInfo.CpuCount.ToString());
            CommandIO.LastCommandSuccess = true;
        }
    }
}
