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
            bool showLines = false;
            bool showWords = false;
            bool showCharacters = false;
            int optionCount = 0;

            while (optionCount + 1 < args.Length && args[optionCount + 1].StartsWith("-", StringComparison.Ordinal))
            {
                string option = args[optionCount + 1];
                if (option.Length < 2 || option.Length > 4)
                {
                    Usage();
                    return;
                }

                for (int i = 1; i < option.Length; i++)
                {
                    switch (option[i])
                    {
                        case 'l': showLines = true; break;
                        case 'w': showWords = true; break;
                        case 'c': showCharacters = true; break;
                        default:
                            WriteMessage.WriteError("wc: unsupported option -" + option[i], "CMD");
                            CommandIO.LastCommandSuccess = false;
                            return;
                    }
                }

                optionCount++;
            }

            if (!showLines && !showWords && !showCharacters)
                showLines = showWords = showCharacters = true;

            int operandIndex = optionCount + 1;
            if (CommandIO.HasInput)
            {
                if (operandIndex != args.Length)
                {
                    Usage();
                    return;
                }

                Count(new StringReader(CommandIO.GetInput()), "pipe", showLines, showWords, showCharacters);
                return;
            }

            if (operandIndex != args.Length - 1)
            {
                Usage();
                return;
            }

            string path = PathResolver.GetAbsolutePath(currentPath, args[operandIndex]);
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
                    Count(reader, path, showLines, showWords, showCharacters);
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("wc read error: " + ex.Message, "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void Usage()
        {
            WriteMessage.WriteError("Usage: wc [-lwc] <file> or pipe into wc [-lwc]", "CMD");
            CommandIO.LastCommandSuccess = false;
        }

        private static void Count(TextReader reader, string label, bool showLines, bool showWords, bool showCharacters)
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

            string output = string.Empty;
            if (showLines) output = lines.ToString();
            if (showWords) output += (output.Length == 0 ? string.Empty : " ") + words;
            if (showCharacters) output += (output.Length == 0 ? string.Empty : " ") + characters;
            CommandIO.WriteLine(output + " " + label);
            CommandIO.LastCommandSuccess = true;
        }
    }
}
