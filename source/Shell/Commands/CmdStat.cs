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
                Disk.FileStat(PathResolver.GetAbsolutePath(currentPath, args[1]));
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
