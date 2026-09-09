namespace ZonderqOS.Commands
{
    public class CmdRm : ICommand
    {
        public string Name => "rm";
        public string Description => "Delete a file";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                Disk.DeleteFile(PathResolver.GetAbsolutePath(currentPath, args[1]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: rm <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
