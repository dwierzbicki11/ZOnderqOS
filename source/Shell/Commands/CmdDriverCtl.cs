using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Commands
{
    public sealed class CmdDriverCtl : ICommand
    {
        public string Name => "driverctl";
        public string Description => "Show discovered devices and kernel driver bindings";

        public void Execute(string[] args, ref string currentPath)
        {
            DriverManager.Initialize();

            if (args.Length == 1 || (args.Length == 2 && args[1] == "status"))
            {
                CommandIO.WriteLine("Driver manager: initialized");
                CommandIO.WriteLine("PCI discovery: " +
                    (DriverManager.PciDiscoveryAvailable ? "available" : "unavailable"));
                CommandIO.WriteLine("Devices: " + DriverManager.DeviceCount);
                CommandIO.WriteLine("Bound: " + DriverManager.BoundDeviceCount);
                CommandIO.WriteLine("Unbound: " +
                    (DriverManager.DeviceCount - DriverManager.BoundDeviceCount));
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (args.Length == 2 && args[1] == "list")
            {
                var devices = new List<DeviceDescriptor>();
                DriverManager.FillDevices(devices);

                if (devices.Count == 0)
                {
                    CommandIO.WriteLine("driverctl: no devices discovered.");
                    CommandIO.LastCommandSuccess = DriverManager.PciDiscoveryAvailable;
                    return;
                }

                CommandIO.WriteLine("ADDRESS   CLASS  DRIVER");
                for (int i = 0; i < devices.Count; i++)
                {
                    DeviceDescriptor device = devices[i];
                    string driverName;
                    bool bound = DriverManager.TryGetBinding(device.Id, out driverName);

                    CommandIO.WriteLine(
                        device.Id.Address.PadRight(10) +
                        (device.ClassCode.ToString("X2") + ":" +
                         device.Subclass.ToString("X2")).PadRight(7) +
                        (bound ? driverName : "unbound"));
                }

                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine("Usage: driverctl [status|list]");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
