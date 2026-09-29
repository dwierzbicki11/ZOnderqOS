using ZonderqOS.Hardware;

namespace ZonderqOS.Commands
{
    public sealed class CmdDevScan : ICommand
    {
        public string Name => "devscan";
        public string Description => "Rescan hardware inventory and rebuild driver bindings";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: devscan");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            bool available = HardwareDeviceManager.Rescan();
            if (!available)
            {
                CommandIO.WriteLine("devscan: PCI discovery backend unavailable.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine(
                "Hardware rescan complete. Devices: " + HardwareDeviceManager.DeviceCount +
                " | bound: " + HardwareDeviceManager.BoundCount +
                " | unbound: " + (HardwareDeviceManager.DeviceCount - HardwareDeviceManager.BoundCount));
            CommandIO.LastCommandSuccess = true;
        }
    }
}
