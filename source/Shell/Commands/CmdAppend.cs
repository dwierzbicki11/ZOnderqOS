using System;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public class CmdAppend : ICommand
    {
        private const int MaxAppendCharacters = 64 * 1024;

        public string Name => "append";
        public string Description => "Append text to file";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                WriteMessage.WriteError("Permission denied. Authentication required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthenticated append attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: append <file> <content>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (VirtualFs.IsReadOnlyPath(path))
            {
                WriteMessage.WriteError($"Read-only virtual filesystem: cannot append to {path}", "FS");
                SecurityLogger.LogEvent("WARN", $"Rejected append on read-only virtual path {path} by {SecurityContext.CurrentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (VirtualFs.IsVirtualPath(path))
            {
                try
                {
                    string virtualContent = BuildBoundedContent(args);
                    Disk.AppendFile(path, virtualContent);
                }
                catch (InvalidDataException ex)
                {
                    WriteMessage.WriteError($"Append rejected: {ex.Message}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
                return;
            }

            if (Directory.Exists(path))
            {
                WriteMessage.WriteError($"Target is a directory: {path}", "FS");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            bool existed = File.Exists(path);
            bool permitted = existed
                ? PermissionManager.CanWrite(path, SecurityContext.CurrentUser)
                : PermissionManager.CanCreate(path, SecurityContext.CurrentUser);
            if (!permitted)
            {
                WriteMessage.WriteError($"Permission denied: Cannot append to {path}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized append attempt on {path} by {SecurityContext.CurrentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                string content = BuildBoundedContent(args);
                Disk.AppendFile(path, content);

                if (!CommandIO.LastCommandSuccess || !File.Exists(path))
                {
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                if (!existed)
                    PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 644);
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
