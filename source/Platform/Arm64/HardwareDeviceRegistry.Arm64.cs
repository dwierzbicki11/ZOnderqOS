using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    public static partial class HardwareDeviceRegistry
    {
        static partial void PlatformDiscoverPci(List<DeviceDescriptor> destination)
        {
            // ARM64 PCIe enumeration requires the ECAM/ACPI backend. Keep the
            // architecture contract real and explicit instead of emulating x86 CF8/CFC.
        }
    }
}
