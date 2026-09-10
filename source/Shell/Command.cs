using System;
using System.Collections.Generic;
using System.Text;
using ZonderqOS.Commands;

namespace ZonderqOS
{
    public static class Command
    {
        private static readonly List<ICommand> _commands = new List<ICommand>();

        public static void Initialize()
        {
            _commands.Clear();
            _commands.Add(new CmdPwd());
            _commands.Add(new CmdCd());
            _commands.Add(new CmdLs());
            _commands.Add(new CmdMkdir());
            _commands.Add(new CmdRmdir());
            _commands.Add(new CmdCat());
            _commands.Add(new CmdWrite());
            _commands.Add(new CmdTouch());
            _commands.Add(new CmdAppend());
            _commands.Add(new CmdCp());
            _commands.Add(new CmdMv());
            _commands.Add(new CmdRm());
            _commands.Add(new CmdSpace());
            _commands.Add(new CmdFormat());
            _commands.Add(new CmdLsblk());
            _commands.Add(new CmdMount());
            _commands.Add(new CmdUmount());
            _commands.Add(new CmdStat());
            _commands.Add(new CmdHexdump());
            _commands.Add(new CmdViewLog());
            _commands.Add(new CmdClear());
            _commands.Add(new CmdSysInfo());
            _commands.Add(new CmdGrep());
            _commands.Add(new CmdHead());
            _commands.Add(new CmdEcho());
            _commands.Add(new CmdWhoami());
            _commands.Add(new CmdGui());
            _commands.Add(new CmdHelp(_commands));
            _commands.Add(new CmdMkpart());
            _commands.Add(new CmdShutDown());
            _commands.Add(new CmdReboot());
            _commands.Add(new CmdUserAdd());
            _commands.Add(new CmdSu());
            _commands.Add(new CmdLogout());
            _commands.Add(new CmdSh());
            _commands.Add(new CmdAudit());
            _commands.Add(new CmdNano());
            _commands.Add(new CmdChmod());
            _commands.Add(new CmdFree());
            _commands.Add(new CmdPs());
            _commands.Add(new CmdSysmond());
            _commands.Add(new CmdNetwork());
            _commands.Add(new CmdKill());
            _commands.Add(new CmdEnv());
            _commands.Add(new CmdExport());
            _commands.Add(new CmdDns());
        }

        public static void Run(string fullInput, ref string currentPath)
        {
            if (string.IsNullOrWhiteSpace(fullInput))
                return;

            fullInput = EnvironmentExpander.Expand(fullInput, currentPath);
            List<string> semiCommands = SplitOutsideQuotes(fullInput, ";");

            for (int i = 0; i < semiCommands.Count; i++)
            {
                string block = semiCommands[i].Trim();
                if (string.IsNullOrEmpty(block))
                    continue;

                List<string> andCommands = SplitOutsideQuotes(block, "&&");
                for (int j = 0; j < andCommands.Count; j++)
                {
                    string pipelineCmd = andCommands[j].Trim();
                    if (string.IsNullOrEmpty(pipelineCmd))
                        continue;

                    ExecutePipeline(pipelineCmd, ref currentPath);
                    if (!CommandIO.LastCommandSuccess)
                        break;
                }
            }
        }

