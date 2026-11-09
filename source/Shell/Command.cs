using System;
using System.Collections.Generic;
using System.Text;
using ZonderqOS.Commands;

namespace ZonderqOS
{
    public static class Command
    {
        private const int MaxCommandLineLength = 16 * 1024;
        private const int MaxCommandSegments = 128;
        private const int MaxCommandTokens = 256;
        private const int MaxTokenLength = 4 * 1024;
        private static readonly List<ICommand> _commands = new List<ICommand>();
        private static readonly Dictionary<string, ICommand> _commandLookup = new Dictionary<string, ICommand>(StringComparer.OrdinalIgnoreCase);

        private static void Register(ICommand cmd)
        {
            _commands.Add(cmd);
            if (!string.IsNullOrEmpty(cmd.Name))
                _commandLookup[cmd.Name] = cmd;
        }

        public static void Initialize()
        {
            _commands.Clear();
            _commandLookup.Clear();
            Register(new CmdPwd()); Register(new CmdCd()); Register(new CmdLs());
            Register(new CmdLspci()); Register(new CmdNicDiag()); Register(new CmdDisplayInfo()); Register(new CmdInputScan()); Register(new CmdMkdir()); Register(new CmdRmdir());
            Register(new CmdCat()); Register(new CmdWrite()); Register(new CmdTouch());
            Register(new CmdAppend()); Register(new CmdCp()); Register(new CmdMv());
            Register(new CmdRm()); Register(new CmdSpace()); Register(new CmdFormat());
            Register(new CmdLsblk()); Register(new CmdMount()); Register(new CmdUmount());
            Register(new CmdStat()); Register(new CmdHexdump()); Register(new CmdViewLog());
            Register(new CmdDmesg()); Register(new CmdClear()); Register(new CmdSysInfo());
            Register(new CmdLscpu()); Register(new CmdNproc()); Register(new CmdRecovery());
            Register(new CmdGrep()); Register(new CmdHead()); Register(new CmdEcho());
            Register(new CmdWhoami()); Register(new CmdTty()); Register(new CmdGui());
            Register(new CmdBootMode()); Register(new CmdDf()); Register(new CmdHostname());
            Register(new CmdService()); Register(new CmdSystemctl()); Register(new CmdHelp(_commands));
            Register(new CmdMkpart()); Register(new CmdShutDown()); Register(new CmdReboot());
            Register(new CmdUserAdd()); Register(new CmdSu()); Register(new CmdLogout());
            Register(new CmdSh()); Register(new CmdAudit()); Register(new CmdNano());
            Register(new CmdIntegrity()); Register(new CmdFirewall()); Register(new CmdUpdate());
            Register(new CmdChmod()); Register(new CmdFree()); Register(new CmdUptime());
            Register(new CmdUname()); Register(new CmdPs()); Register(new CmdPidof());
            Register(new CmdPgrep()); Register(new CmdKillall()); Register(new CmdHtop());
            Register(new CmdSmtCheck()); Register(new CmdSysmond()); Register(new CmdKill());
            Register(new CmdEnv()); Register(new CmdExport());
            Register(new CmdLocale());
            Register(new CmdNeofetch()); Register(new CmdMatrix());
        }

        internal static string[] GetCommandNames()
        {
            string[] names = new string[_commands.Count];
            for (int i = 0; i < _commands.Count; i++) names[i] = _commands[i].Name;
            return names;
        }

        public static void Run(string fullInput, ref string currentPath)
        {
            if (string.IsNullOrWhiteSpace(fullInput)) return;
            if (!ValidateCommandLength(fullInput, "Command line")) return;

            fullInput = EnvironmentExpander.Expand(fullInput, currentPath);
            if (!ValidateCommandLength(fullInput, "Expanded command line")) return;

            List<string> semiCommands;
            try { semiCommands = SplitOutsideQuotes(fullInput, ";"); }
            catch (InvalidOperationException ex) { ReportError(ex.Message); CommandIO.LastCommandSuccess = false; return; }

            for (int i = 0; i < semiCommands.Count; i++)
            {
                string block = semiCommands[i].Trim();
                if (string.IsNullOrEmpty(block)) continue;
                List<string> andCommands;
                try { andCommands = SplitOutsideQuotes(block, "&&"); }
                catch (InvalidOperationException ex) { ReportError(ex.Message); CommandIO.LastCommandSuccess = false; return; }
                for (int j = 0; j < andCommands.Count; j++)
                {
                    string pipelineCmd = andCommands[j].Trim();
                    if (string.IsNullOrEmpty(pipelineCmd)) continue;
                    ExecutePipeline(pipelineCmd, ref currentPath);
                    if (!CommandIO.LastCommandSuccess) break;
                }
            }
        }

