using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdHead : ICommand
    {
        public string Name => "head";
        public string Description => "Output first lines of a file or pipe ([file] [lines])";

        private const int MaxHeadLines = 10000;

        public void Execute(string[] args, ref string currentPath)
        {
            int lineCount = 10;

            if (CommandIO.HasInput)
            {
                if (args.Length > 1 && !TryParseLineCount(args[1], out lineCount))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                using (var reader = new StringReader(CommandIO.GetInput()))
                    PrintHead(reader, lineCount);
                return;
            }

            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: head <file> [lines] or pipe into head", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string filePath = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (args.Length > 2 && !TryParseLineCount(args[2], out lineCount))
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!File.Exists(filePath))
            {
                WriteMessage.WriteError($"File does not exist: {filePath}", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(filePath, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {filePath}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized head attempt on {filePath} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                using (var reader = new StreamReader(filePath))
                    PrintHead(reader, lineCount);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Head read error: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static bool TryParseLineCount(string value, out int lineCount)
        {
            if (!int.TryParse(value, out lineCount) || lineCount <= 0)
            {
                WriteMessage.WriteError("Line count must be a positive integer.", "CMD");
                return false;
            }

            if (lineCount > MaxHeadLines)
            {
                WriteMessage.WriteError($"Line count exceeds safety limit ({MaxHeadLines}).", "CMD");
                return false;
            }

            return true;
        }

        private static void PrintHead(TextReader reader, int lineCount)
        {
            CommandIO.WriteLine($"--- First up to {lineCount} lines ---");

            int printed = 0;
            string line;
            while (printed < lineCount && (line = reader.ReadLine()) != null)
            {
                CommandIO.WriteLine(line);
                printed++;
            }

            CommandIO.LastCommandSuccess = true;
        }
    }
}
