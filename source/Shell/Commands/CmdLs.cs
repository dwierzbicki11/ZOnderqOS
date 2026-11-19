using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdLs : ICommand
    {
        public string Name => "ls";
        public string Description => "List directory tree structure";

        public void Execute(string[] args, ref string currentPath)
        {
            string path = args.Length > 1
                ? PathResolver.GetAbsolutePath(currentPath, args[1])
                : currentPath;

            if (VirtualFs.IsVirtualPath(path))
            {
                if (!VirtualFs.TryList(path, out string[] entries))
                {
                    WriteMessage.WriteError($"virtual filesystem directory does not exist: {path}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                for (int i = 0; i < entries.Length; i++)
                    CommandIO.WriteLine(entries[i]);

                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: {path}", "SEC");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            Disk.Tree(path);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
