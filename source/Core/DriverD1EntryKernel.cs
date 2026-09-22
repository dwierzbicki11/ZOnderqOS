using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.Core;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Hardware;
using ZonderqOS.Platform.X64;

namespace ZonderqOS
{
    /// <summary>
    /// CI/runtime-only D1 proof kernel. It exercises the real x64 CF8/CFC backend,
    /// the shared topology walker and the system-facing device model in QEMU.
    /// It is compiled only when CosmosDriverStageD1 is explicitly enabled.
    /// </summary>
    public sealed class DriverD1EntryKernel : Sys.Kernel
    {
        private bool completed;

        protected override void BeforeRun()
        {
            Console.WriteLine("[DRIVER-D1] starting real PCI discovery");
            try
            {
                var service = new PciDiscoveryService(
                    new PciConfigDiscoverySource(new PciConfigIoAccessor()));
                List<DeviceDescriptor> devices = service.DiscoverDevices();

                if (devices.Count == 0)
                    throw new InvalidOperationException("PCI discovery returned no devices");

                bool sawHostBridge = false;
                for (int i = 0; i < devices.Count; i++)
                {
                    DeviceDescriptor device = devices[i];
                    Console.WriteLine("[DRIVER-D1] PCI " + device.Id +
                        " vendor=0x" + device.VendorId.ToString("X4") +
                        " device=0x" + device.DeviceId.ToString("X4") +
                        " class=" + device.ClassCode.ToString("X2") + ":" +
                        device.Subclass.ToString("X2") + ":" + device.ProgrammingInterface.ToString("X2"));
                    if (device.ClassCode == 0x06 && device.Subclass == 0x00)
                        sawHostBridge = true;
                }

                if (!sawHostBridge)
                    throw new InvalidOperationException("PCI host bridge was not discovered");

                Console.WriteLine("[DRIVER-D1] PASS real PCI discovery count=" + devices.Count);
                completed = true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DRIVER-D1] FAIL " + ex.GetType().Name + ": " + ex.Message);
                completed = true;
            }
        }

        protected override void Run()
        {
            if (completed)
                Thread.Sleep(1000);
        }
    }
}
