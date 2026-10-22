using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    /// <summary>
    /// Hardware-only NIC enumeration. Does not activate network protocols.
    /// </summary>
    public sealed class CmdNicDiag : ICommand
    {
        public string Name => "nicdiag";
        public string Description => "Detect PCI Ethernet/Wi-Fi controllers (read-only, no driver/DHCP)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: nicdiag");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.LastCommandSuccess = NetworkPciDiagnostics.PrintReport(true);
        }
    }
}
