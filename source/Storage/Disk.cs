using System;
using System.IO;
using Cosmos.Kernel.System.Vfs;
using Cosmos.Kernel.System.Filesystems.Fat;
using Cosmos.Kernel.HAL.Vfs;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS
{
    public static class Disk
    {
        private static FatFilesystemType fat;
        private static VfsManager.VfsMount _currentMount;

        public static void Initialize()
        {
            try
            {
                fat = new FatFilesystemType();
                VfsManager.RegisterFilesystem("fat", fat);

                if (VfsManager.TryMount("fat", "0", MountFlags.None, "/", out var mount))
                {
                    _currentMount = mount;
                    WriteMessage.WriteOK($"Mounted root filesystem at '/' (Source: {mount.Source})", "VFS");
                    CreateStandardHierarchy();
                }
                else
                {
                    WriteMessage.WriteError("Failed to mount root FAT partition.", "VFS");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Critical initialization exception: {ex.Message}", "VFS");
            }
        }

        private static void CreateStandardHierarchy()
        {
            string[] fhsDirectories = new string[]
            {
                "/bin", "/boot", "/cdrom", "/dev", "/etc", "/home", "/lib", "/lib64",
                "/media", "/mnt", "/opt", "/proc", "/root", "/run", "/sbin", "/srv",
                "/sys", "/tmp", "/usr", "/var", "/lost+found"
            };

            foreach (string dir in fhsDirectories)
            {
                try
                {
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError($"Failed to create FHS node {dir}: {ex.Message}", "FS");
                }
            }

            string logPath = @"/var/error_log.txt";
            if (!File.Exists(logPath))
            {
                try
                {
                    File.WriteAllText(logPath, "=== ZonderqOS System Log Initialized ===\n");
                }
                catch { }
            }
        }

        public static void Tree(string path, string indent = "")
        {
            try
            {
                string[] files = Directory.GetFiles(path);
                foreach (string file in files)
                    CommandIO.WriteLine($"{indent}--- {Path.GetFileName(file)}");

                string[] directories = Directory.GetDirectories(path);
                foreach (string dir in directories)
                {
                    CommandIO.WriteLine($"{indent}+-- {Path.GetFileName(dir)}");
                    Tree(dir, indent + "    ");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error reading structure at {path}: {ex.Message}", "FS");
            }
        }

        public static void CreateFile(string path, string content)
        {
            if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot modify {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized write attempt on {path} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                File.WriteAllText(path, content);
                if (!PermissionManager.GetPermission(path).Owner.Equals(SecurityContext.CurrentUser))
                    PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);
                WriteMessage.WriteOK($"File saved: {path}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Write error: {ex.Message}", "FS");
            }
        }

        public static void AppendFile(string path, string content)
        {
            if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot modify {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized append attempt on {path} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                File.AppendAllText(path, content + "\n");
                WriteMessage.WriteOK($"Appended to file: {path}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Append error: {ex.Message}", "FS");
            }
        }

        public static void CopyFile(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                WriteMessage.WriteError($"Copy source does not exist: {sourcePath}", "FS");
                return;
            }

            if (!PermissionManager.CanRead(sourcePath, SecurityContext.CurrentUser) ||
                (File.Exists(destinationPath) && !PermissionManager.CanWrite(destinationPath, SecurityContext.CurrentUser)))
            {
                WriteMessage.WriteError($"Permission denied: Cannot copy {sourcePath} to {destinationPath}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized copy attempt by {SecurityContext.CurrentUser}: {sourcePath} -> {destinationPath}");
                return;
            }

            try
            {
                File.Copy(sourcePath, destinationPath);
                WriteMessage.WriteOK($"Copied file from {sourcePath} to {destinationPath}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Copy error: {ex.Message}", "FS");
            }
        }

        public static void MoveFile(string sourcePath, string destinationPath)
        {
            if (!File.Exists(sourcePath))
            {
                WriteMessage.WriteError($"Move source does not exist: {sourcePath}", "FS");
                return;
            }

            if (!PermissionManager.CanWrite(sourcePath, SecurityContext.CurrentUser) ||
                (File.Exists(destinationPath) && !PermissionManager.CanWrite(destinationPath, SecurityContext.CurrentUser)))
            {
                WriteMessage.WriteError($"Permission denied: Cannot move {sourcePath} to {destinationPath}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized move attempt by {SecurityContext.CurrentUser}: {sourcePath} -> {destinationPath}");
                return;
            }

            try
            {
                File.Move(sourcePath, destinationPath);
                WriteMessage.WriteOK($"Moved file from {sourcePath} to {destinationPath}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Move error: {ex.Message}", "FS");
            }
        }

        public static string ReadFile(string path)
        {
            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized read attempt on {path} by {SecurityContext.CurrentUser}");
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error reading file {path}: {ex.Message}", "FS");
                return string.Empty;
            }
        }

        public static void DeleteFile(string path)
        {
            if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot delete {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized delete attempt on {path} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                File.Delete(path);
                WriteMessage.WriteOK($"File deleted: {path}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Deletion error: {ex.Message}", "FS");
            }
        }

        public static void CreateDir(string path)
        {
            try
            {
                Directory.CreateDirectory(path);
                WriteMessage.WriteOK($"Directory created: {path}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error creating directory: {ex.Message}", "FS");
            }
        }

        public static void DeleteDir(string path, bool recursive = true)
        {
            if (Directory.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot delete directory {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized directory delete attempt on {path} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                Directory.Delete(path, recursive);
                WriteMessage.WriteOK($"Directory deleted: {path}", "FS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error deleting directory: {ex.Message}", "FS");
            }
        }

        private static string FormatBytes(ulong bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int unitIndex = 0;
            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }
            return $"{size:0.##} {units[unitIndex]}";
        }

        public static void GetSpace()
        {
            try
            {
                var partitions = StorageManager.Partitions;
                if (partitions == null || partitions.Count == 0)
                {
                    CommandIO.WriteLine("No partitions found.");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                CommandIO.WriteLine("=== Storage Space (All Partitions) ===");
                for (int i = 0; i < partitions.Count; i++)
                {
                    var partition = partitions[i];
                    ulong totalBytes = (ulong)partition.BlockCount * (ulong)partition.BlockSize;
                    ulong usedBytes = 0;
                    try
                    {
                        string targetPath = (i == 0) ? "/" : "/mnt";
                        long calculated = CalculateDirectorySize(targetPath, 0);
                        usedBytes = calculated > 0 ? (ulong)calculated : 0;
                    }
                    catch { }

                    if (usedBytes > totalBytes) usedBytes = totalBytes;
                    ulong freeBytes = totalBytes - usedBytes;
                    double usagePercentage = totalBytes > 0 ? ((double)usedBytes / totalBytes) * 100 : 0;
                    if (usagePercentage > 100) usagePercentage = 100;

                    CommandIO.WriteLine($"Partition [{i}]: {FormatBytes(freeBytes)} / {FormatBytes(totalBytes)}  [Used: {FormatBytes(usedBytes)} - {usagePercentage:0.#}%]");
                }
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error retrieving space: {ex.Message}", "FS");
                CommandIO.LastCommandSuccess = false;
            }
        }

        public static void FormatPartition(string targetId = "0")
        {
            try
            {
                int id = int.Parse(targetId);
                int partitionCount = Cosmos.Kernel.System.Storage.StorageManager.Partitions.Count;
                if (id < 0 || id >= partitionCount)
                {
                    WriteMessage.WriteError($"Partition index [{targetId}] out of range. Max index is {partitionCount - 1}.", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (targetId == "0") VfsManager.TryUnmount("/");

                FatFormatOptions options = new()
                {
                    Type = FatType.Fat32,
                    VolumeLabel = "ZONDERQ",
                };

                if (!VfsManager.TryFormat("fat", targetId, options))
                {
                    WriteMessage.WriteError($"Format failed for target: {targetId}", "FS");
                    CommandIO.LastCommandSuccess = false;
                }
                else
                {
                    WriteMessage.WriteOK($"Partition {targetId} successfully formatted to FAT32.", "FS");
                    CommandIO.LastCommandSuccess = true;
                }

                if (targetId == "0" && VfsManager.TryMount("fat", "0", MountFlags.None, "/", out var mount))
                {
                    _currentMount = mount;
                    WriteMessage.WriteOK($"Mounted root partition {mount.Name} at {mount.MountPoint}", "VFS");
                    CreateStandardHierarchy();
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Formatting exception: {ex.Message}", "FS");
                CommandIO.LastCommandSuccess = false;
            }
        }

        public static void PartitionDisk(int diskId)
        {
            try
            {
                int deviceCount = Cosmos.Kernel.System.Storage.StorageManager.DeviceCount;
                if (diskId < 0 || diskId >= deviceCount)
                {
                    WriteMessage.WriteError($"Disk ID [{diskId}] out of range (0-{deviceCount - 1}).", "HW");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                var device = Cosmos.Kernel.System.Storage.StorageManager.GetDevice(diskId);
                const uint startLba = 2048;
                if (device.BlockSize < 512 || device.BlockCount <= startLba)
                {
                    WriteMessage.WriteError("Disk is too small or has an unsupported block size for MBR partitioning.", "HW");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                ulong availableBlocks = device.BlockCount - startLba;
                if (availableBlocks > uint.MaxValue)
                {
                    WriteMessage.WriteError("Disk is too large for the current MBR partition layout.", "HW");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                byte[] mbr = new byte[device.BlockSize];
                Array.Clear(mbr, 0, mbr.Length);
                int offset = 446;
                mbr[offset + 0] = 0x80;
                mbr[offset + 1] = 0x00;
                mbr[offset + 2] = 0x02;
                mbr[offset + 3] = 0x00;
                mbr[offset + 4] = 0x0B;
                mbr[offset + 5] = 0xFF;
                mbr[offset + 6] = 0xFF;
                mbr[offset + 7] = 0xFF;

                BitConverter.GetBytes(startLba).CopyTo(mbr, offset + 8);
                BitConverter.GetBytes((uint)availableBlocks).CopyTo(mbr, offset + 12);
                mbr[510] = 0x55;
                mbr[511] = 0xAA;

                device.WriteBlock(0, 1, mbr);
                WriteMessage.WriteOK($"MBR successfully written to Drive [{diskId}]. Reboot system to register new partition.", "HW");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Partitioning exception: {ex.Message}", "HW");
                CommandIO.LastCommandSuccess = false;
            }
        }

        public static void ListDisks()
        {
            try
            {
                int deviceCount = Cosmos.Kernel.System.Storage.StorageManager.DeviceCount;
                CommandIO.WriteLine($"--- Physical Block Devices: {deviceCount} ---");
                for (int i = 0; i < deviceCount; i++)
                {
                    var device = Cosmos.Kernel.System.Storage.StorageManager.GetDevice(i);
                    ulong sizeMB = (ulong)((device.BlockCount * device.BlockSize) / (1024 * 1024));
                    CommandIO.WriteLine($"Drive [{i}]: Capacity: {sizeMB} MB (Block Size: {device.BlockSize} B)");
                }

                var partitions = Cosmos.Kernel.System.Storage.StorageManager.Partitions;
                CommandIO.WriteLine($"--- Logical Partitions: {partitions.Count} ---");
                if (partitions.Count == 0)
                {
                    CommandIO.WriteLine("  └─ No initialized partitions found.");
                }
                else
                {
                    for (int p = 0; p < partitions.Count; p++)
                    {
                        var part = partitions[p];
                        ulong partSizeMB = (ulong)((part.BlockCount * part.BlockSize) / (1024 * 1024));
                        CommandIO.WriteLine($"  ├─ Partition [{p}]: {partSizeMB} MB (Mapped via VFS)");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Hardware analysis error: {ex.Message}", "HW");
            }
        }

        public static void MountPartition(string partitionId, string mountPoint)
        {
            try
            {
                if (VfsManager.TryMount("fat", partitionId, MountFlags.None, mountPoint, out var mount))
                    WriteMessage.WriteOK($"Partition '{partitionId}' mounted successfully at {mountPoint}", "VFS");
                else
                    WriteMessage.WriteError($"Failed to mount partition '{partitionId}' at {mountPoint}.", "VFS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Mount exception: {ex.Message}", "VFS");
            }
        }

        public static void UnmountPartition(string mountPoint)
        {
            try
            {
                if (VfsManager.TryUnmount(mountPoint))
                    WriteMessage.WriteOK($"Node {mountPoint} successfully unmounted.", "VFS");
                else
                    WriteMessage.WriteError($"Could not unmount {mountPoint}. Check if path exists.", "VFS");
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Unmount error: {ex.Message}", "VFS");
            }
        }

        public static void FileStat(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var fileInfo = new FileInfo(path);
                    CommandIO.WriteLine($"--- File Statistics: {fileInfo.Name} ---");
                    CommandIO.WriteLine($"Absolute Path: {fileInfo.FullName}");
                    CommandIO.WriteLine($"Size:          {fileInfo.Length} bytes");
                    CommandIO.WriteLine($"Attributes:    {fileInfo.Attributes}");
                    CommandIO.WriteLine($"Last Write:    {fileInfo.LastWriteTime}");
                }
                else
                {
                    WriteMessage.WriteError($"File '{path}' does not exist.", "FS");
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Stat error: {ex.Message}", "FS");
            }
        }

        public static void HexDump(int diskId, ulong sector)
        {
            try
            {
                int deviceCount = Cosmos.Kernel.System.Storage.StorageManager.DeviceCount;
                if (diskId < 0 || diskId >= deviceCount)
                {
                    WriteMessage.WriteError($"Disk [{diskId}] does not exist.", "HW");
                    return;
                }

                var device = Cosmos.Kernel.System.Storage.StorageManager.GetDevice(diskId);
                if (sector >= device.BlockCount)
                {
                    WriteMessage.WriteError($"Sector [{sector}] is outside disk [{diskId}].", "HW");
                    return;
                }

                byte[] buffer = new byte[device.BlockSize];
                device.ReadBlock(sector, 1, buffer);
                CommandIO.WriteLine($"--- HexDump: Disk [{diskId}] | Sector: {sector} | Size: {device.BlockSize} B ---");

                for (int i = 0; i < buffer.Length; i += 16)
                {
                    string hexPart = $"{i:X4}  ";
                    for (int j = 0; j < 16; j++)
                    {
                        hexPart += i + j < buffer.Length ? $"{buffer[i + j]:X2} " : "   ";
                        if (j == 7) hexPart += " ";
                    }

                    hexPart += " |";
                    for (int j = 0; j < 16; j++)
                    {
                        if (i + j < buffer.Length)
                        {
                            byte b = buffer[i + j];
                            hexPart += b >= 32 && b <= 126 ? (char)b : '.';
                        }
                    }
                    hexPart += "|";
                    CommandIO.WriteLine(hexPart);
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Hexdump error: {ex.Message}", "HW");
            }
        }

        private static long CalculateDirectorySize(string path, int depth)
        {
            if (depth > 64) return 0;

            long size = 0;
            try
            {
                string[] files = Directory.GetFiles(path);
                foreach (string file in files)
                {
                    long fileSize = new FileInfo(file).Length;
                    if (fileSize > 0 && size <= long.MaxValue - fileSize)
                        size += fileSize;
                }

                string[] directories = Directory.GetDirectories(path);
                foreach (string dir in directories)
                {
                    long childSize = CalculateDirectorySize(dir, depth + 1);
                    if (childSize > 0 && size <= long.MaxValue - childSize)
                        size += childSize;
                }
            }
            catch { }
            return size;
        }
    }
}