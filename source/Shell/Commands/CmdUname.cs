using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdUname : ICommand
    {
        public string Name => "uname";
        public string Description => "Show kernel/system identity";

        public void Execute(string[] args, ref string currentPath)
        {
            bool all = args.Length > 1 && args[1] == "-a";
            if (args.Length > 1 && !all)
            {
                WriteMessage.WriteError("Usage: uname [-a]", "SYS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!all)
            {
                CommandIO.WriteLine("ZOnderqOS");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            HardwareSnapshot info = HardwareSnapshot.Capture();
            string host = EnvironmentManager.Get("HOSTNAME");
            if (string.IsNullOrEmpty(host))
                host = "ZOnderqOS";

            CommandIO.WriteLine(
                "ZOnderqOS " +
                host +
                " Gen3 " +
                info.Architecture +
                " " +
                info.CpuBrand);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
