using System;
using System.Collections.Generic;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
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
            // The D1 probe deliberately boots with graphics disabled. System.Console
            // therefore has no KernelConsole backing and must not be used here.
            // Cosmos' public Diagnostics.Log writes directly to the platform serial
            // device, which is exactly what the QEMU gate captures.
            Log.WriteString("[DRIVER-D1] starting real PCI discovery\n");
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
                    Log.WriteString("[DRIVER-D1] PCI ");
                    Log.WriteString(device.Id.ToString());
                    Log.WriteString(" vendor=0x");
                    Log.WriteString(device.VendorId.ToString("X4"));
                    Log.WriteString(" device=0x");
                    Log.WriteString(device.DeviceId.ToString("X4"));
                    Log.WriteString(" class=");
                    Log.WriteString(device.ClassCode.ToString("X2"));
                    Log.WriteString(":");
                    Log.WriteString(device.Subclass.ToString("X2"));
                    Log.WriteString(":");
                    Log.WriteString(device.ProgrammingInterface.ToString("X2"));
                    Log.WriteString("\n");
                    if (device.ClassCode == 0x06 && device.Subclass == 0x00)
                        sawHostBridge = true;
                }

                if (!sawHostBridge)
                    throw new InvalidOperationException("PCI host bridge was not discovered");

                Log.WriteString("[DRIVER-D1] PASS real PCI discovery count=");
                Log.WriteNumber(devices.Count);
                Log.WriteString("\n");
                completed = true;
            }
            catch (Exception ex)
            {
                Log.WriteString("[DRIVER-D1] FAIL ");
                Log.WriteString(ex.GetType().Name);
                Log.WriteString(": ");
                Log.WriteString(ex.Message);
                Log.WriteString("\n");
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
