namespace ZonderqOS.Commands
{
    public class CmdUserAdd : ICommand
    {
        public string Name => "useradd";
        public string Description => "Create a new local user (root only)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Permission denied. Only authenticated root can create users.", "AUTH");
                SecurityLogger.LogEvent("CRIT", "Unauthorized useradd attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: useradd <username> <password>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string username = args[1];
            string password = args[2];

            if (UserManager.CreateUser(username, password))
            {
                string home = UserManager.GetHomeDirectory(username);
                WriteMessage.WriteOK("User '" + username + "' created successfully. Home directory: " + home, "AUTH");
                CommandIO.LastCommandSuccess = true;
            }
            else
            {
                WriteMessage.WriteError("Failed to create user '" + username + "' (invalid name or account already exists).", "AUTH");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}