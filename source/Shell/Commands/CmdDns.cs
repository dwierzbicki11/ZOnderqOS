namespace ZonderqOS.Commands
{
    public class CmdDns : ICommand
    {
        public string Name => "dns";
        public string Description => "Resolve a DNS name: dns <host>";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                CommandIO.WriteLine("Usage: dns <host>");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string host = args[1].Trim();
            if (!Network.IsReady)
            {
                CommandIO.WriteLine("dns: network is not configured");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string address = Network.ResolveDns(host);
            if (string.IsNullOrEmpty(address))
            {
                CommandIO.WriteLine("dns: could not resolve " + host);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine(host + " -> " + address);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
