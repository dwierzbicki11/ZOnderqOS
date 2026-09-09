using System;

namespace ZonderqOS.Commands
{
    public class CmdEcho : ICommand
    {
        public string Name => "echo";
        public string Description => "Print text to console";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                CommandIO.WriteLine(string.Join(" ", args, 1, args.Length - 1));
            }
            else
            {
                CommandIO.WriteLine("");
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
