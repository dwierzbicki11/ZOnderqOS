namespace ZonderqOS.Commands
{
    public sealed class CmdRecovery : ICommand
    {
        public string Name => "recovery";
        public string Description => "Enter authenticated recovery / safe mode (root only)";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0 ||
                SecurityContext.CurrentUser != "root")
            {
                WriteMessage.WriteError("Permission denied. Recovery mode requires authenticated root.", "RECOVERY");
                SecurityLogger.LogEvent("WARN", "Unauthorized recovery mode request.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            SystemLogger.Log(SystemLogLevel.Warning, "RECOVERY",
                "Manual recovery mode requested by authenticated root.");
            SecurityLogger.LogEvent("INFO", "Root entered manual recovery mode.");
            CommandIO.LastCommandSuccess = true;

            RecoveryMode.Run("MANUAL MAINTENANCE", null, true);

            WriteMessage.WriteInfo("Returned from recovery mode.", "RECOVERY");
        }
    }
}
