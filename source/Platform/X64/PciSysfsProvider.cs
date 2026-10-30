using System.Collections.Generic;
using System.Globalization;
using ZonderqOS.Hardware;

namespace ZonderqOS.Platform
{
    public static class PciSysfsProvider
    {
        public static bool TryReadRealtekBars(DeviceDescriptor device, out List<PciBarDescriptor> bars)
        {
            bars = new List<PciBarDescriptor>();
            if (device == null || device.VendorId != 0x10EC || device.DeviceId != 0x8136 ||
                device.Id.Bus != "pci") return false;
            string address = device.Id.Address;
            if (address == null || address.Length != 7 || address[2] != ':' || address[5] != '.')
                return false;
            byte bus, slot, function;
            if (!byte.TryParse(address.Substring(0, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out bus) ||
                !byte.TryParse(address.Substring(3, 2), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out slot) ||
                !byte.TryParse(address.Substring(6, 1), NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out function)) return false;
            try
            {
                return PciBarInventory.TryReadRealtek810xE(new X64.PciConfigIoAccessor(),
                    bus, slot, function, out bars);
            }
            catch
            {
                bars.Clear();
                return false;
            }
        }

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
