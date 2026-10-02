using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    public class CmdSh : ICommand
    {
        public string Name => "sh";
        public string Description => "Execute a shell script file (sh <script_path>)";

        private const long MaxScriptBytes = 256 * 1024;
        private const int MaxScriptCommands = 4096;
        private const int MaxScriptDepth = 8;
        private const int MaxScriptLineChars = 16 * 1024;

        [ThreadStatic]
        private static int executionDepth;

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 1)
            {
                WriteMessage.WriteError("Usage: sh <script_path>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string scriptPath = PathResolver.GetAbsolutePath(currentPath, args[1]);

            try
            {
                if (!File.Exists(scriptPath))
                {
                    WriteMessage.WriteError($"Script file does not exist: {scriptPath}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!PermissionManager.CanRead(scriptPath, SecurityContext.CurrentUser))
                {
                    WriteMessage.WriteError($"Permission denied: Cannot read script {scriptPath}", "SEC");
                    SecurityLogger.LogEvent("WARN", $"Unauthorized script read attempt on {scriptPath} by {SecurityContext.CurrentUser}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                FileInfo scriptInfo = new FileInfo(scriptPath);
                if (scriptInfo.Length > MaxScriptBytes)
                {
                    WriteMessage.WriteError($"Script is too large. Limit: {MaxScriptBytes / 1024} KB.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (executionDepth >= MaxScriptDepth)
                {
                    WriteMessage.WriteError($"Maximum nested script depth ({MaxScriptDepth}) exceeded.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                executionDepth++;
                try
                {
                    CommandIO.WriteLine($"--- Executing script: {scriptPath} ---");
                    int executedCommands = 0;
                    bool lastResult = true;
                    var lineBuffer = new StringBuilder(Math.Min(MaxScriptLineChars, 256));

                    using (var reader = new StreamReader(scriptPath))
                    {
                        while (TryReadBoundedLine(reader, lineBuffer, out bool lineTooLong))
                        {
                            if (lineTooLong)
                            {
                                WriteMessage.WriteError($"Script line is too long. Limit: {MaxScriptLineChars} characters.", "CMD");
                                CommandIO.LastCommandSuccess = false;
                                return;
                            }

                            string line = lineBuffer.ToString().Trim();
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                                continue;

                            if (executedCommands >= MaxScriptCommands)
                            {
                                WriteMessage.WriteError($"Script command limit ({MaxScriptCommands}) exceeded.", "CMD");
                                CommandIO.LastCommandSuccess = false;
                                return;
                            }

                            Command.Run(line, ref currentPath);
                            lastResult = CommandIO.LastCommandSuccess;
                            executedCommands++;
                        }
                    }

                    CommandIO.LastCommandSuccess = lastResult;
                }
                finally
                {
                    executionDepth--;
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Script execution error: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static bool TryReadBoundedLine(StreamReader reader, StringBuilder buffer, out bool lineTooLong)
        {
            buffer.Clear();
            lineTooLong = false;
            bool readAny = false;

            while (true)
            {
                int value = reader.Read();
                if (value < 0)
                    return readAny;

                readAny = true;
                char ch = (char)value;
                if (ch == '\n')
                    return true;
                if (ch == '\r')
                    continue;

                if (buffer.Length >= MaxScriptLineChars)
                {
                    lineTooLong = true;
                    return true;
                }

                buffer.Append(ch);
            }
        }
    }
}
