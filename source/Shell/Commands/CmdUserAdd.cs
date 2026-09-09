namespace ZonderqOS.Commands
{
    public class CmdUserAdd : ICommand
    {
        public string Name => "useradd";
        public string Description => "Create a new user (useradd <username> <password>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Permission denied. Only root can create users.", "AUTH");
                SecurityLogger.LogEvent("CRIT", $"Unauthorized attempt to create user '{args[1]}'.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length > 2)
            {
                string username = args[1];
                string password = args[2];

                if (UserManager.CreateUser(username, password))
                {
                    WriteMessage.WriteOK($"User '{username}' created successfully. Home directory: /home/{username}", "AUTH");
                    SecurityLogger.LogEvent("INFO", $"Created new user account '{username}'.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError($"Failed to create user '{username}' (User may already exist).", "AUTH");
                    SecurityLogger.LogEvent("WARN", $"Failed to create user '{username}' (conflict or error).");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else
            {
                WriteMessage.WriteError("Usage: useradd <username> <password>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}