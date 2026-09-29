using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdCp : ICommand
    {
        public string Name => "cp";
        public string Description => "Copy file from source to destination";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: cp <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string source = PathResolver.GetAbsolutePath(currentPath, args[1]);
            string destination = PathResolver.GetAbsolutePath(currentPath, args[2]);

            bool sourceVirtual = VirtualFs.IsVirtualPath(source);
            bool destinationVirtual = VirtualFs.IsVirtualPath(destination);

            if (sourceVirtual)
            {
                if (!VirtualFs.TryRead(source, out string virtualContent))
                {
                    WriteMessage.WriteError($"Virtual source is not a readable file: {source}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (destinationVirtual && !IsWritableVirtualTarget(destination))
                {
                    WriteMessage.WriteError($"Destination is not writable: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (IsRamFsPath(destination) && VirtualEntryExists(destination))
                {
                    WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!destinationVirtual && File.Exists(destination))
                {
                    WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(destination, virtualContent);
                return;
            }

            if (!File.Exists(source))
            {
                WriteMessage.WriteError($"Source file does not exist: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(source, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized copy attempt on {source} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (IsRamFsPath(destination))
            {
                if (VirtualEntryExists(destination))
                {
                    WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string content = File.ReadAllText(source);
                Disk.CreateFile(destination, content);
                return;
            }

            if (destinationVirtual)
            {
                WriteMessage.WriteError($"Destination is not writable: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (File.Exists(destination))
            {
                WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.CopyFile(source, destination);
            if (!File.Exists(destination))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.CopyPermissionsUnder(source, destination, SecurityContext.CurrentUser);
            CommandIO.LastCommandSuccess = true;
        }

        private static bool IsRamFsPath(string path)
        {
            return RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path);
        }

        private static bool IsWritableVirtualTarget(string path)
        {
            return IsRamFsPath(path) || DeviceFs.IsDevicePath(path);
        }

        private static bool VirtualEntryExists(string path)
        {
            if (RunFs.IsRunPath(path))
                return RunFs.FileExists(path) || RunFs.DirectoryExists(path);
            if (TmpFs.IsTmpPath(path))
                return TmpFs.FileExists(path) || TmpFs.DirectoryExists(path);
            return false;
        }
    }
}
