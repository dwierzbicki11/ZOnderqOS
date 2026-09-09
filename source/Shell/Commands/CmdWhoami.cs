namespace ZonderqOS.Commands
{
    public class CmdWhoami : ICommand
    {
        public string Name => "whoami";
        public string Description => "Display active user identity";

        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine(SecurityContext.CurrentUser);
            CommandIO.LastCommandSuccess = true;
        }
    }
}