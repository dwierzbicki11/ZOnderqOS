namespace ZonderqOS.Commands
{
    public class CmdTouch : ICommand
    {
        public string Name => "touch";
        public string Description => "Create an empty file";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                Disk.CreateFile(PathResolver.GetAbsolutePath(currentPath, args[1]), string.Empty);
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: touch <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
