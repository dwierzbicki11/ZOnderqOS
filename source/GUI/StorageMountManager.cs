using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;

namespace ZonderqOS.GUI
{
    public static class StorageMountManager
    {
        private static readonly Dictionary<int, string> mountedPartitions = new Dictionary<int, string>();

        static StorageMountManager()
        {
            mountedPartitions[0] = "/";
        }

        public static bool IsMounted(int partitionIndex)
        {
            if (partitionIndex == 0)
                return true;

            return mountedPartitions.ContainsKey(partitionIndex);
        }

        public static string GetMountPoint(int partitionIndex)
        {
            if (partitionIndex == 0)
                return "/";

            string mountPoint;
            return mountedPartitions.TryGetValue(partitionIndex, out mountPoint) ? mountPoint : null;
        }

        public static bool TryMount(int partitionIndex, out string mountPoint, out string error)
        {
            mountPoint = null;
            error = null;

            try
            {
                if (partitionIndex <= 0 || partitionIndex >= StorageManager.Partitions.Count)
                {
                    error = "Invalid partition";
                    return false;
                }

                if (mountedPartitions.TryGetValue(partitionIndex, out mountPoint))
                    return true;

                Directory.CreateDirectory("/mnt");
                mountPoint = "/mnt/volume" + partitionIndex;
                if (!Directory.Exists(mountPoint))
                    Directory.CreateDirectory(mountPoint);

                if (!VfsManager.TryMount("fat", partitionIndex.ToString(), MountFlags.None, mountPoint, out var mount))
                {
                    mountPoint = null;
                    error = "Mount failed";
                    return false;
                }

                mountedPartitions[partitionIndex] = mountPoint;
                return true;
            }
            catch (Exception ex)
            {
                mountPoint = null;
                error = ex.Message;
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

                string mountPoint;
                if (!mountedPartitions.TryGetValue(partitionIndex, out mountPoint))
                    return true;

                if (!VfsManager.TryUnmount(mountPoint))
                {
                    error = "Unmount failed";
                    return false;
                }

                mountedPartitions.Remove(partitionIndex);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
