using System;

namespace ZonderqOS.Commands
{
    public class CmdClear : ICommand
    {
        public string Name => "clear";
        public string Description => "Clear console screen";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("\u0001GUI_CLEAR\u0001");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
