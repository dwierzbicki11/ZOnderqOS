namespace ZonderqOS.Commands
{
    public class CmdUmount : ICommand
    {
        public string Name => "umount";
        public string Description => "Unmount partition from path";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1) 
            {
                Disk.UnmountPartition(PathResolver.GetAbsolutePath(currentPath, args[1]));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: umount <mount_point>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
