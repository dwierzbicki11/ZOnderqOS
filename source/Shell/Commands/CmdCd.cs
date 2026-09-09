using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdCd : ICommand
    {
        public string Name => "cd";
        public string Description => "Change working directory";
        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 1)
            {
                string newPath = PathResolver.GetAbsolutePath(currentPath, args[1]);
                try
                {
                    if (newPath == "/" || newPath == "") { currentPath = "/"; CommandIO.LastCommandSuccess = true; return; }
                    if (Directory.Exists(newPath)) 
                    {
                        currentPath = newPath;
                        CommandIO.LastCommandSuccess = true;
                    }
                    else 
                    {
                        WriteMessage.WriteError($"Directory does not exist: {newPath}", "CMD");
                        CommandIO.LastCommandSuccess = false;
                    }
                }
                catch (Exception ex) 
                { 
                    WriteMessage.WriteError($"VFS navigation error: {ex.Message}", "CMD"); 
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else 
            {
                WriteMessage.WriteError("Usage: cd <path>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
