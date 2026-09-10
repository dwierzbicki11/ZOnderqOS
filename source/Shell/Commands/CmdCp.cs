using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdCp : ICommand
    {
        public string Name => "cp";
        public string Description => "Copy file from source to destination";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: cp <source> <destination>", "CMD");
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

            if (!PermissionManager.CanRead(source, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {source}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized copy attempt on {source} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (File.Exists(destination))
            {
                WriteMessage.WriteError($"Destination already exists: {destination}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.CopyFile(source, destination);
            if (!File.Exists(destination))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            PermissionManager.CopyPermissionsUnder(source, destination, SecurityContext.CurrentUser);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
