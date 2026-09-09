namespace ZonderqOS.Commands
{
    public class CmdMkdir : ICommand
    {
        public string Name => "mkdir";
        public string Description => "Create a new directory";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                Disk.CreateDir(PathResolver.GetAbsolutePath(currentPath, args[1]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: mkdir <path>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
