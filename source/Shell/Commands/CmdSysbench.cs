using System;
using System.Threading;
using ZonderqOS.SystemCore.Diagnostics;

namespace ZonderqOS.Commands
{
    public sealed class CmdSysbench : ICommand
    {
        public string Name => "sysbench";
        public string Description => "Safe ZONDERQDATA diagnostics with portable reports";

        public void Execute(string[] args, ref string currentPath)
        {
            bool soak = false;
            bool rebootRequested = false;
            bool statusOnly = false;
            if (args != null)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    string arg = args[i] ?? string.Empty;
                    if (arg == "--help" || arg == "-h")
                    {
                        CommandIO.WriteLine("Usage: sysbench [--quick|--full|--soak|--status|--reboot-resume]");
                        CommandIO.WriteLine("  --quick: short checks, USB report");
                        CommandIO.WriteLine("  --full/--soak: real 3600+ second managed-heap sample");
                        CommandIO.WriteLine("  --reboot-resume: unsupported, reported NOT_TESTED; never reboots");
                        CommandIO.LastCommandSuccess = true;
                        return;
                    }

                    if (arg == "--full" || arg == "--soak") soak = true;
                    else if (arg == "--quick") { }
                    else if (arg == "--reboot-resume") rebootRequested = true;
                    else if (arg == "--status") statusOnly = true;
                    else
                    {
                        CommandIO.WriteLine("[sysbench] Unknown option: " + arg);
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                }
            }

            SystemDiagnosticRunner runner = SystemDiagnosticRunner.Shared;
            if (statusOnly)
            {
                CommandIO.WriteLine("[sysbench] running=" + runner.IsRunning +
                                    " progress=" + runner.ProgressPercent +
                                    "% scenario=" + runner.CurrentScenario);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            if (runner.IsRunning)
            {
                CommandIO.WriteLine("[sysbench] Another diagnostic session is already running");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("[sysbench] Starting safe diagnostics...");
            if (soak)
                CommandIO.WriteLine("[sysbench] Real 60-minute managed-heap test; do not shut down.");
            if (rebootRequested)
                CommandIO.WriteLine("[sysbench] Reboot/resume is NOT implemented and will not run.");

            runner.Start(soak, rebootRequested);
            int lastProgress = -1;
            string lastScenario = string.Empty;
            while (runner.Step())
            {
                if (runner.ProgressPercent != lastProgress ||
                    runner.CurrentScenario != lastScenario)
                {
                    lastProgress = runner.ProgressPercent;
                    lastScenario = runner.CurrentScenario;
                    CommandIO.WriteLine("[sysbench] " + runner.ProgressPercent +
                                        "% " + runner.CurrentScenario);
                }

                // Step() samples without blocking so GUI stays responsive;
                // only the terminal command sleeps during its hour-long soak.
                if (soak && runner.CurrentScenario.StartsWith(
                        "Managed heap soak:", StringComparison.Ordinal))
                    Thread.Sleep(750);
            }

            CommandIO.WriteLine(runner.ExportMarkdown());
            CommandIO.WriteLine("[sysbench] Report file: " + runner.LastReportPath);

            bool passed = runner.LastReportPath == "/sysbench_report.md";
            for (int i = 0; i < runner.Results.Count; i++)
            {
                if (runner.Results[i].Status == DiagnosticStatus.Fail)
                    passed = false;
            }
            CommandIO.LastCommandSuccess = passed;
        }
    }
}
