using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public class CmdSu : ICommand
    {
        public string Name => "su";
        public string Description => "Switch user session (su <username> <password>)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length > 2)
            {
                string username = args[1];
                string password = args[2];

                if (UserManager.ValidateCredentials(username, password))
                {
                    SecurityContext.CurrentUser = username;
                    string home = UserManager.GetHomeDirectory(username);
                    SecurityContext.CurrentHome = home;
                    
                    // Aktualizacja zmiennych środowiskowych sesji
                    EnvironmentManager.Set("USER", username);
                    EnvironmentManager.Set("HOME", home);

                    if (Directory.Exists(home))
                    {
                        currentPath = home;
                    }

                    WriteMessage.WriteOK($"Switched session to user: {username}", "AUTH");
                    SecurityLogger.LogEvent("INFO", $"Successful session switch to '{username}'.");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    WriteMessage.WriteError("Authentication failed: invalid username or password.", "AUTH");
                    SecurityLogger.LogEvent("WARN", $"Failed authentication attempt for target '{username}'.");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            else
            {
                WriteMessage.WriteError("Usage: su <username> <password>", "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}