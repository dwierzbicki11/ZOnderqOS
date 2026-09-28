using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public sealed class CmdWc : ICommand
    {
        public string Name => "wc";
        public string Description => "Count lines, words and characters in a file or pipe";

        public void Execute(string[] args, ref string currentPath)
        {
            if (CommandIO.HasInput)
            {
                Count(new StringReader(CommandIO.GetInput()), "pipe");
                return;
            }

            if (args.Length != 2)
            {
                WriteMessage.WriteError("Usage: wc <file> or pipe into wc", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[1]);
            if (!File.Exists(path))
            {
                WriteMessage.WriteError("File does not exist: " + path, "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError("Permission denied: Cannot read " + path, "SEC");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                using (var reader = new StreamReader(path))
                    Count(reader, path);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("wc read error: " + ex.Message, "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void Count(TextReader reader, string label)
        {
            long lines = 0;
            long words = 0;
            long characters = 0;
            bool inWord = false;

            int value;
            while ((value = reader.Read()) >= 0)
            {
                char c = (char)value;
                characters++;

                if (c == '\n')
                    lines++;

                if (char.IsWhiteSpace(c))
                {
                    if (inWord)
                    {
                        words++;
                        inWord = false;
                    }
                }
                else
                {
                    inWord = true;
                }
            }

            if (inWord)
                words++;

            CommandIO.WriteLine(lines + " " + words + " " + characters + " " + label);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
