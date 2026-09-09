namespace ZonderqOS.Commands
{
    public class CmdHexdump : ICommand
    {
        public string Name => "hexdump";
        public string Description => "Print raw hardware sector data in HEX";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2 && int.TryParse(args[1], out int id) && ulong.TryParse(args[2], out ulong sec))
            {
                Disk.HexDump(id, sec);
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("Usage: hexdump <disk_id> <sector_number>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
