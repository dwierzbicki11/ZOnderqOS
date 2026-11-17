using System;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdMv : ICommand
    {
        private const int MaxRamFsFileBytes = 128 * 1024;

        public string Name => "mv";
        public string Description => "Move or rename file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                WriteMessage.WriteError("Permission denied. Authentication required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthenticated move attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length != 3)
            {
                WriteMessage.WriteError("Usage: mv <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string source = PathResolver.GetAbsolutePath(currentPath, args[1]);
            string destination = PathResolver.GetAbsolutePath(currentPath, args[2]);
            string currentUser = SecurityContext.CurrentUser;

            if (string.Equals(source, destination, StringComparison.OrdinalIgnoreCase))
            {
                WriteMessage.WriteError($"Source and destination are identical: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (VirtualFs.IsReadOnlyPath(source) || (VirtualFs.IsVirtualPath(source) && !IsRamFsPath(source)))
            {
                WriteMessage.WriteError($"Cannot move read-only/special virtual node: {source}", "FS");
                SecurityLogger.LogEvent("WARN", $"Rejected move of virtual path {source} by {currentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (VirtualFs.IsReadOnlyPath(destination) || (VirtualFs.IsVirtualPath(destination) && !IsRamFsPath(destination)))
            {
                WriteMessage.WriteError($"Destination is not movable target: {destination}", "FS");
                SecurityLogger.LogEvent("WARN", $"Rejected move to read-only/virtual path {destination} by {currentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
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

                    if (!VirtualFs.TryDeleteFile(source, out string delError))
                    {
                        VirtualFs.TryDeleteFile(destination, out _);
                        WriteMessage.WriteError($"Move cleanup failed: {delError}", "FS");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }

                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!PermissionManager.CanCreate(destination, currentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot create {destination}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized move destination attempt by {currentUser ?? "?"}: {source} -> {destination}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(destination, content);
                if (!CommandIO.LastCommandSuccess || !File.Exists(destination))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                PermissionManager.SetPermission(destination, currentUser, 644);
                VirtualFs.TryDeleteFile(source, out _);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (Directory.Exists(source))
            {
                WriteMessage.WriteError($"Source is a directory: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!File.Exists(source))
            {
                WriteMessage.WriteError($"Source file does not exist: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(source, currentUser) || !PermissionManager.CanWrite(source, currentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot move {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized move attempt on {source} by {currentUser ?? "?"}");
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

                if (!TryReadBoundedText(source, MaxRamFsFileBytes, out string content, out string readError))
                {
                    WriteMessage.WriteError($"Move failed: {readError}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(destination, content);
                if (!CommandIO.LastCommandSuccess)
                    return;

                try
                {
                    File.Delete(source);
                }
                catch (Exception ex)
                {
                    VirtualFs.TryDeleteFile(destination, out _);
                    WriteMessage.WriteError($"Move failed deleting source: {ex.Message}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                PermissionManager.RemovePermission(source);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (File.Exists(destination) || Directory.Exists(destination))
            {
                WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanCreate(destination, currentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot create {destination}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized move destination attempt by {currentUser ?? "?"}: {source} -> {destination}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.MoveFile(source, destination);
            if (File.Exists(source) || !File.Exists(destination))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.MovePermissionsUnder(source, destination);
            CommandIO.LastCommandSuccess = true;
        }

        private static bool TryReadBoundedText(string source, int maxBytes, out string content, out string error)
        {
            content = null;
            error = null;
            try
            {
                using (FileStream fs = new FileStream(source, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length > maxBytes)
                    {
                        error = $"source file exceeds {maxBytes} byte RAM filesystem limit";
                        return false;
                    }

                    using (var reader = new StreamReader(fs, Encoding.UTF8, true))
                    {
                        var sb = new StringBuilder((int)Math.Min(fs.Length, 4096L));
                        char[] chunk = new char[4096];
                        int read;
                        int totalChars = 0;
                        while ((read = reader.Read(chunk, 0, chunk.Length)) > 0)
                        {
                            totalChars += read;
                            if (totalChars > maxBytes)
                            {
                                error = $"source text exceeds {maxBytes} character limit";
                                return false;
                            }
                            sb.Append(chunk, 0, read);
                        }
                        content = sb.ToString();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
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
