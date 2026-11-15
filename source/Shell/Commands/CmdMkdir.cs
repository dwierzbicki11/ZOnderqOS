using System;

namespace ZonderqOS.Commands
{
    public class CmdMkdir : ICommand
    {
        public string Name => "mkdir";
        public string Description => "Create a new directory";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                if (args.Length > 1)
                {
                    Disk.CreateDir(PathResolver.GetAbsolutePath(currentPath, args[1]));
                }
                else
                {
                    WriteMessage.WriteError("Usage: mkdir <path>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            catch (Exception ex)
            {
                CommandIO.LastCommandSuccess = false;
                WriteMessage.WriteError($"Failed to create directory: {ex.Message}", "FS");
            }
        }
    }
}
