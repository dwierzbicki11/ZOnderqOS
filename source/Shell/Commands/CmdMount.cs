using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdMount : ICommand
    {
        public string Name => "mount";
        public string Description => "Mount partition (<id> <path>)";
        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                WriteMessage.WriteError("Access denied. Root privileges required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthorized mount attempt by " + (SecurityContext.CurrentUser ?? "?"));
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length != 3 || string.IsNullOrWhiteSpace(args[1]) || string.IsNullOrWhiteSpace(args[2]))
            {
                WriteMessage.WriteError("Usage: mount <partition_id> <mount_point>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string mountPoint = PathResolver.GetAbsolutePath(currentPath, args[2]);
            if (string.IsNullOrEmpty(mountPoint) || mountPoint == "/" || VirtualFs.IsVirtualPath(mountPoint))
            {
                WriteMessage.WriteError($"Invalid mount point: {mountPoint}", "VFS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.MountPartition(args[1].Trim(), mountPoint);
        }
    }
}
