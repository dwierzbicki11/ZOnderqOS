namespace ZonderqOS.Commands
{
    public class CmdPwd : ICommand
    {
        public string Name => "pwd";
        public string Description => "Displays current working directory";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine($"Current directory: {currentPath}");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
