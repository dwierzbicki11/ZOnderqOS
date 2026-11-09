using System;
using System.IO;
using Cosmos.Kernel.System.FileSystem;
using Cosmos.Kernel.System.FileSystem.Fat;

using Cosmos.Kernel.System.Storage;
using ZonderqOS.SystemCore;

namespace ZonderqOS
{
    public static partial class Disk
    {
        private static FatFileSystemType fat;
        private static VfsMount _currentMount;

        private sealed class RootSectorReader : IFatSectorReader
        {
            private readonly Partition partition;
            public RootSectorReader(Partition value) { partition = value; }
            public ulong SectorCount => partition.BlockCount;
            public uint SectorSize => checked((uint)partition.BlockSize);
            public void ReadSector(ulong sector, byte[] buffer)
            {
                if (sector >= partition.BlockCount)
                    throw new ArgumentOutOfRangeException(nameof(sector));
                partition.ReadBlock(sector, 1UL, buffer);
            }
        }

        public static bool TryGetRootSpace(out Fat32Space space, out string error)
        {
            // Keep the approved object selected at mount time. A discovery-list
            // reorder must not redirect diagnostics to a different device.
            return Fat32Allocation.TryRead(_rootPartition == null ? null :
                new RootSectorReader(_rootPartition), out space, out error);
        }

        public static bool TryBeginRootSpaceScan(out Fat32Allocation.Scan scan, out string error)
        {
            return Fat32Allocation.TryBegin(_rootPartition == null ? null :
                new RootSectorReader(_rootPartition), out scan, out error);
        }

