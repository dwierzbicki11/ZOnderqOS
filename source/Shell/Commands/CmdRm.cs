using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdRm : ICommand
    {
        public string Name => "rm";
        public string Description => "Delete a file";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                if (args.Length <= 1)
                {
                    WriteMessage.WriteError("Usage: rm <file>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string path = PathResolver.GetAbsolutePath(currentPath, args[1]);

                if (RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path))
                {
                    Disk.DeleteFile(path);
                    return;
                }

                if (VirtualFs.IsVirtualPath(path))
                {
                    WriteMessage.WriteError($"Cannot remove virtual node: {path}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!File.Exists(path))
                {
                    WriteMessage.WriteError($"File does not exist: {path}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot delete {path}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized delete attempt on {path} by {SecurityContext.CurrentUser}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.DeleteFile(path);
                // Disappearance alone cannot override a backend failure (for
                // example, an I/O error after deletion or loss of the device).
                if (!CommandIO.LastCommandSuccess || File.Exists(path))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                PermissionManager.RemovePermission(path);
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.LastCommandSuccess = false;
                WriteMessage.WriteError($"Delete failed: {ex.Message}", "FS");
            }
        }
    }
}
