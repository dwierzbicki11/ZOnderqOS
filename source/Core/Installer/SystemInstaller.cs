using System;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.SystemCore.Installer
{
    public static class SystemInstaller
    {
        public static int TargetCount
        {
            get
            {
                try
                {
                    return Math.Max(0, StorageManager.DeviceCount);
                }
                catch
                {
                    return 0;
                }
            }
        }

        public static bool TryGetTargetPlan(
            int diskId,
            out string name,
            out InstallerLayoutPlan plan,
            out string error)
        {
            name = string.Empty;
            plan = null;
            error = string.Empty;

            try
            {
                int count = StorageManager.DeviceCount;
                if (diskId < 0 || diskId >= count)
                {
                    error = "Disk index out of range.";
                    return false;
                }

                var device = StorageManager.GetDevice(diskId);
                if (device == null)
                {
                    error = "Storage device is unavailable.";
                    return false;
                }

                name = string.IsNullOrWhiteSpace(device.Name)
                    ? "disk" + diskId
                    : device.Name;

                plan = InstallerLayoutPlan.Create(
                    device.BlockCount,
                    (uint)device.BlockSize);

                return true;
            }
            catch (Exception ex)
            {
                error = "Storage query failed: " + ex.Message;
                return false;
            }
        }

        public static bool CanApplyDestructivePlan(out string reason)
        {
            // Fail closed until the VFS/storage layer exposes a reliable mapping
            // from the mounted root filesystem back to its physical block device.
            // Without that identity the installer cannot prove that a selected
            // target is not the disk currently running the OS.
            reason =
                "Destructive install is disabled: current root backing disk identity is unresolved.";
            return false;
        }
    }
}
