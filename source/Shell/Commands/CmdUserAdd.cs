namespace ZonderqOS.Commands
{
    public class CmdUserAdd : ICommand
    {
        public string Name => "useradd";
        public string Description => "Create a new local user (root only)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0 ||
                SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Permission denied. Only authenticated root can create users.", "AUTH");
                SecurityLogger.LogEvent("CRIT", "Unauthorized useradd attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: useradd <username> <password>", "CMD");
                WriteMessage.WriteError(PasswordPolicy.Summary, "AUTH");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string username = args[1];
            string password = args[2];
            string passwordError;
            if (!PasswordPolicy.Validate(password, out passwordError))
            {
                password = null;
                WriteMessage.WriteError(passwordError, "AUTH");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (UserManager.CreateUser(username, password))
            {
                password = null;
                string home = UserManager.GetHomeDirectory(username);
                WriteMessage.WriteOK("User '" + username + "' created successfully. Home directory: " + home, "AUTH");
                CommandIO.LastCommandSuccess = true;
            }
            else
            {
                password = null;
                WriteMessage.WriteError("Failed to create user '" + username + "' (invalid name, permissions, or account already exists).", "AUTH");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}