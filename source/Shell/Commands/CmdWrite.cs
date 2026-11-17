using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdWrite : ICommand
    {
        private const int MaxContentCharacters = 65536;

        public string Name => "write";
        public string Description => "Create or overwrite file with content";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                WriteMessage.WriteError("Permission denied. Authentication required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthenticated write attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length < 2)
            {
                WriteMessage.WriteError("Usage: write <file> [content]", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (VirtualFs.IsReadOnlyPath(path))
            {
                WriteMessage.WriteError($"Read-only virtual filesystem: cannot write {path}", "FS");
                SecurityLogger.LogEvent("WARN", $"Rejected write on read-only virtual path {path} by {SecurityContext.CurrentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (VirtualFs.IsVirtualPath(path))
            {
                if (!TryBuildContent(args, out string virtualContent))
                {
                    WriteMessage.WriteError($"Content exceeds {MaxContentCharacters} characters.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.CreateFile(path, virtualContent);
                return;
            }

            if (Directory.Exists(path))
            {
                WriteMessage.WriteError($"Target is a directory: {path}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            bool existed = File.Exists(path);
            bool permitted = existed
                ? PermissionManager.CanWrite(path, SecurityContext.CurrentUser)
                : PermissionManager.CanCreate(path, SecurityContext.CurrentUser);
            if (!permitted)
            {
                WriteMessage.WriteError($"Permission denied: Cannot write {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized write attempt on {path} by {SecurityContext.CurrentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!TryBuildContent(args, out string content))
            {
                WriteMessage.WriteError($"Content exceeds {MaxContentCharacters} characters.", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.CreateFile(path, content);
            if (!CommandIO.LastCommandSuccess || !File.Exists(path))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!existed)
                PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);
        }

        private static bool TryBuildContent(string[] args, out string content)
        {
            var builder = new StringBuilder(System.Math.Min(256, MaxContentCharacters));
            for (int i = 2; i < args.Length; i++)
            {
                string value = args[i] ?? string.Empty;
                if (i > 2)
                {
                    if (builder.Length == MaxContentCharacters)
                    {
                        content = string.Empty;
                        return false;
                    }
                    builder.Append(' ');
                }

                for (int j = 0; j < value.Length; j++)
                {
                    if (builder.Length == MaxContentCharacters)
                    {
                        content = string.Empty;
                        return false;
                    }

                    if (value[j] == '\\' && j + 1 < value.Length && value[j + 1] == 'n')
                    {
                        builder.Append('\n');
                        j++;
                    }
                    else
                    {
                        builder.Append(value[j]);
                    }
                }
            }

            content = builder.ToString();
            return true;
        }
    }
}
