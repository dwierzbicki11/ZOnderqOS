namespace ZonderqOS.Commands
{
    public class CmdCp : ICommand
    {
        public string Name => "cp";
        public string Description => "Copy file from source to destination";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2) 
            {
                Disk.CopyFile(PathResolver.GetAbsolutePath(currentPath, args[1]), PathResolver.GetAbsolutePath(currentPath, args[2]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: cp <source> <destination>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
