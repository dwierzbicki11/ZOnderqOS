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
            CommandIO.WriteLine($"--- Grep results for '{pattern}' ---");

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!line.Contains(pattern))
                    continue;

                CommandIO.WriteLine("  " + line);
                count++;
            }

            CommandIO.WriteLine($"Found {count} matching line(s).");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
