namespace ZonderqOS.Commands
{
    public sealed class CmdTty : ICommand
    {
        public string Name => "tty";
        public string Description => "Print the current terminal device";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 1)
            {
                CommandIO.WriteLine("Usage: tty");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("/dev/tty");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
