using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdHead : ICommand
    {
        public string Name => "head";
        public string Description => "Output first lines of a file or pipe ([file] [lines])";
        public void Execute(string[] args, ref string currentPath)
        {
            string[] lines = null;
            int lineCount = 10;

            if (CommandIO.HasInput)
            {
                lines = CommandIO.GetInput().Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                if (args.Length > 1 && int.TryParse(args[1], out int parsed)) lineCount = parsed;
            }
            else
            {
                if (args.Length > 1)
                {
                    string filePath = PathResolver.GetAbsolutePath(currentPath, args[1]);
                    if (args.Length > 2 && int.TryParse(args[2], out int parsedLines)) lineCount = parsedLines;

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
                    WriteMessage.WriteError("Usage: head <file> [lines] or pipe into head", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            CommandIO.WriteLine($"--- First {Math.Min(lineCount, lines.Length)} lines ---");
            for (int i = 0; i < Math.Min(lineCount, lines.Length); i++)
            {
                CommandIO.WriteLine(lines[i]);
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
