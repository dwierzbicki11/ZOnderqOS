using System.Collections.Generic;
using ZonderqOS.Hardware;

namespace ZonderqOS.Platform
{
    public static class PciSysfsProvider
    {
        public static bool TryDiscover(out List<DeviceDescriptor> devices)
        {
            // ARM64 PCI ECAM enumeration is not wired into the production HAL yet.
            // Keep sysfs honest: no fabricated devices.
            devices = new List<DeviceDescriptor>();
            return false;
        }
    }
}