        private static bool ValidateCommandLength(string value, string label)
        {
            if (value.Length <= MaxCommandLineLength) return true;
            ReportError(label + " exceeds the " + MaxCommandLineLength + " character limit.");
            CommandIO.LastCommandSuccess = false;
            return false;
        }

        private static void ExecutePipeline(string pipelineStr, ref string currentPath)
        {
            List<string> pipeParts;
            try { pipeParts = SplitOutsideQuotes(pipelineStr, "|"); }
            catch (InvalidOperationException ex) { ReportError(ex.Message); CommandIO.LastCommandSuccess = false; return; }
            string pipedInput = null;
            try
            {
                for (int i = 0; i < pipeParts.Count; i++)
                {
                    string singleCmdStr = pipeParts[i].Trim();
                    if (string.IsNullOrEmpty(singleCmdStr)) continue;
                    CommandIO.SetInput(pipedInput);
                    bool isIntermediate = i < pipeParts.Count - 1;
                    if (isIntermediate)
                    {
                        string captured;
                        CommandIO.StartRedirection();
                        try { ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath); }
                        finally { captured = CommandIO.EndRedirection(); }
                        pipedInput = captured;
                    }
                    else ExecuteSingleCommandWithRedirection(singleCmdStr, ref currentPath);
                }
            }
            finally { CommandIO.SetInput(null); }
        }

        private static void ExecuteSingleCommandWithRedirection(string commandLine, ref string currentPath)
        {
            string redirectPath = null;
            bool appendMode;
            int redirectIndex = FindRedirection(commandLine, out appendMode);
            string commandPart = commandLine;
            if (redirectIndex >= 0)
            {
                commandPart = commandLine.Substring(0, redirectIndex);
                redirectPath = commandLine.Substring(redirectIndex + (appendMode ? 2 : 1)).Trim();
                if (string.IsNullOrEmpty(redirectPath)) { ReportError("Missing redirection target path."); CommandIO.LastCommandSuccess = false; return; }
                redirectPath = UnquotePath(redirectPath);
                if (string.IsNullOrEmpty(redirectPath)) { ReportError("Invalid redirection target path."); CommandIO.LastCommandSuccess = false; return; }
            }

            string[] words;
            try { words = Tokenize(commandPart); }
            catch (InvalidOperationException ex) { ReportError(ex.Message); CommandIO.LastCommandSuccess = false; return; }
            if (words.Length == 0) { CommandIO.LastCommandSuccess = false; return; }

            string cmdName = words[0].ToLower();
            if (!_commandLookup.TryGetValue(cmdName, out ICommand targetCmd) || targetCmd == null) { ReportError("Unknown command: " + cmdName); CommandIO.LastCommandSuccess = false; return; }

            try
            {
                if (!string.IsNullOrEmpty(redirectPath))
                {
                    string resolvedPath = PathResolver.GetAbsolutePath(currentPath, redirectPath);
                    string output;
                    CommandIO.StartRedirection();
                    try { targetCmd.Execute(words, ref currentPath); }
                    finally { output = CommandIO.EndRedirection(); }
                    if (appendMode) Disk.AppendFile(resolvedPath, output.TrimEnd('\r', '\n'));
                    else Disk.CreateFile(resolvedPath, output.TrimEnd('\r', '\n'));
                }
                else targetCmd.Execute(words, ref currentPath);
            }
            catch (Exception ex) { ReportError("Command '" + cmdName + "' execution failed: " + ex.Message); CommandIO.LastCommandSuccess = false; }
        }

        private static void ReportError(string message) { WriteMessage.WriteError(message, "CMD"); }

