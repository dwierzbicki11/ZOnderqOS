using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdViewLog : ICommand
    {
        public string Name => "viewlog";
        public string Description => "Preview error_log.txt file";
        public void Execute(string[] args, ref string currentPath)
        {
            string path = @"/var/error_log.txt";
            if (File.Exists(path)) 
            {
                CommandIO.WriteLine(Disk.ReadFile(path));
                CommandIO.LastCommandSuccess = true;
            }
            else 
            {
                WriteMessage.WriteError("File does not exist: /mnt/error_log.txt", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
