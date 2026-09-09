namespace ZonderqOS.Commands
{
    public class CmdLs : ICommand
    {
        public string Name => "ls";
        public string Description => "List directory tree structure";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) Disk.Tree(PathResolver.GetAbsolutePath(currentPath, args[1]));
            else Disk.Tree(currentPath);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
