using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    public class CmdAudit : ICommand
    {
        public string Name => "audit";
        public string Description => "Review security authentication logs";

        private const int DefaultDisplayCount = 15;
        private const int MaxDisplayCount = 500;
        private const int MaxPhysicalLineLength = 16 * 1024;

        public void Execute(string[] args, ref string currentPath)
        {
            if (SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Access denied. Root privileges required to read audit logs.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthorized access attempt to audit logs.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            const string logPath = "/var/log/auth.log";
            int displayCount = DefaultDisplayCount;
            if (args.Length > 1)
            {
                if (!int.TryParse(args[1], out displayCount) || displayCount <= 0 || displayCount > MaxDisplayCount)
                {
                    WriteMessage.WriteError($"Usage: audit [1-{MaxDisplayCount}]", "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            try
            {
                if (!File.Exists(logPath))
                {
                    WriteMessage.WriteError("Audit log file not found.", "SEC");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string[] recent = new string[displayCount];
                int totalLines = 0;

                using (var reader = new StreamReader(logPath))
                {
                    var lineBuffer = new StringBuilder(Math.Min(256, MaxPhysicalLineLength));
                    while (TryReadBoundedLine(reader, lineBuffer, out string line))
                    {
                        recent[totalLines % displayCount] = line;
                        totalLines++;
                    }
                }

                CommandIO.WriteLine("=== ZonderqOS Security Audit Trail ===");
                int linesToShow = Math.Min(totalLines, displayCount);
                int start = totalLines > displayCount ? totalLines % displayCount : 0;

                for (int i = 0; i < linesToShow; i++)
                {
                    string line = recent[(start + i) % displayCount];
                    if (line != null)
                        CommandIO.WriteLine(line);
                }

                CommandIO.LastCommandSuccess = true;
            }
            catch (InvalidDataException ex)
            {
                WriteMessage.WriteError($"Audit log rejected: {ex.Message}", "SEC");
                CommandIO.LastCommandSuccess = false;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Audit error: {ex.Message}", "SEC");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static bool TryReadBoundedLine(StreamReader reader, StringBuilder buffer, out string line)
        {
            buffer.Clear();
            bool readAny = false;

            while (true)
            {
                int value = reader.Read();
                if (value < 0)
                {
                    line = readAny ? buffer.ToString() : null;
                    return readAny;
                }

                readAny = true;
                char ch = (char)value;
                if (ch == '\n')
                {
                    line = buffer.ToString();
                    return true;
                }

                if (ch == '\r')
                    continue;

                if (buffer.Length >= MaxPhysicalLineLength)
                    throw new InvalidDataException($"physical line exceeds {MaxPhysicalLineLength} characters");

                buffer.Append(ch);
            }
        }
    }
}
