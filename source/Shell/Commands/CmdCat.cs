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
            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: cat <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (!File.Exists(path))
            {
                WriteMessage.WriteError($"File does not exist: {path}", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized cat attempt on {path} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                using (var reader = new StreamReader(path))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                        CommandIO.WriteLine(line);
                }

                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Could not read file: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
