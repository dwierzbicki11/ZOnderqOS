using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdSh : ICommand
    {
        public string Name => "sh";
        public string Description => "Execute a shell script file (sh <script_path>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                string scriptPath = PathResolver.GetAbsolutePath(currentPath, args[1]);
                try
                {
                    if (File.Exists(scriptPath))
                    {
                        string content = File.ReadAllText(scriptPath);
                        string[] lines = content.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);

                        CommandIO.WriteLine($"--- Executing script: {scriptPath} ---");
                        foreach (string rawLine in lines)
                        {
                            string line = rawLine.Trim();
                            // Ignoruj puste linie oraz komentarze uniksowe
                            if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

                            // Wykonaj linię skryptu przez główny parser powłoki
                            Command.Run(line, ref currentPath);
                        }
                        CommandIO.LastCommandSuccess = true;
                    }
                    else
                    {
                        WriteMessage.WriteError($"Script file does not exist: {scriptPath}", "CMD");
                        CommandIO.LastCommandSuccess = false;
                    }
                }
                catch (Exception ex)
                {
                    WriteMessage.WriteError($"Script execution error: {ex.Message}", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else
            {
                WriteMessage.WriteError("Usage: sh <script_path>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}