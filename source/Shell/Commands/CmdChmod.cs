using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdChmod : ICommand
    {
        private const int MaxAclPathLength = 512;

        public string Name => "chmod";
        public string Description => "Change file mode bits (chmod 600 <file>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                WriteMessage.WriteError("Permission denied. Only root can change permissions.", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized chmod attempt by {SecurityContext.CurrentUser ?? "?"} (uid={SecurityContext.CurrentUid}, auth={SecurityContext.IsAuthenticated}).");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length != 3)
            {
                WriteMessage.WriteError("Usage: chmod <permissions> <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!TryParseMode(args[1], out int perms))
            {
                WriteMessage.WriteError("Invalid permission format. Use three octal digits (e.g. 644, 600, 755).", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
            if (VirtualFs.IsVirtualPath(path) || VirtualFs.IsReadOnlyPath(path))
            {
                WriteMessage.WriteError($"Cannot change permissions on virtual filesystem path: {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Rejected chmod on virtual path {path} by {SecurityContext.CurrentUser ?? "?"}.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (string.IsNullOrEmpty(path) || path.Length > MaxAclPathLength ||
                path.IndexOf('|') >= 0 || path.IndexOf('\r') >= 0 || path.IndexOf('\n') >= 0)
            {
                WriteMessage.WriteError("Invalid target path for ACL entry.", "SEC");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!File.Exists(path) && !Directory.Exists(path))
            {
                WriteMessage.WriteError("File or directory does not exist.", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            var currentAcl = PermissionManager.GetPermission(path);
            PermissionManager.SetPermission(path, currentAcl.Owner, perms);
            var updatedAcl = PermissionManager.GetPermission(path);
            if (updatedAcl.Perms != perms)
            {
                WriteMessage.WriteError($"Failed to update ACL permissions for {path}.", "SEC");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            WriteMessage.WriteOK($"Permissions for {path} changed to {perms:D3}", "SEC");
            SecurityLogger.LogEvent("INFO", $"Permissions of {path} changed to {perms:D3} by root.");
            CommandIO.LastCommandSuccess = true;
        }

        private static bool TryParseMode(string value, out int mode)
        {
            mode = 0;
            if (string.IsNullOrEmpty(value) || value.Length != 3)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c < '0' || c > '7')
                    return false;

                mode = mode * 10 + (c - '0');
            }

            return true;
        }
    }
}
