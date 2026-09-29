using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public sealed class CmdTail : ICommand
    {
        private const int MaxTailLines = 10000;

        public string Name => "tail";
        public string Description => "Output last lines of a file or pipe ([file] [lines])";

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
                    PrintTail(reader, lineCount);
                return;
            }

            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: tail <file> [lines] or pipe into tail", "CMD");
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
                WriteMessage.WriteError("File does not exist: " + filePath, "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(filePath, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError("Permission denied: Cannot read " + filePath, "SEC");
                SecurityLogger.LogEvent("WARN",
                    "Unauthorized tail attempt on " + filePath + " by " + SecurityContext.CurrentUser);
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                using (var reader = new StreamReader(filePath))
                    PrintTail(reader, lineCount);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Tail read error: " + ex.Message, "CMD");
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

            if (lineCount > MaxTailLines)
            {
                WriteMessage.WriteError("Line count exceeds safety limit (" + MaxTailLines + ").", "CMD");
                return false;
            }

            return true;
        }

        private static void PrintTail(TextReader reader, int lineCount)
        {
            string[] ring = new string[lineCount];
            int write = 0;
            int count = 0;

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                ring[write] = line;
                write = (write + 1) % lineCount;
                if (count < lineCount)
                    count++;
            }

            CommandIO.WriteLine("--- Last up to " + lineCount + " lines ---");
            int start = count == lineCount ? write : 0;
            for (int i = 0; i < count; i++)
                CommandIO.WriteLine(ring[(start + i) % lineCount]);

            CommandIO.LastCommandSuccess = true;
        }
    }
}
