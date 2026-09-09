using System;

namespace ZonderqOS.Commands
{
    public class CmdClear : ICommand
    {
        public string Name => "clear";
        public string Description => "Clear terminal screen";

        public void Execute(string[] args, ref string currentPath)
        {
            // Console.Clear() cannot clear the framebuffer-backed GUI terminal.
            // Emit a control marker that TerminalApp translates into ClearOutput().
            CommandIO.WriteLine("\u0001GUI_CLEAR\u0001");
            Console.Clear();
            CommandIO.LastCommandSuccess = true;
        }
    }
}
