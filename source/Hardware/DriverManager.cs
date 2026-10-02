using System;
using System.Collections.Generic;
using ZonderqOS.Platform;

namespace ZonderqOS.Hardware
{
    public static class DriverManager
    {
        private static readonly object Sync = new object();
        private static readonly DriverRegistry Registry = new DriverRegistry();
        private static readonly List<DeviceDescriptor> Devices = new List<DeviceDescriptor>();

        private static bool initialized;
        private static bool pciDiscoveryAvailable;

        public static bool Initialized
        {
            get { lock (Sync) return initialized; }
        }

        public static bool PciDiscoveryAvailable
        {
            get { lock (Sync) return pciDiscoveryAvailable; }
        }

        public static int DeviceCount
        {
            get { lock (Sync) return Devices.Count; }
        }

        public static int BoundDeviceCount
        {
            get
            {
                lock (Sync)
                {
                    int count = 0;
                    for (int i = 0; i < Devices.Count; i++)
                    {
                        if (Registry.TryGetBinding(Devices[i].Id, out _))
                            count++;
                    }

                    return count;
                }
            }
        }

        public static void Initialize()
        {
            lock (Sync)
            {
                if (initialized)
                    return;

                Devices.Clear();

                List<DeviceDescriptor> discovered;
                pciDiscoveryAvailable = PciSysfsProvider.TryDiscover(out discovered);

                if (pciDiscoveryAvailable && discovered != null)
                {
                    for (int i = 0; i < discovered.Count; i++)
                    {
                        DeviceDescriptor device = discovered[i];
                        if (device == null)
                            continue;

                        Devices.Add(device);
                        Registry.TryBind(device, out _);
                    }
                }

                initialized = true;
            }
        }

        public static void Register(IDeviceDriver driver)
        {
            if (driver == null)
                throw new ArgumentNullException(nameof(driver));

            lock (Sync)
            {
                Registry.Register(driver);

                if (!initialized)
                    return;

                for (int i = 0; i < Devices.Count; i++)
                {
                    DeviceDescriptor device = Devices[i];
                    if (!Registry.TryGetBinding(device.Id, out _))
                        Registry.TryBind(device, out _);
                }
            }
        }

        public static void FillDevices(List<DeviceDescriptor> target)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            lock (Sync)
            {
                target.Clear();
                for (int i = 0; i < Devices.Count; i++)
                    target.Add(Devices[i]);
            }
        }

        public static bool TryGetDevice(string address, out DeviceDescriptor device)
        {
            lock (Sync)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    DeviceDescriptor candidate = Devices[i];
                    if (candidate != null &&
                        string.Equals(candidate.Id.Address, address, StringComparison.OrdinalIgnoreCase))
                    {
                        device = candidate;
                        return true;
                    }
                }
            }

            device = null;
            return false;
        }

        public static bool TryGetBinding(DeviceId id, out string driverName)
        {
            lock (Sync)
            {
                if (Registry.TryGetBinding(id, out IDeviceDriver driver) && driver != null)
                {
                    driverName = driver.Name;
                    return true;
                }
            }

            driverName = null;
            return false;
        }
    }
}
