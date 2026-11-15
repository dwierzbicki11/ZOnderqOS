using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS
{
    public static partial class Disk
    {
        // Dispatch and permission failures must report failure just like physical I/O.
        public static void DeleteFile(string path)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
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
            CommandIO.LastCommandSuccess = false;
            try
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
            try
            {
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

    }
}
