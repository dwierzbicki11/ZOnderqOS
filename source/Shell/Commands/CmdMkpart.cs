using System;

namespace ZonderqOS.Commands
{
    public class CmdMkpart : ICommand
    {
        public string Name => "mkpart";
        public string Description => "Create MBR partition table on raw disk (mkpart <disk_id>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1 && int.TryParse(args[1], out int diskId))
            {
                Disk.PartitionDisk(diskId);
            }
            else
            {
                WriteMessage.WriteError("Usage: mkpart <disk_id>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}