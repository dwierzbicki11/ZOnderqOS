using System;
using System.Collections.Generic;
using ZonderqOS.Commands;

namespace ZonderqOS
{
    public static class Command
    {
        private static List<ICommand> _commands = new List<ICommand>();

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

            // Komendy obecne w source/Shell/Commands, które wcześniej nie były
            // rejestrowane i dlatego nie działały w żadnej sesji przez Command.Run.
            _commands.Add(new CmdEnv());
            _commands.Add(new CmdExport());
            _commands.Add(new CmdDns());
        }

        public static void Run(string fullInput, ref string currentPath)
        {
            if (string.IsNullOrWhiteSpace(fullInput)) return;
            fullInput = EnvironmentExpander.Expand(fullInput, currentPath);

            // 1. Rozdzielanie po średnikach (;) - sekwencje niezależne
            string[] semiCommands = fullInput.Split(';', StringSplitOptions.RemoveEmptyEntries);

            foreach (string semiCmd in semiCommands)
            {
                string block = semiCmd.Trim();
                if (string.IsNullOrEmpty(block)) continue;

                // 2. Rozdzielanie po operatorze warunkowym (&&)
                string[] andCommands = block.Split(new string[] { "&&" }, StringSplitOptions.RemoveEmptyEntries);

                foreach (string andCmd in andCommands)
                {
                    string pipelineCmd = andCmd.Trim();
                    if (string.IsNullOrEmpty(pipelineCmd)) continue;

                    // Wykonaj potok (obsługuje '|', '>' oraz '>>')
                    ExecutePipeline(pipelineCmd, ref currentPath);

                    // Jeśli poprzednie polecenie w bloku '&&' zawiodło, przerywamy łańcuch
                    if (!CommandIO.LastCommandSuccess)
                    {
                        break;
                    }
                }
            }
        }

        private static void ExecutePipeline(string pipelineStr, ref string currentPath)
        {
            string[] pipeParts = pipelineStr.Split('|');
            string pipedInput = null;

            for (int i = 0; i < pipeParts.Length; i++)
            {
                string singleCmdStr = pipeParts[i].Trim();
                if (string.IsNullOrEmpty(singleCmdStr)) continue;

                // Przekazanie stdin z poprzedniego potoku
                CommandIO.SetInput(pipedInput);

                bool isIntermediate = (i < pipeParts.Length - 1);

                if (isIntermediate)
                {
                    CommandIO.StartRedirection();
                    ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath);
                    pipedInput = CommandIO.EndRedirection();
                }
                else
                {
                    // Ostatni element potoku (lub pojedyncza komenda)
                    ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath);
                }
            }
            CommandIO.SetInput(null);
        }

        private static void ExecuteSingleCommandWithRedirection(string commandLine, ref string currentPath)
        {
            string redirectPath = null;
            bool appendMode = false;

            int appendIndex = -1;
            int singleIndex = -1;
            bool inQuotes = false;

            // Szukamy znaków > lub >> IGNORUJĄC to, co jest w cudzysłowach
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                }
                else if (!inQuotes)
                {
                    if (i < commandLine.Length - 1 && commandLine[i] == '>' && commandLine[i + 1] == '>')
                    {
                        appendIndex = i;
                        break;
                    }
                    else if (c == '>')
                    {
                        singleIndex = i;
                        break;
                    }
                }
            }

            string commandPart = commandLine;

            if (appendIndex != -1)
            {
                appendMode = true;
                commandPart = commandLine.Substring(0, appendIndex);
                redirectPath = commandLine.Substring(appendIndex + 2).Trim();
            }
            else if (singleIndex != -1)
            {
                appendMode = false;
                commandPart = commandLine.Substring(0, singleIndex);
                redirectPath = commandLine.Substring(singleIndex + 1).Trim();
            }

            string[] words = commandPart.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return;

            string cmdName = words[0].ToLower();

            ICommand targetCmd = null;
            foreach (var cmd in _commands)
            {
                if (cmd.Name == cmdName)
                {
                    targetCmd = cmd;
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
                    CommandIO.StartRedirection();
                    targetCmd.Execute(words, ref currentPath);
                    string output = CommandIO.EndRedirection();

                    if (appendMode)
                    {
                        Disk.AppendFile(resolvedPath, output.TrimEnd('\r', '\n'));
                    }
                    else
                    {
                        Disk.CreateFile(resolvedPath, output.TrimEnd('\r', '\n'));
                    }
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
    }
}
