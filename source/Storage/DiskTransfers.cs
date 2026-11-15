using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS
{
    public static partial class Disk
    {
        public static void CopyFile(string sourcePath, string destinationPath)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
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
            try
            {
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

                File.Move(sourcePath, destinationPath);
                WriteMessage.WriteOK($"Moved file from {sourcePath} to {destinationPath}", "FS");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Move error: {ex.Message}", "FS");
            }
        }

    }
}
