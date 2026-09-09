namespace ZonderqOS.Commands
{
    public class CmdLsblk : ICommand
    {
        public string Name => "lsblk";
        public string Description => "List block devices and partitions";
        public void Execute(string[] args, ref string currentPath)
        {
            Disk.ListDisks();
            CommandIO.LastCommandSuccess = true;
        }
    }
}
