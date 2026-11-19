using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdStat : ICommand
    {
        public string Name => "stat";
        public string Description => "Display file metadata and statistics";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                string target = PathResolver.GetAbsolutePath(currentPath, args[1]);
                if (!VirtualFs.IsVirtualPath(target) && !PermissionManager.CanRead(target, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: {target}", "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                Disk.FileStat(target);
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: stat <file_path>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
