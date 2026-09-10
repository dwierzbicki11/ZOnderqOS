using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdRm : ICommand
    {
        public string Name => "rm";
        public string Description => "Delete a file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: rm <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
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
            if (File.Exists(path))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.RemovePermission(path);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
