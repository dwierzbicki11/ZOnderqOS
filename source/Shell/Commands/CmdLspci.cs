using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Commands
{
    public sealed class CmdLspci : ICommand
    {
        public string Name => "lspci";
        public string Description => "List PCI/PCIe devices discovered by the production hardware model";

        public void Execute(string[] args, ref string currentPath)
        {
            bool verbose = false;
            bool showDriver = false;

            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (arg == "-v" || arg == "--verbose")
                    verbose = true;
                else if (arg == "-k" || arg == "--kernel-driver")
                    showDriver = true;
                else
                {
                    CommandIO.WriteLine("Usage: lspci [-v] [-k]");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            if (!HardwareDeviceManager.DiscoveryAvailable)
            {
                CommandIO.WriteLine("lspci: PCI discovery is not available on this architecture/backend.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            List<DeviceDescriptor> devices = HardwareDeviceManager.GetDevicesSnapshot();

            if (devices.Count == 0)
            {
                CommandIO.WriteLine("lspci: no PCI devices discovered.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor d = devices[i];
                CommandIO.WriteLine(
                    d.Id.Address + "  " +
                    "vendor=" + d.VendorId.ToString("X4") + " " +
                    "device=" + d.DeviceId.ToString("X4") + " " +
                    "class=" + d.ClassCode.ToString("X2") + ":" +
                    d.Subclass.ToString("X2") + " " +
                    ClassName(d.ClassCode));

                if (verbose)
                {
                    CommandIO.WriteLine("        prog-if=0x" + d.ProgrammingInterface.ToString("X2"));
                    CommandIO.WriteLine("        modalias=" + BuildModalias(d));
                }

                if (showDriver)
                {
                    string driverName;
                    bool bound = HardwareDeviceManager.TryGetDriver(d.Id, out driverName);
                    CommandIO.WriteLine("        kernel driver: " + (bound ? driverName : "unbound"));
                }
            }

            int boundCount = HardwareDeviceManager.BoundCount;
            CommandIO.WriteLine(
                "PCI devices: " + devices.Count +
                " | bound: " + boundCount +
                " | unbound: " + (devices.Count - boundCount));
            CommandIO.LastCommandSuccess = true;
        }

        private static string ClassName(byte classCode)
        {
            switch (classCode)
            {
                case 0x01: return "Mass storage";
                case 0x02: return "Network controller";
                case 0x03: return "Display controller";
                case 0x04: return "Multimedia";
                case 0x05: return "Memory controller";
                case 0x06: return "Bridge";
                case 0x07: return "Communication";
                case 0x08: return "System peripheral";
                case 0x09: return "Input controller";
                case 0x0C: return "Serial bus";
                default: return "Other";
            }
        }

        private static string BuildModalias(DeviceDescriptor device)
        {
            return "pci:v0000" + device.VendorId.ToString("X4") +
                   "d0000" + device.DeviceId.ToString("X4") +
                   "sv*sd*bc" + device.ClassCode.ToString("X2") +
                   "sc" + device.Subclass.ToString("X2") +
                   "i" + device.ProgrammingInterface.ToString("X2");
        }
    }
}
