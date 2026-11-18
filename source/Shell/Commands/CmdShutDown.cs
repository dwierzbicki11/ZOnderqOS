using ZonderqOS.Commands;

public class CmdShutDown : ICommand
{
    public string Name => "shutdown";

    public string Description => "Shutdown system";

    public void Execute(string[] args, ref string currentPath)
    {
        if (!ZonderqOS.SecurityContext.IsAuthenticated || ZonderqOS.SecurityContext.CurrentUid != 0)
        {
            ZonderqOS.WriteMessage.WriteError("Access denied. Root privileges required to shut down.", "SEC");
            ZonderqOS.SecurityLogger.LogEvent("WARN", "Unauthorized shutdown attempt by " + (ZonderqOS.SecurityContext.CurrentUser ?? "?"));
            ZonderqOS.CommandIO.LastCommandSuccess = false;
            return;
        }

        ZonderqOS.SystemCore.SystemPower.Shutdown("shell-shutdown");
    }
}