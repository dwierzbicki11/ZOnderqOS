using System;
using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Commands
{
    public sealed class CmdLspci : ICommand
    {
        public string Name => "lspci";
        public string Description => "List PCI/PCIe functions discovered by the kernel hardware registry";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                CommandIO.WriteLine("Usage: lspci");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!HardwareDeviceRegistry.IsInitialized)
                HardwareDeviceRegistry.Initialize();

            if (!string.IsNullOrEmpty(HardwareDeviceRegistry.LastError))
            {
                CommandIO.WriteLine("lspci: PCI discovery degraded: " + HardwareDeviceRegistry.LastError);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            var devices = new List<DeviceDescriptor>();
            HardwareDeviceRegistry.CopyPciDevices(devices);

            if (devices.Count == 0)
            {
                CommandIO.WriteLine("No PCI/PCIe functions registered on this platform.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            for (int i = 0; i < devices.Count; i++)
            {
                DeviceDescriptor device = devices[i];
                CommandIO.WriteLine(
                    device.Id.Address + " " +
                    device.VendorId.ToString("X4") + ":" +
                    device.DeviceId.ToString("X4") + " " +
                    ClassName(device.ClassCode) + " [" +
                    device.ClassCode.ToString("X2") + ":" +
                    device.Subclass.ToString("X2") + ":" +
                    device.ProgrammingInterface.ToString("X2") + "]");
            }

            CommandIO.WriteLine("PCI functions: " + devices.Count);
            CommandIO.LastCommandSuccess = true;
        }

        private static string ClassName(byte classCode)
        {
            switch (classCode)
            {
                case 0x01: return "Mass storage";
                case 0x02: return "Network";
                case 0x03: return "Display";
                case 0x04: return "Multimedia";
                case 0x05: return "Memory";
                case 0x06: return "Bridge";
                case 0x07: return "Communication";
                case 0x08: return "System peripheral";
                case 0x0C: return "Serial bus";
                default: return "PCI device";
            }
        }
    }
}
