using System;
using System.Collections.Generic;
using System.IO;

using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.FileSystem;

namespace ZonderqOS.GUI
{
    public static class StorageMountManager
    {
        // This cache is only a fast fallback. The VFS mount table remains the source of truth.
        private static readonly Dictionary<int, string> mountedPartitions = new Dictionary<int, string>();

        public static bool IsMounted(int partitionIndex)
        {
            return !string.IsNullOrEmpty(GetMountPoint(partitionIndex));
        }

        public static string GetMountPoint(int partitionIndex)
        {
            if (partitionIndex < 0)
                return null;

            string actualMountPoint = FindMountPointInVfs(partitionIndex);
            if (!string.IsNullOrEmpty(actualMountPoint))
            {
                mountedPartitions[partitionIndex] = actualMountPoint;
                return actualMountPoint;
            }

            mountedPartitions.Remove(partitionIndex);
            return null;
        }

        public static bool TryMount(int partitionIndex, out string mountPoint, out string error)
        {
            // Do not double-mount the writable root or resolve VFS sources
            // through mutable partition indices while USB hotplug is active.
            // The selected data volume is already mounted at / by Disk.
            mountPoint = null;
            error = "Manual mounts disabled in Cosmos 3.0.94; the approved data root is already mounted.";
            NotifyFailure("Montowanie zablokowane", error);
            return false;
        }

        public static bool TryUnmount(int partitionIndex, out string error)
        {
            error = null;

            try
            {
                string mountPoint = GetMountPoint(partitionIndex);
                if (mountPoint == "/")
                {
                    error = "Active system root cannot be unmounted";
                    NotifyFailure("Odmontowanie zablokowane", error);
                    return false;
                }
                if (string.IsNullOrEmpty(mountPoint))
                {
                    mountedPartitions.Remove(partitionIndex);
                    return true;
                }

                if (!VfsManager.TryUnmount(mountPoint))
                {
                    error = "Unmount failed: volume may still be in use";
                    NotifyFailure("Odmontowanie nie powiodlo sie", error);
                    return false;
                }

                mountedPartitions.Remove(partitionIndex);
                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Storage,
                    "MAGAZYN",
                    "Wolumin odmontowany",
                    mountPoint);
                return true;
            }
            catch (Exception ex)
            {
                error = "Unmount exception: " + ex.Message;
                NotifyFailure("Blad odmontowania woluminu", error);
                return false;
            }
        }

        private static void NotifyFailure(string title, string message)
        {
            global::ZonderqOS.NotificationService.Post(
                global::ZonderqOS.NotificationKind.Warning,
                "MAGAZYN",
                title,
                message);
        }

        private static string FindMountPointInVfs(int partitionIndex)
        {
            string source = partitionIndex.ToString();

            try
            {
                foreach (VfsMount mount in VfsManager.Mounts)
                {
                    if (mount.Source == source)
                        return mount.MountPoint;
                }
            }
            catch
            {
                // Keep the GUI usable even if mount-table inspection fails.
            }

            return null;
        }

        private static string FindFreeMountPoint(string baseMountPoint)
        {
            for (int suffix = 0; suffix < 32; suffix++)
            {
                string candidate = suffix == 0 ? baseMountPoint : baseMountPoint + "_" + (suffix + 1);

                if (IsMountPointInUse(candidate))
                    continue;

                if (File.Exists(candidate))
                    continue;

                if (!Directory.Exists(candidate))
                    return candidate;

                // Older GUI builds created the mount directory before TryMount. Remove only
                // an empty stale directory; never delete user data to make room for a mount.
                try
                {
                    string[] files = Directory.GetFiles(candidate);
                    string[] directories = Directory.GetDirectories(candidate);
                    if (files.Length == 0 && directories.Length == 0)
                    {
                        Directory.Delete(candidate);
                        return candidate;
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static bool IsMountPointInUse(string mountPoint)
        {
            try
            {
                foreach (VfsMount mount in VfsManager.Mounts)
                {
                    if (mount.MountPoint == mountPoint)
                        return true;
                }
            }
            catch
            {
            }

            return false;
        }
    }
}
