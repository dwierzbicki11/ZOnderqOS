using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    public class CmdGrep : ICommand
    {
        private const int MaxLineLength = 16 * 1024;
        private const int MaxMatches = 512;
        private const int MaxScannedLines = 100000;

        public string Name => "grep";
        public string Description => "Search pattern in file or pipe (<pattern> [file])";

        public void Execute(string[] args, ref string currentPath)
        {
            if (CommandIO.HasInput)
            {
                if (args.Length <= 1)
                {
                    WriteMessage.WriteError("Usage: <cmd> | grep <pattern>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                using (var reader = new StringReader(CommandIO.GetInput()))
                    Scan(reader, args[1]);
                return;
            }

            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: grep <pattern> <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string pattern = args[1];
            string filePath = PathResolver.GetAbsolutePath(currentPath, args[2]);
            if (!File.Exists(filePath))
            {
                WriteMessage.WriteError($"File does not exist: {filePath}", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(filePath, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {filePath}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized grep attempt on {filePath} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                using (var reader = new StreamReader(filePath))
                    Scan(reader, pattern);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Grep read error: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void Scan(TextReader reader, string pattern)
        {
            int count = 0;
            int scannedLines = 0;
            var lineBuffer = new StringBuilder(256);
            CommandIO.WriteLine($"--- Grep results for '{pattern}' ---");

            while (TryReadBoundedLine(reader, lineBuffer, out bool lineTooLong))
            {
                scannedLines++;
                if (lineTooLong)
                {
                    WriteMessage.WriteError($"grep: line exceeds {MaxLineLength} characters; input rejected.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (lineBuffer.ToString().Contains(pattern))
                {
                    CommandIO.WriteLine("  " + lineBuffer.ToString());
                    count++;
                    if (count >= MaxMatches)
                    {
                        CommandIO.WriteLine($"Result limit reached ({MaxMatches} matching lines). Output truncated.");
                        CommandIO.LastCommandSuccess = true;
                        return;
                    }
                }

                if (scannedLines >= MaxScannedLines)
                {
                    CommandIO.WriteLine($"Scan limit reached ({MaxScannedLines} lines). Output truncated.");
                    CommandIO.LastCommandSuccess = true;
                    return;
                }
            }

            CommandIO.WriteLine($"Found {count} matching line(s).");
            CommandIO.LastCommandSuccess = true;
        }

        private static bool TryReadBoundedLine(TextReader reader, StringBuilder buffer, out bool lineTooLong)
        {
            buffer.Clear();
            lineTooLong = false;
            bool sawData = false;

            while (true)
            {
                int value = reader.Read();
                if (value < 0)
                    return sawData;

                sawData = true;
                char c = (char)value;
                if (c == '\n')
                    return true;
                if (c == '\r')
                    continue;

                if (buffer.Length >= MaxLineLength)
                {
                    lineTooLong = true;
                    while ((value = reader.Read()) >= 0 && value != '\n')
                    {
                    }
                    return true;
                }

                buffer.Append(c);
            }
        }
    }
}
