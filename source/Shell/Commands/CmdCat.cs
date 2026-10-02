using System;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdCat : ICommand
    {
        private const int MaxLineLength = 16 * 1024;
        private const int MaxCharacters = 8 * 1024 * 1024;

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

            if (VirtualFs.IsVirtualPath(path))
            {
                if (VirtualFs.TryRead(path, out string virtualContent))
                {
                    CommandIO.Write(virtualContent);
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError($"virtual filesystem entry does not exist: {path}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

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
                    var line = new StringBuilder(256);
                    int total = 0;
                    int value;

                    while ((value = reader.Read()) >= 0)
                    {
                        total++;
                        if (total > MaxCharacters)
                            throw new InvalidOperationException("cat input limit reached (8 Mi characters)");

                        char ch = (char)value;
                        if (ch == '\n')
                        {
                            if (line.Length > 0 && line[line.Length - 1] == '\r')
                                line.Length--;
                            CommandIO.WriteLine(line.ToString());
                            line.Clear();
                            continue;
                        }

                        if (line.Length >= MaxLineLength)
                            throw new InvalidOperationException("cat line limit reached (16 KiB)");

                        line.Append(ch);
                    }

                    if (line.Length > 0)
                    {
                        if (line[line.Length - 1] == '\r')
                            line.Length--;
                        CommandIO.Write(line.ToString());
                    }
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
