namespace ZonderqOS.Commands
{
    public class CmdDns : ICommand
    {
        public string Name => "dns";
        public string Description => "DNS resolver utility";
        public void Execute(string[] args, ref string currentPath) { }
    }
}
