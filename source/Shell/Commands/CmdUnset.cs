namespace ZonderqOS.Commands
{
    public sealed class CmdUnset : ICommand
    {
        public string Name => "unset";
        public string Description => "Remove a non-core environment variable";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 2)
            {
                WriteMessage.WriteError("Usage: unset <KEY>", "ENV");
                CommandIO.LastCommandSuccess = false;
                return;
            }
            CommandIO.LastCommandSuccess = EnvironmentManager.TryUnset(args[1]);
        }
    }
}
