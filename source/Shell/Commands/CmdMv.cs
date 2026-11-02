using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdMv : ICommand
    {
        public string Name => "mv";
        public string Description => "Move or rename file";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                ExecuteCore(args, ref currentPath);
            }
            catch (Exception ex)
            {
                CommandIO.LastCommandSuccess = false;
                WriteMessage.WriteError($"Move failed: {ex.Message}", "FS");
            }
        }

        private static void ExecuteCore(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: mv <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string source = PathResolver.GetAbsolutePath(currentPath, args[1]);
            string destination = PathResolver.GetAbsolutePath(currentPath, args[2]);

            // Rename within one RAM filesystem under its own lock. Copy/delete
            // would need a second quota allocation and can fail on a full volume.
            bool sameTmp = TmpFs.IsTmpPath(source) && TmpFs.IsTmpPath(destination);
            bool sameRun = RunFs.IsRunPath(source) && RunFs.IsRunPath(destination);
            if (sameTmp || sameRun)
            {
                string error;
                bool moved = sameTmp
                    ? TmpFs.TryMoveFile(source, destination, out error)
                    : RunFs.TryMoveFile(source, destination, out error);
                CommandIO.LastCommandSuccess = moved;
                if (!moved)
                    WriteMessage.WriteError($"Move failed: {error}", "FS");
                return;
            }

            if (IsRamFsPath(source))
            {
                if (!VirtualFs.TryRead(source, out string content))
                {
                    WriteMessage.WriteError($"Source file does not exist: {source}", "FS");
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

                    Disk.CreateFile(destination, content);
                    if (!CommandIO.LastCommandSuccess)
                        return;

                    Disk.DeleteFile(source);
                    return;
                }

                if (VirtualFs.IsVirtualPath(destination))
                {
                    WriteMessage.WriteError($"Destination is not movable target: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (File.Exists(destination))
                {
                    WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                RamTextTransfer.ValidateForDisk(content);
                Disk.CreateFile(destination, content);
                if (!CommandIO.LastCommandSuccess)
                    return;

                Disk.DeleteFile(source);
                return;
            }

            if (VirtualFs.IsVirtualPath(source))
            {
                WriteMessage.WriteError($"Cannot move read-only/special virtual node: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!File.Exists(source))
            {
                WriteMessage.WriteError($"Source file does not exist: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanWrite(source, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot move {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized move attempt on {source} by {SecurityContext.CurrentUser}");
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

                if (!PermissionManager.CanRead(source, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot read {source}", "SEC");
                    return;
                }

                string content = RamTextTransfer.ReadLossless(source, TmpFs.IsTmpPath(destination) ? TmpFs.MaxFileChars : RunFs.MaxFileChars);
                Disk.CreateFile(destination, content);
                if (!CommandIO.LastCommandSuccess)
                    return;

                Disk.DeleteFile(source);
                if (!CommandIO.LastCommandSuccess)
                    return;

                PermissionManager.RemovePermission(source);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (VirtualFs.IsVirtualPath(destination))
            {
                WriteMessage.WriteError($"Destination is not movable target: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (File.Exists(destination))
            {
                WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.MoveFile(source, destination);
            if (!CommandIO.LastCommandSuccess || File.Exists(source) || !File.Exists(destination))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.MovePermissionsUnder(source, destination);
            CommandIO.LastCommandSuccess = true;
        }

        private static bool IsRamFsPath(string path)
        {
            return RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path);
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
