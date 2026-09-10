using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdAppend : ICommand
    {
        public string Name => "append";
        public string Description => "Append text to file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: append <file> <content>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            bool existed = File.Exists(path);

            if (existed && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot append to {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized append attempt on {path} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string content = string.Join(" ", args, 2, args.Length - 2).Replace("\\n", "\n");
            Disk.AppendFile(path, content);

            if (!existed && File.Exists(path))
                PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);

            CommandIO.LastCommandSuccess = File.Exists(path);
        }
    }
}
