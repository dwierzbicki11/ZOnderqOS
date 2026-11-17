using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdUmount : ICommand
    {
        public string Name => "umount";
        public string Description => "Unmount partition from path";
        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
            {
                WriteMessage.WriteError("Access denied. Root privileges required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthorized umount attempt by " + (SecurityContext.CurrentUser ?? "?"));
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length != 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                WriteMessage.WriteError("Usage: umount <mount_point>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string mountPoint = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (string.IsNullOrEmpty(mountPoint) || mountPoint == "/" || VirtualFs.IsVirtualPath(mountPoint))
            {
                WriteMessage.WriteError($"Cannot unmount protected or virtual path: {mountPoint}", "VFS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.UnmountPartition(mountPoint);
        }
    }
}
