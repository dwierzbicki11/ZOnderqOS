using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS
{
    public static partial class Disk
    {
        // Keep virtual dispatch and permission-store failures inside the same
        // command error boundary as physical writes. Success is published last.
        public static void CreateFile(string path, string content)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                if (VirtualFs.IsVirtualPath(path))
                {
                    if (VirtualFs.TryWrite(path, content, false, out string virtualError))
                    {
                        CommandIO.LastCommandSuccess = true;
                        return;
                    }

                    string message = string.IsNullOrEmpty(virtualError)
                        ? "virtual filesystem does not support writing this path"
                        : virtualError;
                    WriteMessage.WriteError($"Write rejected for {path}: {message}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot modify {path}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized write attempt on {path} by {SecurityContext.CurrentUser}");
                    return;
                }

                File.WriteAllText(path, content);
                if (!PermissionManager.GetPermission(path).Owner.Equals(SecurityContext.CurrentUser))
                    PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);
                WriteMessage.WriteOK($"File saved: {path}", "FS");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Write error: {ex.Message}", "FS");
            }
        }

        public static void AppendFile(string path, string content)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                if (VirtualFs.IsVirtualPath(path))
                {
                    if (VirtualFs.TryWrite(path, content + "\n", true, out string virtualError))
                    {
                        CommandIO.LastCommandSuccess = true;
                        return;
                    }

                    string message = string.IsNullOrEmpty(virtualError)
                        ? "virtual filesystem does not support appending this path"
                        : virtualError;
                    WriteMessage.WriteError($"Append rejected for {path}: {message}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (File.Exists(path) && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot modify {path}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized append attempt on {path} by {SecurityContext.CurrentUser}");
                    return;
                }

                File.AppendAllText(path, content + "\n");
                WriteMessage.WriteOK($"Appended to file: {path}", "FS");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Append error: {ex.Message}", "FS");
            }
        }

    }
}
