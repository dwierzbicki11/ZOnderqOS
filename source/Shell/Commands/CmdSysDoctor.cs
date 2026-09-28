using System.Collections.Generic;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdSysDoctor : ICommand
    {
        private readonly List<SystemCheckResult> results = new List<SystemCheckResult>(16);

        public string Name => "sysdoctor";
        public string Description => "Run read-only kernel health checks";

        public void Execute(string[] args, ref string currentPath)
        {
            bool strict = args.Length > 1 && args[1] == "--strict";

            int failures = SystemDoctor.Run(results);
            int warnings = 0;

            CommandIO.WriteLine("=== ZOnderqOS System Doctor ===");
            for (int i = 0; i < results.Count; i++)
            {
                SystemCheckResult result = results[i];
                string level = result.Severity == SystemCheckSeverity.Fail
                    ? "FAIL"
                    : result.Severity == SystemCheckSeverity.Warning
                        ? "WARN"
                        : "PASS";

                if (result.Severity == SystemCheckSeverity.Warning)
                    warnings++;

                CommandIO.WriteLine("[" + level + "] " + result.Name + ": " + result.Message);
            }

            CommandIO.WriteLine("");
            CommandIO.WriteLine(
                "Summary: checks=" + results.Count +
                " warnings=" + warnings +
                " failures=" + failures);

            bool success = failures == 0 && (!strict || warnings == 0);
            CommandIO.LastCommandSuccess = success;

            if (!success)
            {
                SystemLogger.Log(
                    failures > 0 ? SystemLogLevel.Error : SystemLogLevel.Warning,
                    "DOCTOR",
                    "System Doctor reported warnings=" + warnings + " failures=" + failures + ".");
            }
        }
    }
}
