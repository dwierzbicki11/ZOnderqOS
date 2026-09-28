namespace ZonderqOS.Commands
{
    public sealed class CmdUnalias : ICommand
    {
        public string Name => "unalias";
        public string Description => "Remove a shell alias";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 2)
            {
                WriteMessage.WriteError("Usage: unalias <name>", "ALIAS");
                CommandIO.LastCommandSuccess = false;
                return;
            }
            CommandIO.LastCommandSuccess = AliasManager.Remove(args[1]);
        }
    }
}
