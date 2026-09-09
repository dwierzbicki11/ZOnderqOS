using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdChmod : ICommand
    {
        public string Name => "chmod";
        public string Description => "Change file mode bits (chmod 600 <file>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length != 3) 
            {
                WriteMessage.WriteError("Usage: chmod <permissions> <file>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }
            
            if (SecurityContext.CurrentUser != "root") 
            {
                WriteMessage.WriteError("Permission denied. Only root can change permissions.", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized chmod attempt by {SecurityContext.CurrentUser}.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (int.TryParse(args[1], out int perms)) 
            {
                string path = PathResolver.GetAbsolutePath(currentPath, args[2]);
                if (File.Exists(path))
                {
                    var currentAcl = PermissionManager.GetPermission(path);
                    PermissionManager.SetPermission(path, currentAcl.Owner, perms);
                    
                    WriteMessage.WriteOK($"Permissions for {path} changed to {perms}", "SEC");
                    SecurityLogger.LogEvent("INFO", $"Permissions of {path} changed to {perms} by root.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("File does not exist.", "FS");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else
            {
                WriteMessage.WriteError("Invalid permission format. Use octal (e.g. 644, 600).", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}