        private static void ExecutePipeline(string pipelineStr, ref string currentPath)
        {
            List<string> pipeParts = SplitOutsideQuotes(pipelineStr, "|");
            string pipedInput = null;

            try
            {
                for (int i = 0; i < pipeParts.Count; i++)
                {
                    string singleCmdStr = pipeParts[i].Trim();
                    if (string.IsNullOrEmpty(singleCmdStr))
                        continue;

                    CommandIO.SetInput(pipedInput);
                    bool isIntermediate = i < pipeParts.Count - 1;

                    if (isIntermediate)
                    {
                        string captured = string.Empty;
                        CommandIO.StartRedirection();
                        try
                        {
                            ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath);
                        }
                        finally
                        {
                            captured = CommandIO.EndRedirection();
                        }

                        pipedInput = captured;
                    }
                    else
                    {
                        ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath);
                    }
                }
            }
            finally
            {
                CommandIO.SetInput(null);
            }
        }

        private static void ExecuteSingleCommandWithRedirection(string commandLine, ref string currentPath)
        {
            string redirectPath = null;
            bool appendMode = false;
            int redirectIndex = FindRedirection(commandLine, out appendMode);
            string commandPart = commandLine;

            if (redirectIndex >= 0)
            {
                commandPart = commandLine.Substring(0, redirectIndex);
                redirectPath = commandLine.Substring(redirectIndex + (appendMode ? 2 : 1)).Trim();

                if (string.IsNullOrEmpty(redirectPath))
                {
                    WriteMessage.WriteError("Missing redirection target path.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                redirectPath = UnquotePath(redirectPath);
                if (string.IsNullOrEmpty(redirectPath))
                {
                    WriteMessage.WriteError("Invalid redirection target path.", "CMD");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }

            string[] words = Tokenize(commandPart);
            if (words.Length == 0)
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string cmdName = words[0].ToLower();
            ICommand targetCmd = null;

            for (int i = 0; i < _commands.Count; i++)
            {
                if (_commands[i].Name == cmdName)
                {
                    targetCmd = _commands[i];
                    break;
                }
            }

            if (targetCmd == null)
            {
                WriteMessage.WriteError($"Unknown command: {cmdName}", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                if (!string.IsNullOrEmpty(redirectPath))
                {
                    string resolvedPath = PathResolver.GetAbsolutePath(currentPath, redirectPath);
                    string output = string.Empty;

                    CommandIO.StartRedirection();
                    try
                    {
                        targetCmd.Execute(words, ref currentPath);
                    }
                    finally
                    {
                        output = CommandIO.EndRedirection();
                    }

                    if (appendMode)
                        Disk.AppendFile(resolvedPath, output.TrimEnd('\r', '\n'));
                    else
                        Disk.CreateFile(resolvedPath, output.TrimEnd('\r', '\n'));
                }
                else
                {
                    targetCmd.Execute(words, ref currentPath);
                }
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Command '{cmdName}' execution failed: {ex.Message}", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static int FindRedirection(string value, out bool appendMode)
        {
            appendMode = false;
            bool inDoubleQuotes = false;
            bool inSingleQuotes = false;
            bool escaped = false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '"' && !inSingleQuotes)
                {
                    inDoubleQuotes = !inDoubleQuotes;
                    continue;
                }

                if (c == '\'' && !inDoubleQuotes)
                {
                    inSingleQuotes = !inSingleQuotes;
                    continue;
                }

                if (!inDoubleQuotes && !inSingleQuotes && c == '>')
                {
                    appendMode = i + 1 < value.Length && value[i + 1] == '>';
                    return i;
                }
            }

            return -1;
        }

        private static List<string> SplitOutsideQuotes(string input, string separator)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input))
                return result;

            var current = new StringBuilder();
            bool inDoubleQuotes = false;
            bool inSingleQuotes = false;
            bool escaped = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (escaped)
                {
                    current.Append(c);
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    current.Append(c);
                    escaped = true;
                    continue;
                }

                if (c == '"' && !inSingleQuotes)
                {
                    inDoubleQuotes = !inDoubleQuotes;
                    current.Append(c);
                    continue;
                }

                if (c == '\'' && !inDoubleQuotes)
                {
                    inSingleQuotes = !inSingleQuotes;
                    current.Append(c);
                    continue;
                }

                if (!inDoubleQuotes && !inSingleQuotes && MatchesAt(input, separator, i))
                {
                    result.Add(current.ToString());
                    current.Clear();
                    i += separator.Length - 1;
                    continue;
                }

                current.Append(c);
            }

            result.Add(current.ToString());
            return result;
        }

        private static bool MatchesAt(string value, string separator, int index)
        {
            if (index + separator.Length > value.Length)
                return false;

            for (int i = 0; i < separator.Length; i++)
            {
                if (value[index + i] != separator[i])
                    return false;
            }

            return true;
        }

        private static string[] Tokenize(string commandPart)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();
            bool inDoubleQuotes = false;
            bool inSingleQuotes = false;
            bool escaped = false;
            bool tokenStarted = false;

            for (int i = 0; i < commandPart.Length; i++)
            {
                char c = commandPart[i];

                if (escaped)
                {
                    current.Append(c);
                    tokenStarted = true;
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    tokenStarted = true;
                    continue;
                }

                if (c == '"' && !inSingleQuotes)
                {
                    inDoubleQuotes = !inDoubleQuotes;
                    tokenStarted = true;
                    continue;
                }

                if (c == '\'' && !inDoubleQuotes)
                {
                    inSingleQuotes = !inSingleQuotes;
                    tokenStarted = true;
                    continue;
                }

                if (!inDoubleQuotes && !inSingleQuotes && char.IsWhiteSpace(c))
                {
                    if (tokenStarted)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                        tokenStarted = false;
                    }
                    continue;
                }

                current.Append(c);
                tokenStarted = true;
            }

            if (escaped)
                current.Append('\\');

            if (tokenStarted)
                tokens.Add(current.ToString());

            return tokens.ToArray();
        }

        private static string UnquotePath(string value)
        {
            string path = value.Trim();
            if (path.Length >= 2)
            {
                char first = path[0];
                char last = path[path.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\''))
                    path = path.Substring(1, path.Length - 2);
            }

            return path.Replace("\\\"", "\"").Replace("\\'", "'");
        }
    }
}
