using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdInputScan : ICommand
    {
        public string Name => "inputscan";
        public string Description => "Scan PCI/PCIe input and USB host controllers; show HID detection limits";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: inputscan");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.LastCommandSuccess = InputBusDiagnostics.PrintReport(true);
        }
    }
}
