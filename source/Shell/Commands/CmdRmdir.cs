namespace ZonderqOS.Commands
{
    public class CmdRmdir : ICommand
    {
        public string Name => "rmdir";
        public string Description => "Remove a directory";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                Disk.DeleteDir(PathResolver.GetAbsolutePath(currentPath, args[1]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: rmdir <path>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
