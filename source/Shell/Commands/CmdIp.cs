namespace ZonderqOS.Commands
{
    public class CmdIp : ICommand
    {
        public string Name => "ip";
        public string Description => "Network IP configuration";
        public void Execute(string[] args, ref string currentPath) { }
    }
}
