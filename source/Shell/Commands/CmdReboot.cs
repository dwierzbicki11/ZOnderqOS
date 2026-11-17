using ZonderqOS.Commands;

class CmdReboot : ICommand
{
    public string Name => "reboot";

    public string Description => "Reboot your system";
    public void Execute(string[] args, ref string currentPath)
    {
        if (!ZonderqOS.SecurityContext.IsAuthenticated || ZonderqOS.SecurityContext.CurrentUid != 0)
        {
            ZonderqOS.WriteMessage.WriteError("Access denied. Root privileges required to reboot.", "SEC");
            ZonderqOS.SecurityLogger.LogEvent("WARN", "Unauthorized reboot attempt by " + (ZonderqOS.SecurityContext.CurrentUser ?? "?"));
            ZonderqOS.CommandIO.LastCommandSuccess = false;
            return;
        }

        ZonderqOS.SystemCore.SystemPower.Reboot("shell-reboot");
    }
}