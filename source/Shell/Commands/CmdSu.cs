using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdSu : ICommand
    {
        public string Name => "su";
        public string Description => "Switch user session in console mode (su <username> <password>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (CommandIO.IsGraphicalCommand)
            {
                WriteMessage.WriteError(
                    "In-place 'su' is disabled inside the graphical desktop because open windows belong to the current session. Use logout or the Accounts panel to switch users safely.",
                    "AUTH");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length <= 2)
            {
                WriteMessage.WriteError("Usage: su <username> <password>", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string username = args[1];
            string password = args[2];
            int retryAfter;
            if (!AuthenticationGuard.CanAttempt(username, out retryAfter))
            {
                password = null;
                WriteMessage.WriteError("Authentication temporarily blocked for " + retryAfter + " s.", "AUTH");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            bool valid = UserManager.ValidateCredentials(username, password);
            password = null;
            if (valid && UserManager.ActivateSession(username))
            {
                AuthenticationGuard.RecordSuccess(username);
                string home = SecurityContext.CurrentHome;
                if (Directory.Exists(home))
                    currentPath = home;

                WriteMessage.WriteOK("Switched session to user: " + username, "AUTH");
                SecurityLogger.LogEvent("INFO", "Successful console session switch to '" + username + "'.");
                CommandIO.LastCommandSuccess = true;
                return;
            }

            AuthenticationGuard.RecordFailure(username);
            retryAfter = AuthenticationGuard.GetRetryAfterSeconds(username);
            WriteMessage.WriteError(retryAfter > 0
                ? "Authentication failed. Temporary delay: " + retryAfter + " s."
                : "Authentication failed: invalid username or password.", "AUTH");
            SecurityLogger.LogEvent("WARN", "Failed authentication attempt for target '" + username + "'.");
            CommandIO.LastCommandSuccess = false;
        }
    }
}
