using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdAudit : ICommand
    {
        public string Name => "audit";
        public string Description => "Review security authentication logs";

        public void Execute(string[] args, ref string currentPath)
        {
            if (SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Access denied. Root privileges required to read audit logs.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthorized access attempt to audit logs.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string logPath = @"/var/log/auth.log";
            
            try
            {
                if (File.Exists(logPath))
                {
                    string[] lines = File.ReadAllLines(logPath);
                    CommandIO.WriteLine("=== ZonderqOS Security Audit Trail ===");
                    
                    int displayCount = args.Length > 1 && int.TryParse(args[1], out int parsed) ? parsed : 15;
                    int start = Math.Max(0, lines.Length - displayCount);

                    for (int i = start; i < lines.Length; i++)
                    {
                        CommandIO.WriteLine(lines[i]);
                    }
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Audit log file not found.", "SEC");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Audit error: {ex.Message}", "SEC");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}