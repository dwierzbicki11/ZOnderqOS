using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdSysReport : ICommand
    {
        public string Name => "sysreport";
        public string Description => "Create a privacy-conscious system diagnostic report";

        public void Execute(string[] args, ref string currentPath)
        {
            string path = args.Length > 1
                ? PathResolver.GetAbsolutePath(currentPath, args[1])
                : PathResolver.GetAbsolutePath(currentPath, "zonderq-diagnostic.txt");

            bool existed = File.Exists(path);
            if (existed && !PermissionManager.CanWrite(path, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError("Permission denied: cannot overwrite " + path, "SEC");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                string report = DiagnosticReportBuilder.Build();
                File.WriteAllText(path, report);

                if (!existed)
                    PermissionManager.SetPermission(path, SecurityContext.CurrentUser, 600);

                SystemLogger.Log(
                    SystemLogLevel.Info,
                    "DIAG",
                    "Diagnostic report exported to " + path + ".");

                WriteMessage.WriteOK("Diagnostic report written: " + path, "DIAG");
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Diagnostic report failed: " + ex.Message, "DIAG");
                SystemLogger.Log(
                    SystemLogLevel.Error,
                    "DIAG",
                    "Diagnostic report export failed: " + ex.Message);
                CommandIO.LastCommandSuccess = false;
            }
        }
    }
}
