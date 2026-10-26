namespace ZonderqOS.Commands
{
    // Preserve the shell name, but NEVER claim managed SMP coverage on the stock runtime.
    public sealed class CmdSmtCheck : ICommand
    {
        public string Name => "smtcheck";
        public string Description => "SMP/SMT test unavailable (waiting for upstream Cosmos support)";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("[SMTCHECK] DISABLED: private SMP/SMT implementation was archived.");
            CommandIO.WriteLine("ZonderqOS runs the unmodified Cosmos 3.0.95 scheduler.");
            CommandIO.WriteLine("No multicore worker distribution has been certified.");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
