using System;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdCp : ICommand
    {
        private const int CopyBufferBytes = 16 * 1024;
        private const int MaxRamFsFileBytes = 128 * 1024;

        public string Name => "cp";
        public string Description => "Copy file from source to destination";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                WriteMessage.WriteError("Permission denied. Authentication required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthenticated copy attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length != 3)
            {
                WriteMessage.WriteError("Usage: cp <source> <destination>", "CMD");
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

            bool sourceVirtual = VirtualFs.IsVirtualPath(source);
            bool destinationVirtual = VirtualFs.IsVirtualPath(destination);

            if (VirtualFs.IsReadOnlyPath(destination) || (destinationVirtual && !IsRamFsPath(destination)))
            {
                WriteMessage.WriteError($"Destination is a read-only or special virtual path: {destination}", "FS");
                SecurityLogger.LogEvent("WARN", $"Rejected copy to read-only/virtual path {destination} by {currentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (sourceVirtual)
            {
                if (!VirtualFs.TryRead(source, out string virtualContent))
                {
                    WriteMessage.WriteError($"Virtual source is not a readable file: {source}", "FS");
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

                    Disk.CreateFile(destination, virtualContent);
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
                    SecurityLogger.LogEvent("WARN", $"Unauthorized copy destination attempt by {currentUser ?? "?"}: {source} -> {destination}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(destination, virtualContent);
                if (!CommandIO.LastCommandSuccess || !File.Exists(destination))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                PermissionManager.SetPermission(destination, currentUser, 644);
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

            if (!PermissionManager.CanRead(source, currentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized copy attempt on {source} by {currentUser ?? "?"}");
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
                    WriteMessage.WriteError($"Copy failed: {readError}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(destination, content);
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
                SecurityLogger.LogEvent("WARN", $"Unauthorized copy destination attempt by {currentUser ?? "?"}: {source} -> {destination}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!TryCopyFileChunked(source, destination, out string copyError))
            {
                WriteMessage.WriteError($"Copy error: {copyError}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.CopyPermissionsUnder(source, destination, currentUser);
            WriteMessage.WriteOK($"Copied file from {source} to {destination}", "FS");
            CommandIO.LastCommandSuccess = true;
        }

        private static bool TryCopyFileChunked(string source, string destination, out string error)
        {
            error = null;
            bool created = false;
            try
            {
                byte[] buffer = new byte[CopyBufferBytes];
                using (FileStream src = new FileStream(source, FileMode.Open, FileAccess.Read))
                using (FileStream dst = new FileStream(destination, FileMode.CreateNew, FileAccess.Write))
                {
                    created = true;
                    int bytesRead;
                    while ((bytesRead = src.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        dst.Write(buffer, 0, bytesRead);
                    }
                    dst.Flush();
                }

                return File.Exists(destination);
            }
            catch (Exception ex)
            {
                if (created)
                {
                    try
                    {
                        if (File.Exists(destination))
                            File.Delete(destination);
                    }
                    catch { }
                }

                error = ex.Message;
                return false;
            }
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
