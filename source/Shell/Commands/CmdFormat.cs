namespace ZonderqOS.Commands
{
    public class CmdFormat : ICommand
    {
        public string Name => "format";
        public string Description => "Format partition to FAT32 (format <partition_id>)";
        
        public void Execute(string[] args, ref string currentPath)
        {
            string targetId = args.Length > 1 ? args[1] : "0";
            Disk.FormatPartition(targetId);
        }
    }
}