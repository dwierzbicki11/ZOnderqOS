using System;
using System.Collections.Generic;
using ZonderqOS.Platform;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Central hardware inventory and driver-binding service.
    /// PCI discovery happens once during boot; shell/sysfs consume the cached
    /// production inventory instead of rescanning configuration space.
    /// </summary>
    public static class HardwareDeviceManager
    {
        private static readonly object Sync = new object();
        private static readonly List<DeviceDescriptor> Devices = new List<DeviceDescriptor>();
        private static readonly List<IDeviceDriver> Drivers = new List<IDeviceDriver>();

        private static DriverRegistry registry = new DriverRegistry();
        private static bool initialized;
        private static bool discoveryAvailable;

        public static bool DiscoveryAvailable
        {
            get
            {
                lock (Sync)
                    return discoveryAvailable;
            }
        }

        public static int DeviceCount
        {
            get
            {
                lock (Sync)
                    return Devices.Count;
            }
        }

        public static int BoundCount
        {
            get
            {
                lock (Sync)
                {
                    int bound = 0;
                    for (int i = 0; i < Devices.Count; i++)
                    {
                        IDeviceDriver driver;
                        if (registry.TryGetBinding(Devices[i].Id, out driver))
                            bound++;
                    }
                    return bound;
                }
            }
        }

        public static void Initialize()
        {
            int discoveredCount;
            bool available;

            lock (Sync)
            {
                if (initialized)
                    return;

                Devices.Clear();

                List<DeviceDescriptor> discovered;
                available = PciSysfsProvider.TryDiscover(out discovered);
                if (available && discovered != null)
                    Devices.AddRange(discovered);

                discoveryAvailable = available;
                initialized = true;
                RebuildBindingsLocked();
                discoveredCount = Devices.Count;
            }

            SystemLogger.Log(
                SystemLogLevel.Info,
                "DEVICE",
                available
                    ? "Hardware inventory initialized; PCI devices=" + discoveredCount + "."
                    : "Hardware inventory initialized without a production PCI discovery backend.");
        }

        public static void RegisterDriver(IDeviceDriver driver)
        {
            if (driver == null)
                throw new ArgumentNullException(nameof(driver));

            lock (Sync)
            {
                Drivers.Add(driver);
                registry.Register(driver);

                if (!initialized)
                    return;

                for (int i = 0; i < Devices.Count; i++)
                {
                    IDeviceDriver existing;
                    if (!registry.TryGetBinding(Devices[i].Id, out existing))
                    {
                        IDeviceDriver bound;
                        registry.TryBind(Devices[i], out bound);
                    }
                }
            }
        }

        public static List<DeviceDescriptor> GetDevicesSnapshot()
        {
            lock (Sync)
                return new List<DeviceDescriptor>(Devices);
        }

        public static DeviceDescriptor Find(DeviceId id)
        {
            lock (Sync)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    if (Devices[i].Id.Equals(id))
                        return Devices[i];
                }

                return null;
            }
        }

        public static DeviceDescriptor FindPci(string address)
        {
            if (string.IsNullOrEmpty(address))
                return null;

            lock (Sync)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    DeviceDescriptor device = Devices[i];
                    if (device.Id.Bus == "pci" &&
                        string.Equals(device.Id.Address, address, StringComparison.OrdinalIgnoreCase))
                        return device;
                }

                return null;
            }
        }

        public static bool TryGetDriver(DeviceId id, out string driverName)
        {
            lock (Sync)
            {
                IDeviceDriver driver;
                if (registry.TryGetBinding(id, out driver) && driver != null)
                {
                    driverName = driver.Name ?? "unnamed";
                    return true;
                }

                driverName = "unbound";
                return false;
            }
        }

        private static void RebuildBindingsLocked()
        {
            registry = new DriverRegistry();

            for (int i = 0; i < Drivers.Count; i++)
                registry.Register(Drivers[i]);

            for (int i = 0; i < Devices.Count; i++)
            {
                IDeviceDriver ignored;
                registry.TryBind(Devices[i], out ignored);
            }
        }
    }
}
