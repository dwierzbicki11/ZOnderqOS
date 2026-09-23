using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using Sys = Cosmos.Kernel.System;
using ZonderqOS.Hardware;
using ZonderqOS.Platform.X64;

namespace ZonderqOS
{
    /// <summary>D2.1 runtime proof: real PCI discovery plus typed AHCI/NVMe binding. No MMIO.</summary>
    public sealed class DriverD2EntryKernel : Sys.Kernel
    {
        private bool completed;

        protected override void BeforeRun()
        {
            Log.WriteString("[DRIVER-D2.1] starting storage controller discovery/binding\n");
            try
            {
                var discovery = new PciDiscoveryService(new PciConfigDiscoverySource(new PciConfigIoAccessor()));
                List<DeviceDescriptor> devices = discovery.DiscoverDevices();
                var ahci = new AhciControllerDriver();
                var nvme = new NvmeControllerDriver();
                int ahciBound = 0;
                int nvmeBound = 0;

                for (int i = 0; i < devices.Count; i++)
                {
                    DeviceDescriptor device = devices[i];
                    if (ahci.Bind(device))
                    {
                        ahciBound++;
                        Log.WriteString("[DRIVER-D2.1] AHCI bound ");
                        Log.WriteString(device.Id.ToString());
                        Log.WriteString("\n");
                    }
                    if (nvme.Bind(device))
                    {
                        nvmeBound++;
                        Log.WriteString("[DRIVER-D2.1] NVME bound ");
                        Log.WriteString(device.Id.ToString());
                        Log.WriteString("\n");
                    }
                }

                if (ahciBound < 1) throw new InvalidOperationException("no AHCI controller bound");
                if (nvmeBound < 1) throw new InvalidOperationException("no NVMe controller bound");

                Log.WriteString("[DRIVER-D2.1] PASS ahci=");
                Log.WriteNumber(ahciBound);
                Log.WriteString(" nvme=");
                Log.WriteNumber(nvmeBound);
                Log.WriteString("\n");
            }
            catch (Exception ex)
            {
                Log.WriteString("[DRIVER-D2.1] FAIL ");
                Log.WriteString(ex.GetType().Name);
                Log.WriteString(": ");
                Log.WriteString(ex.Message);
                Log.WriteString("\n");
            }
            completed = true;
        }

        protected override void Run()
        {
            if (completed) Thread.Sleep(1000);
        }
    }
}
