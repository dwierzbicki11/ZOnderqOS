using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Platform
{
    public static class PciSysfsProvider
    {
        public static bool TryDiscover(out List<DeviceDescriptor> devices)
        {
            try
            {
                var source = new PciConfigDiscoverySource(new X64.PciConfigIoAccessor());
                devices = new PciDiscoveryService(source).DiscoverDevices();
                return true;
            }
            catch
            {
                devices = new List<DeviceDescriptor>();
                return false;
            }
        }
    }
}
