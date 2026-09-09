using System;
using System.Collections.Generic;

namespace ZonderqOS.Commands
{
    public class CmdHelp : ICommand
    {
        private List<ICommand> _commands;
        public CmdHelp(List<ICommand> commands) { _commands = commands; }
        
        public string Name => "help";
        public string Description => "Lists all available commands";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.WriteLine("Available commands:");
            foreach (var cmd in _commands)
            {
                CommandIO.WriteLine($"  {cmd.Name,-12} - {cmd.Description}");
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
