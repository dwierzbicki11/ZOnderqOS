using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdMv : ICommand
    {
        public string Name => "mv";
        public string Description => "Move or rename file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: mv <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string source = PathResolver.GetAbsolutePath(currentPath, args[1]);
            string destination = PathResolver.GetAbsolutePath(currentPath, args[2]);

            if (!File.Exists(source))
            {
                WriteMessage.WriteError($"Source file does not exist: {source}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanWrite(source, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot move {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized move attempt on {source} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (File.Exists(destination))
            {
                WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
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
    }
}
