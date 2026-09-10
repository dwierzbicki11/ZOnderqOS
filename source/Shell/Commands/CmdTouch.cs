using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdTouch : ICommand
    {
        public string Name => "touch";
        public string Description => "Create an empty file if it does not exist";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: touch <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (File.Exists(path))
            {
                if (!PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot touch {path}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized touch attempt on {path} by {SecurityContext.CurrentUser}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                // Do not truncate an existing file. Metadata-only timestamp updates are
                // intentionally omitted until the VFS exposes them consistently.
                CommandIO.LastCommandSuccess = true;
                return;
            }

            Disk.CreateFile(path, string.Empty);
            if (!File.Exists(path))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
