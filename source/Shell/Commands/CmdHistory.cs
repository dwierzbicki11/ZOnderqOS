namespace ZonderqOS.Commands
{
    public sealed class CmdHistory : ICommand
    {
        private readonly string[] entries = new string[128];
        public string Name => "history";
        public string Description => "Show or clear in-memory command history";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1 && args[1] == "-c")
            {
                ShellHistory.Clear();
                CommandIO.LastCommandSuccess = true;
                return;
            }

            int count = ShellHistory.CopyTo(entries);
            for (int i = 0; i < count; i++)
            {
                CommandIO.WriteLine((i + 1) + "  " + entries[i]);
                entries[i] = null;
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
