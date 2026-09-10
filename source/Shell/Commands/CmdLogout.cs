namespace ZonderqOS.Commands
{
    public class CmdLogout : ICommand
    {
        public string Name => "logout";
        public string Description => "End the current user session and return to the login prompt";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                CommandIO.LastCommandSuccess = false;
                return;
            }

            UserManager.EndSession();
            currentPath = "/";
            CommandIO.WriteLine("Session ended.");
            CommandIO.LastCommandSuccess = true;
        }
    }
}
