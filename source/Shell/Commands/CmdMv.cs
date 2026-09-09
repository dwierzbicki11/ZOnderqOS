namespace ZonderqOS.Commands
{
    public class CmdMv : ICommand
    {
        public string Name => "mv";
        public string Description => "Move or rename file";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2) 
            {
                Disk.MoveFile(PathResolver.GetAbsolutePath(currentPath, args[1]), PathResolver.GetAbsolutePath(currentPath, args[2]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: mv <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
