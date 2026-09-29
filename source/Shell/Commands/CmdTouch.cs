using System.IO;
using ZonderqOS.SystemCore;

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

            if (RunFs.IsRunPath(path) || TmpFs.IsTmpPath(path))
            {
                bool directory = RunFs.IsRunPath(path)
                    ? RunFs.DirectoryExists(path)
                    : TmpFs.DirectoryExists(path);
                bool file = RunFs.IsRunPath(path)
                    ? RunFs.FileExists(path)
                    : TmpFs.FileExists(path);

                if (directory)
                {
                    WriteMessage.WriteError($"Path is a directory: {path}", "FS");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (file)
                {
                    CommandIO.LastCommandSuccess = true;
                    return;
                }

                Disk.CreateFile(path, string.Empty);
                return;
            }

            if (VirtualFs.IsVirtualPath(path))
            {
                WriteMessage.WriteError($"Cannot touch virtual node: {path}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

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
