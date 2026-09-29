using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Commands
{
    public sealed class CmdDrivers : ICommand
    {
        public string Name => "drivers";
        public string Description => "Show detected hardware and driver bindings";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: drivers");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!HardwareDeviceManager.DiscoveryAvailable)
            {
                CommandIO.WriteLine("drivers: hardware discovery backend unavailable.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<DeviceDescriptor> devices = HardwareDeviceManager.GetDevicesSnapshot();
            CommandIO.WriteLine("BUS ADDRESS  VENDOR DEVICE CLASS  DRIVER");
            CommandIO.WriteLine("-------------------------------------------");

            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                string driverName;
                bool bound = HardwareDeviceManager.TryGetDriver(d.Id, out driverName);

                CommandIO.WriteLine(
                    d.Id.Bus.PadRight(4) + " " +
                    d.Id.Address.PadRight(8) + " " +
                    d.VendorId.ToString("X4") + "   " +
                    d.DeviceId.ToString("X4") + "   " +
                    d.ClassCode.ToString("X2") + ":" + d.Subclass.ToString("X2") + "  " +
                    (bound ? driverName : "unbound"));
            }

            CommandIO.WriteLine(
                "Devices: " + devices.Count +
                " | registered drivers: " + HardwareDeviceManager.DriverCount +
                " | bound: " + HardwareDeviceManager.BoundCount +
                " | unbound: " + (devices.Count - HardwareDeviceManager.BoundCount));
            CommandIO.LastCommandSuccess = true;
        }
    }
}
