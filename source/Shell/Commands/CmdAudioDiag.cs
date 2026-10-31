using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    /// <summary>PCI audio survey only; does not initialize a codec or play sound.</summary>
    public sealed class CmdAudioDiag : ICommand
    {
        public string Name => "audiodiag";
        public string Description => "List PCI audio controllers (read-only; no driver binding or playback)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: audiodiag");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.LastCommandSuccess = AudioPciDiagnostics.PrintReport(true);
        }
    }
}
