namespace ZonderqOS.Commands
{
    public class CmdMount : ICommand
    {
        public string Name => "mount";
        public string Description => "Mount partition (<id> <path>)";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2) 
            {
                Disk.MountPartition(args[1], PathResolver.GetAbsolutePath(currentPath, args[2]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: mount <partition_id> <mount_point>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
