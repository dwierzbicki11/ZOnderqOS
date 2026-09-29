using System;
using System.Collections.Generic;

namespace ZonderqOS.Hardware
{
    /// <summary>
    /// Single system-owned snapshot of discovered hardware. Bus-specific discovery
    /// stays behind platform partials; consumers only see stable descriptors.
    /// </summary>
    public static partial class HardwareDeviceRegistry
    {
        private static readonly object Gate = new object();
        private static readonly List<DeviceDescriptor> PciDevices = new List<DeviceDescriptor>();
        private static bool initialized;
        private static string lastError = string.Empty;

        public static bool IsInitialized
        {
            get { lock (Gate) return initialized; }
        }

        public static int PciDeviceCount
        {
            get { lock (Gate) return PciDevices.Count; }
        }

        public static string LastError
        {
            get { lock (Gate) return lastError; }
        }

        public static void Initialize()
        {
            var discovered = new List<DeviceDescriptor>();
            string error = string.Empty;

            try
            {
                PlatformDiscoverPci(discovered);
            }
            catch (Exception ex)
            {
                error = ex.Message ?? "PCI discovery failed";
            }

            lock (Gate)
            {
                PciDevices.Clear();
                for (int i = 0; i < discovered.Count; i++)
                    PciDevices.Add(discovered[i]);

                lastError = error;
                initialized = true;
            }
        }

        public static void CopyPciDevices(List<DeviceDescriptor> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (Gate)
            {
                destination.Clear();
                for (int i = 0; i < PciDevices.Count; i++)
                    destination.Add(PciDevices[i]);
            }
        }

        public static bool TryGetPciDevice(string address, out DeviceDescriptor device)
        {
            device = null;
            if (string.IsNullOrEmpty(address))
                return false;

            lock (Gate)
            {
                for (int i = 0; i < PciDevices.Count; i++)
                {
                    DeviceDescriptor candidate = PciDevices[i];
                    if (candidate != null &&
                        string.Equals(candidate.Id.Address, address, StringComparison.OrdinalIgnoreCase))
                    {
                        device = candidate;
                        return true;
                    }
                }
            }

            return false;
        }

        static partial void PlatformDiscoverPci(List<DeviceDescriptor> destination);
    }
}