        private static int FindRedirection(string value, out bool appendMode)
        {
            appendMode = false; bool inDoubleQuotes = false; bool inSingleQuotes = false; bool escaped = false;
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (escaped) { escaped = false; continue; }
                if (c == '\\') { escaped = true; continue; }
                if (c == '"' && !inSingleQuotes) { inDoubleQuotes = !inDoubleQuotes; continue; }
                if (c == '\'' && !inDoubleQuotes) { inSingleQuotes = !inSingleQuotes; continue; }
                if (!inDoubleQuotes && !inSingleQuotes && c == '>') { appendMode = i + 1 < value.Length && value[i + 1] == '>'; return i; }
            }
            return -1;
        }

        private static List<string> SplitOutsideQuotes(string input, string separator)
        {
            var result = new List<string>();
            if (string.IsNullOrEmpty(input)) return result;
            var current = new StringBuilder(); bool inDoubleQuotes = false; bool inSingleQuotes = false; bool escaped = false;
            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];
                if (escaped) { current.Append(c); escaped = false; continue; }
                if (c == '\\') { current.Append(c); escaped = true; continue; }
                if (c == '"' && !inSingleQuotes) { inDoubleQuotes = !inDoubleQuotes; current.Append(c); continue; }
                if (c == '\'' && !inDoubleQuotes) { inSingleQuotes = !inSingleQuotes; current.Append(c); continue; }
                if (!inDoubleQuotes && !inSingleQuotes && MatchesAt(input, separator, i))
                {
                    if (result.Count >= MaxCommandSegments - 1) throw new InvalidOperationException("Too many command segments; limit is " + MaxCommandSegments + ".");
                    result.Add(current.ToString()); current.Clear(); i += separator.Length - 1; continue;
                }
                current.Append(c);
            }
            result.Add(current.ToString());
            return result;
        }

        private static bool MatchesAt(string value, string separator, int index)
        {
            if (index + separator.Length > value.Length) return false;
            for (int i = 0; i < separator.Length; i++) if (value[index + i] != separator[i]) return false;
            return true;
        }

        private static string[] Tokenize(string commandPart)
        {
            var tokens = new List<string>(); var current = new StringBuilder();
            bool inDoubleQuotes = false; bool inSingleQuotes = false; bool escaped = false; bool tokenStarted = false;
            for (int i = 0; i < commandPart.Length; i++)
            {
                char c = commandPart[i];
                if (escaped) { AppendTokenChar(current, c); tokenStarted = true; escaped = false; continue; }
                if (c == '\\') { escaped = true; tokenStarted = true; continue; }
                if (c == '"' && !inSingleQuotes) { inDoubleQuotes = !inDoubleQuotes; tokenStarted = true; continue; }
                if (c == '\'' && !inDoubleQuotes) { inSingleQuotes = !inSingleQuotes; tokenStarted = true; continue; }
                if (!inDoubleQuotes && !inSingleQuotes && char.IsWhiteSpace(c))
                {
                    if (tokenStarted) { AddToken(tokens, current); tokenStarted = false; }
                    continue;
                }
                AppendTokenChar(current, c); tokenStarted = true;
            }
            if (escaped) AppendTokenChar(current, '\\');
            if (tokenStarted) AddToken(tokens, current);
            return tokens.ToArray();
        }

        private static void AppendTokenChar(StringBuilder current, char value)
        {
            if (current.Length >= MaxTokenLength) throw new InvalidOperationException("Command token exceeds the " + MaxTokenLength + " character limit.");
            current.Append(value);
        }

        private static void AddToken(List<string> tokens, StringBuilder current)
        {
            if (tokens.Count >= MaxCommandTokens) throw new InvalidOperationException("Too many command tokens; limit is " + MaxCommandTokens + ".");
            tokens.Add(current.ToString()); current.Clear();
        }

        private static string UnquotePath(string value)
        {
            string path = value.Trim();
            if (path.Length >= 2)
            {
                char first = path[0]; char last = path[path.Length - 1];
                if ((first == '"' && last == '"') || (first == '\'' && last == '\'')) path = path.Substring(1, path.Length - 2);
            }
            return path.Replace("\\\"", "\"").Replace("\\'", "'");
        }
    }
}
