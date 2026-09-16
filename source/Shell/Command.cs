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

#if ARCH_ARM64
            // QEMU ARM64 profile: use the real shell dispatcher and command
            // implementations that do not depend on storage, networking, GUI
            // startup or the scheduler. Keyboard input is provided by Cosmos'
            // VirtIO-MMIO keyboard backend through System.Console.
            _commands.Add(new CmdPwd());
            _commands.Add(new CmdClear());
            _commands.Add(new CmdEcho());
            _commands.Add(new CmdHelp(_commands));
#else
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
            _commands.Add(new CmdRecovery());
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
            _commands.Add(new CmdKill());
            _commands.Add(new CmdEnv());
            _commands.Add(new CmdExport());
#endif
        }

        internal static string[] GetCommandNames()
        {
            string[] names = new string[_commands.Count];
            for (int i = 0; i < _commands.Count; i++)
                names[i] = _commands[i].Name;
            return names;
        }

        public static void Run(string fullInput, ref string currentPath)
        {
            if (string.IsNullOrWhiteSpace(fullInput))
                return;

#if !ARCH_ARM64
            fullInput = EnvironmentExpander.Expand(fullInput, currentPath);
#endif
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
                        if (!CommandIO.LastCommandSuccess)
                            break;
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
                CommandIO.ResetRedirection();
            }
        }

        private static void ExecuteSingleCommandWithRedirection(string commandLine, ref string currentPath)
        {
            bool append;
            string redirectPath;
            string commandPart;

            ParseOutputRedirection(commandLine, out commandPart, out redirectPath, out append);

            if (redirectPath == null)
            {
                ExecuteSingleCommand(commandPart, ref currentPath);
                return;
            }

#if ARCH_ARM64
            CommandIO.WriteLine("redirection: unavailable in the ARM64 QEMU shell profile");
            CommandIO.LastCommandSuccess = false;
#else
            string output = string.Empty;
            CommandIO.StartRedirection();
            try
            {
                ExecuteSingleCommand(commandPart, ref currentPath);
            }
            finally
            {
                output = CommandIO.EndRedirection();
            }

            if (!CommandIO.LastCommandSuccess)
                return;

            string resolvedPath = PathResolver.Resolve(redirectPath, currentPath);

            try
            {
                if (append && File.Exists(resolvedPath))
                {
                    string previous = File.ReadAllText(resolvedPath);
                    File.WriteAllText(resolvedPath, previous + output);
                }
                else
                {
                    File.WriteAllText(resolvedPath, output);
                }

                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("redirection failed: " + ex.Message, "SHELL");
                CommandIO.LastCommandSuccess = false;
            }
#endif
        }

        private static void ExecuteSingleCommand(string input, ref string currentPath)
        {
            List<string> tokens = Tokenize(input);
            if (tokens.Count == 0)
            {
                CommandIO.LastCommandSuccess = true;
                return;
            }

            string commandName = tokens[0];
            string[] args = new string[tokens.Count - 1];
            for (int i = 1; i < tokens.Count; i++)
                args[i - 1] = tokens[i];

            ICommand command = FindCommand(commandName);
            if (command == null)
            {
                CommandIO.WriteLine(commandName + ": command not found");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                command.Execute(args, ref currentPath);
            }
            catch (Exception ex)
            {
#if ARCH_ARM64
                CommandIO.WriteLine(commandName + ": " + ex.Message);
#else
                WriteMessage.WriteError(commandName + ": " + ex.Message, "SHELL");
#endif
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static ICommand FindCommand(string name)
        {
            for (int i = 0; i < _commands.Count; i++)
            {
                if (string.Equals(_commands[i].Name, name, StringComparison.OrdinalIgnoreCase))
                    return _commands[i];
            }

            return null;
        }

        private static List<string> Tokenize(string input)
        {
            List<string> tokens = new List<string>();
            StringBuilder current = new StringBuilder();
            bool inSingle = false;
            bool inDouble = false;
            bool escaping = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (escaping)
                {
                    current.Append(c);
                    escaping = false;
                    continue;
                }

                if (c == '\\' && !inSingle)
                {
                    escaping = true;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (char.IsWhiteSpace(c) && !inSingle && !inDouble)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    continue;
                }

                current.Append(c);
            }

            if (escaping)
                current.Append('\\');

            if (current.Length > 0)
                tokens.Add(current.ToString());

            return tokens;
        }

        private static List<string> SplitOutsideQuotes(string input, string separator)
        {
            List<string> parts = new List<string>();
            int start = 0;
            bool inSingle = false;
            bool inDouble = false;
            bool escaping = false;

            for (int i = 0; i <= input.Length - separator.Length; i++)
            {
                char c = input[i];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (c == '\\' && !inSingle)
                {
                    escaping = true;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (!inSingle && !inDouble && MatchesAt(input, separator, i))
                {
                    parts.Add(input.Substring(start, i - start));
                    i += separator.Length - 1;
                    start = i + 1;
                }
            }

            parts.Add(input.Substring(start));
            return parts;
        }

        private static bool MatchesAt(string text, string value, int index)
        {
            if (index + value.Length > text.Length)
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                if (text[index + i] != value[i])
                    return false;
            }

            return true;
        }

        private static void ParseOutputRedirection(string input, out string commandPart, out string redirectPath, out bool append)
        {
            commandPart = input;
            redirectPath = null;
            append = false;

            bool inSingle = false;
            bool inDouble = false;
            bool escaping = false;

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (escaping)
                {
                    escaping = false;
                    continue;
                }

                if (c == '\\' && !inSingle)
                {
                    escaping = true;
                    continue;
                }

                if (c == '\'' && !inDouble)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (c == '"' && !inSingle)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (c != '>' || inSingle || inDouble)
                    continue;

                append = i + 1 < input.Length && input[i + 1] == '>';
                int pathStart = i + (append ? 2 : 1);
                commandPart = input.Substring(0, i).TrimEnd();
                redirectPath = input.Substring(pathStart).Trim();

                if (redirectPath.Length == 0)
                    redirectPath = null;

                return;
            }
        }
    }
}
