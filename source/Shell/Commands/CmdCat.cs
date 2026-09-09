using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdCat : ICommand
    {
        public string Name => "cat";
        public string Description => "Read text file contents";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
                if (File.Exists(path)) 
                {
                    CommandIO.WriteLine(Disk.ReadFile(path));
                    CommandIO.LastCommandSuccess = true;
                }
                else 
                {
                    WriteMessage.WriteError($"File does not exist: {path}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else 
            {
                WriteMessage.WriteError("Usage: cat <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
