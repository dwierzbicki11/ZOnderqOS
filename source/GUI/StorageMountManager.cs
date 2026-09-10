using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

namespace ZonderqOS.GUI
{
    public static class StorageMountManager
    {
        // This cache is only a fast fallback. The VFS mount table remains the source of truth.
        private static readonly Dictionary<int, string> mountedPartitions = new Dictionary<int, string>();

        static StorageMountManager()
        {
            mountedPartitions[0] = "/";
        }

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

            if (partitionIndex == 0)
                return "/";

            mountedPartitions.Remove(partitionIndex);
            return null;
        }

        public static bool TryMount(int partitionIndex, out string mountPoint, out string error)
        {
            mountPoint = null;
            error = null;

            try
            {
                int partitionCount = StorageManager.Partitions.Count;
                if (partitionIndex < 0 || partitionIndex >= partitionCount)
                {
                    error = "Partition does not exist";
                    return false;
                }

                if (partitionIndex == 0)
                {
                    mountPoint = GetMountPoint(0) ?? "/";
                    return true;
                }

                string existingMountPoint = FindMountPointInVfs(partitionIndex);
                if (!string.IsNullOrEmpty(existingMountPoint))
                {
                    mountPoint = existingMountPoint;
                    mountedPartitions[partitionIndex] = existingMountPoint;
                    return true;
                }

                // /mnt is a normal directory on the system volume. The mount target itself,
                // however, must not be pre-created. Cosmos Gen3 VFS creates/owns the mount point.
                if (!Directory.Exists("/mnt"))
                    Directory.CreateDirectory("/mnt");

                string baseMountPoint = "/mnt/volume" + partitionIndex;
                mountPoint = FindFreeMountPoint(baseMountPoint);
                if (string.IsNullOrEmpty(mountPoint))
                {
                    error = "No free mount point under /mnt";
                    return false;
                }

                VfsManager.VfsMount mount;
                if (!VfsManager.TryMount("fat", partitionIndex.ToString(), MountFlags.None, mountPoint, out mount))
                {
                    mountPoint = null;
                    error = "Mount failed: volume must contain a Cosmos FAT12/16/32 filesystem";
                    return false;
                }

                mountPoint = mount.MountPoint;
                mountedPartitions[partitionIndex] = mountPoint;
                return true;
            }
            catch (Exception ex)
            {
                mountPoint = null;
                error = "Mount exception: " + ex.Message;
                return false;
            }
        }

        public static bool TryUnmount(int partitionIndex, out string error)
        {
            error = null;

            try
            {
                if (partitionIndex <= 0)
                {
                    error = "System volume cannot be unmounted";
                    return false;
                }

                string mountPoint = GetMountPoint(partitionIndex);
                if (string.IsNullOrEmpty(mountPoint))
                {
                    mountedPartitions.Remove(partitionIndex);
                    return true;
                }

                if (!VfsManager.TryUnmount(mountPoint))
                {
                    error = "Unmount failed: volume may still be in use";
                    return false;
                }

                mountedPartitions.Remove(partitionIndex);
                return true;
            }
            catch (Exception ex)
            {
                error = "Unmount exception: " + ex.Message;
                return false;
            }
        }

        private static string FindMountPointInVfs(int partitionIndex)
        {
            string source = partitionIndex.ToString();

            try
            {
                foreach (VfsManager.VfsMount mount in VfsManager.Mounts)
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
                foreach (VfsManager.VfsMount mount in VfsManager.Mounts)
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
