using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    public static partial class HardwareDeviceRegistry
    {
        static partial void PlatformDiscoverPci(List<DeviceDescriptor> destination)
        {
            var source = new PciConfigDiscoverySource(new global::ZonderqOS.Platform.X64.PciConfigIoAccessor());
            List<DeviceDescriptor> devices = new PciDiscoveryService(source).DiscoverDevices();
            for (int i = 0; i < devices.Count; i++)
                destination.Add(devices[i]);
        }
    }
}
