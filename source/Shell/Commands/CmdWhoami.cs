namespace ZonderqOS.Commands
{
    public class CmdWhoami : ICommand
    {
        public string Name => "whoami";
        public string Description => "Display active user identity (whoami -a for session details)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                CommandIO.WriteLine("unauthenticated");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length <= 1 || (args[1] != "-a" && args[1] != "--session"))
            {
                CommandIO.WriteLine(SecurityContext.CurrentUser);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            CommandIO.WriteLine("USER: " + SecurityContext.CurrentUser);
            CommandIO.WriteLine("UID: " + SecurityContext.CurrentUid);
            CommandIO.WriteLine("HOME: " + SecurityContext.CurrentHome);
            CommandIO.WriteLine("AUTHENTICATED: YES");
            CommandIO.WriteLine("SESSION: " + SessionManager.ElapsedSeconds + " s");
            CommandIO.WriteLine("SINCE UNLOCK: " + SessionManager.SecondsSinceUnlock + " s");
            CommandIO.WriteLine("UNLOCKS: " + SessionManager.UnlockCount);

            int autoLockMinutes = SystemSettings.AutoLockMinutes;
            CommandIO.WriteLine(autoLockMinutes > 0
                ? "AUTO LOCK: " + autoLockMinutes + " min"
                : "AUTO LOCK: OFF");
            CommandIO.LastCommandSuccess = true;
        }
    }
}