namespace ZonderqOS.Commands
{
    public class CmdSpace : ICommand
    {
        public string Name => "space";
        public string Description => "Display partition capacity & free space";
        public void Execute(string[] args, ref string currentPath)
        {
            Disk.GetSpace();
            CommandIO.LastCommandSuccess = true;
        }
    }
}
