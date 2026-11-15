using System;

namespace ZonderqOS.Commands
{
    public class CmdRmdir : ICommand
    {
        public string Name => "rmdir";
        public string Description => "Remove a directory";
        public void Execute(string[] args, ref string currentPath)
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                if (args.Length > 1)
                {
                    Disk.DeleteDir(PathResolver.GetAbsolutePath(currentPath, args[1]));
                }
                else
                {
                    WriteMessage.WriteError("Usage: rmdir <path>", "CMD");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            catch (Exception ex)
            {
                CommandIO.LastCommandSuccess = false;
                WriteMessage.WriteError($"Failed to remove directory: {ex.Message}", "FS");
            }
        }
    }
}
