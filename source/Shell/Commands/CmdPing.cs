namespace ZonderqOS.Commands
{
    public class CmdPing : ICommand
    {
        public string Name => "ping";
        public string Description => "Network ping utility";
        public void Execute(string[] args, ref string currentPath) { }
    }
}
