using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdWrite : ICommand
    {
        public string Name => "write";
        public string Description => "Create or overwrite file with content";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2)
            {
                WriteMessage.WriteError("Usage: write <file> [content]", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            bool existed = File.Exists(path);
            if (existed && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot write {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized write attempt on {path} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string content = args.Length > 2
                ? string.Join(" ", args, 2, args.Length - 2).Replace("\\n", "\n")
                : string.Empty;

            Disk.CreateFile(path, content);

            if (!File.Exists(path))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!existed)
                PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);

            CommandIO.LastCommandSuccess = true;
        }
    }
}
