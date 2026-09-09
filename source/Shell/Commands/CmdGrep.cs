using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdGrep : ICommand
    {
        public string Name => "grep";
        public string Description => "Search pattern in file or pipe (<pattern> [file])";
        public void Execute(string[] args, ref string currentPath)
        {
            string pattern = null;
            string[] lines = null;

            if (CommandIO.HasInput)
            {
                if (args.Length > 1)
                {
                    pattern = args[1];
                    lines = CommandIO.GetInput().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                }
                else
                {
                    WriteMessage.WriteError("Usage: <cmd> | grep <pattern>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }
            else
            {
                if (args.Length > 2)
                {
                    pattern = args[1];
                    string filePath = PathResolver.GetAbsolutePath(currentPath, args[2]);
                    if (File.Exists(filePath))
                    {
                        lines = File.ReadAllLines(filePath);
                    }
                    else
                    {
                        WriteMessage.WriteError($"File does not exist: {filePath}", "CMD");
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                }
                else
                {
                    WriteMessage.WriteError("Usage: grep <pattern> <file>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            int count = 0;
            CommandIO.WriteLine($"--- Grep results for '{pattern}' ---");
            foreach (string line in lines)
            {
                if (line.Contains(pattern))
                {
                    CommandIO.WriteLine($"  {line}");
                    count++;
                }
            }
            CommandIO.WriteLine($"Found {count} matching line(s).");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
