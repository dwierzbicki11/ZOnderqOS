using System;
using System.IO;
using System.Text;

namespace ZonderqOS.Commands
{
    public class CmdAppend : ICommand
    {
        private const int MaxAppendCharacters = 64 * 1024;

        public string Name => "append";
        public string Description => "Append text to file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: append <file> <content>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            bool existed = File.Exists(path);

            if (existed && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot append to {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized append attempt on {path} by {SecurityContext.CurrentUser}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                string content = BuildBoundedContent(args);
                Disk.AppendFile(path, content);

                if (!existed && File.Exists(path))
                    PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);

                CommandIO.LastCommandSuccess = File.Exists(path);
            }
            catch (InvalidDataException ex)
            {
                WriteMessage.WriteError($"Append rejected: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static string BuildBoundedContent(string[] args)
        {
            var content = new StringBuilder(Math.Min(256, MaxAppendCharacters));

            for (int i = 2; i < args.Length; i++)
            {
                if (i > 2)
                    AppendBounded(content, ' ');

                string value = args[i] ?? string.Empty;
                for (int j = 0; j < value.Length; j++)
                {
                    char ch = value[j];
                    if (ch == '\\' && j + 1 < value.Length && value[j + 1] == 'n')
                    {
                        AppendBounded(content, '\n');
                        j++;
                    }
                    else
                    {
                        AppendBounded(content, ch);
                    }
                }
            }

            return content.ToString();
        }

        private static void AppendBounded(StringBuilder content, char value)
        {
            if (content.Length >= MaxAppendCharacters)
                throw new InvalidDataException($"content exceeds {MaxAppendCharacters} characters");

            content.Append(value);
        }
    }
}
