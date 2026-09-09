namespace ZonderqOS.Commands
{
    public class CmdAppend : ICommand
    {
        public string Name => "append";
        public string Description => "Append text to file";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2) 
            {
                Disk.AppendFile(PathResolver.GetAbsolutePath(currentPath, args[1]), args[2]);
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: append <file> <content>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
