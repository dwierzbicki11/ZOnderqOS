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
            const string path = "/var/error_log.txt";
            if (!File.Exists(path))
            {
                WriteMessage.WriteError($"File does not exist: {path}", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot read {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized viewlog attempt by {SecurityContext.CurrentUser}");
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
                WriteMessage.WriteError($"Log read error: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