        public static bool Initialize()
        {
            try
            {
                fat = new FatFileSystemType();
                if (!VfsManager.RegisterFileSystem("fat", fat))
                {
                    WriteMessage.WriteError("Could not register FAT filesystem.", "VFS");
                    return false;
                }

                // Cosmos 3.0.95: the partition order can change when a USB disk
                // appears. Never mount numeric source "0" (it was Kali's ESP).
                // Require a unique explicitly labelled data FAT32 partition.
#if ZONDERQ_3094_RAM_ROOT
                // Full GUI can boot safely without ANY partition writes.
                // This memory-only partition is created and FAT32-formatted
                // inside private RAM, never from StorageManager's disk index.
                if (!RamRootVolume.TryCreate(out Partition data))
                {
                    WriteMessage.WriteError("RAM-only root could not be prepared; refusing host-disk fallback.", "VFS");
                    return false;
                }
#else
                if (!DataVolumePolicy.TryFindDataPartition(out Partition data, out string error))
                {
                    WriteMessage.WriteError(error, "VFS");
                    return false;
                }
#endif

#if ZONDERQ_3094_RAM_ROOT
                // Bind this filesystem to the explicit RAM partition object.
                // Unlike an index-based FAT mount, even a concurrent hotplug
                // cannot redirect a write to the laptop's physical disk.
                if (!VfsManager.RegisterFileSystem("ramfat", new FatFileSystemType(data)))
                {
                    WriteMessage.WriteError("Could not register RAM-only FAT.", "VFS");
                    return false;
                }
                if (!VfsManager.TryMount("ramfat", ReadOnlySpan<char>.Empty, MountFlags.None, "/", out var mount))
                {
                    WriteMessage.WriteError("Failed to mount RAM-only root. Host-disk fallback prohibited.", "VFS");
                    return false;
                }
#else
                // FAT's numeric partition source can be renumbered by USB
                // hotplug. Bind the driver to the approved Partition itself
                // and mount it with an EMPTY source. A later rescan cannot
                // turn an ordinary filesystem write into a write to Kali's ESP.
                if (!VfsManager.RegisterFileSystem("rootfat", new FatFileSystemType(data)))
                {
                    WriteMessage.WriteError("Could not register bound persistent root FAT.", "VFS");
                    return false;
                }
                if (!VfsManager.TryMount("rootfat", ReadOnlySpan<char>.Empty, MountFlags.None, "/", out var mount))
                {
                    WriteMessage.WriteError("Failed to mount bound ZONDERQDATA root.", "VFS");
                    return false;
                }
#endif

                _currentMount = mount;
                _rootPartition = data;
#if ZONDERQ_3094_RAM_ROOT
                WriteMessage.WriteOK("Mounted ephemeral ZonderqOS FAT32 RAM root at '/' (no physical writes).", "VFS");
#else
                WriteMessage.WriteOK($"Mounted dedicated ZONDERQDATA at '/' (Source: {mount.Source})", "VFS");
#endif
                CreateStandardHierarchy();
                return true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Storage init failed closed: {ex.Message}", "VFS");
                return false;
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

        private static bool RejectVirtualMutation(string path, string operation)
        {
            if (!VirtualFs.IsReadOnlyPath(path))
                return false;

            WriteMessage.WriteError($"Read-only virtual filesystem: {operation} is not allowed on {path}", "FS");
            SecurityLogger.LogEvent("WARN",
                $"Rejected {operation} on read-only virtual path {path} by {SecurityContext.CurrentUser}");
            CommandIO.LastCommandSuccess = false;
            return true;
        }

        public static void CopyFile(string sourcePath, string destinationPath)
        {
            CommandIO.LastCommandSuccess = false;
            if (VirtualFs.IsReadOnlyPath(sourcePath))
            {
                WriteMessage.WriteError($"Virtual filesystem entries cannot be copied through the physical FAT backend: {sourcePath}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (RejectVirtualMutation(destinationPath, "copy destination"))
                return;

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
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Copy error: {ex.Message}", "FS");
            }
        }

        public static void MoveFile(string sourcePath, string destinationPath)
        {
            CommandIO.LastCommandSuccess = false;
            if (RejectVirtualMutation(sourcePath, "move source") ||
                RejectVirtualMutation(destinationPath, "move destination"))
                return;

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
                CommandIO.LastCommandSuccess = true;
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
            CommandIO.LastCommandSuccess = false;
            if (RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path))
            {
                if (VirtualFs.TryDeleteFile(path, out string virtualError))
                {
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                WriteMessage.WriteError($"Delete failed for {path}: {virtualError}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (RejectVirtualMutation(path, "delete"))
                return;

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
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Deletion error: {ex.Message}", "FS");
            }
        }

        public static void CreateDir(string path)
        {
            if (RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path))
            {
                if (VirtualFs.TryCreateDirectory(path, out string virtualError))
                {
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                WriteMessage.WriteError($"mkdir failed for {path}: {virtualError}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (RejectVirtualMutation(path, "mkdir"))
                return;

            try
            {
                Directory.CreateDirectory(path);
                WriteMessage.WriteOK($"Directory created: {path}", "FS");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error creating directory: {ex.Message}", "FS");
                CommandIO.LastCommandSuccess = false;
            }
        }

        public static void DeleteDir(string path, bool recursive = true)
        {
            CommandIO.LastCommandSuccess = false;
            if (RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path))
            {
                if (VirtualFs.TryDeleteDirectory(path, recursive, out string virtualError))
                {
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                WriteMessage.WriteError($"rmdir failed for {path}: {virtualError}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (RejectVirtualMutation(path, "rmdir"))
                return;

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
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Error deleting directory: {ex.Message}", "FS");
                CommandIO.LastCommandSuccess = false;
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
                int partitionCount = partitions == null ? 0 : partitions.Count;
                if (partitionCount == 0 && _rootPartition == null)
                {
                    CommandIO.WriteLine("No partitions found.");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                CommandIO.WriteLine("=== Storage Space (All Partitions) ===");
                for (int i = 0; i < partitionCount; i++)
                {
                    var partition = partitions[i];
                    ulong totalBytes = (ulong)partition.BlockCount * (ulong)partition.BlockSize;
                    // Partition discovery order does not identify VFS mounts.
                    // File lengths also exclude FAT metadata and cluster slack,
                    // so directory traversal cannot measure free filesystem space.
                    CommandIO.WriteLine($"Partition [{i}]: Capacity: {FormatBytes(totalBytes)}; used/free: unavailable");
                }
                if (_rootPartition != null)
                {
                    if (!TryGetRootSpace(out Fat32Space space, out string error))
                    {
                        WriteMessage.WriteError($"Root FAT allocation unavailable: {error}", "FS");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                    CommandIO.WriteLine($"Root FAT32 on-disk scan: data {FormatBytes(space.DataBytes)}; allocated {FormatBytes(space.AllocatedBytes)}; free {FormatBytes(space.FreeBytes)}; bad {FormatBytes(space.BadBytes)}");
                    CommandIO.WriteLine("Data clusters only (metadata excluded); non-atomic scan, concurrent writes may change values.");
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
            // No implicit or interactive disk formatting in the 3.0.95
            // bare-metal build. Provision data on the host, not in-kernel.
            WriteMessage.WriteError("format is disabled for bare-metal data safety (Cosmos 3.0.95).", "FS");
            CommandIO.LastCommandSuccess = false;
        }

        public static void PartitionDisk(int diskId)
        {
            WriteMessage.WriteError("mkpart is disabled for bare-metal data safety (Cosmos 3.0.95).", "FS");
            CommandIO.LastCommandSuccess = false;
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
                    ulong sizeMB = ((ulong)device.BlockCount * (ulong)device.BlockSize) / (1024 * 1024);
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
                        ulong partSizeMB = ((ulong)part.BlockCount * (ulong)part.BlockSize) / (1024 * 1024);
                        CommandIO.WriteLine($"  ├─ Partition [{p}]: {partSizeMB} MB (discovered partition)");
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
            // Additional mounts via index-based FAT are unsafe after USB
            // hotplug and can double-mount the active writable root.
            // Keep the approved persistent root mounted at / only.
            WriteMessage.WriteError("Manual mounts disabled in Cosmos 3.0.95 pending stable device-bound secondary mounts.", "VFS");
            CommandIO.LastCommandSuccess = false;
        }

        public static void UnmountPartition(string mountPoint)
        {
            if (mountPoint == "/")
            {
                WriteMessage.WriteError("Cannot unmount the active data root.", "VFS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (VfsManager.TryUnmount(mountPoint))
            {
                WriteMessage.WriteOK($"Unmounted {mountPoint}", "VFS");
                CommandIO.LastCommandSuccess = true;
            }
            else
            {
                WriteMessage.WriteError($"Cannot unmount {mountPoint}", "VFS");
                CommandIO.LastCommandSuccess = false;
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

    }
}
