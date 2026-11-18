using System;
using ZonderqOS.SystemCore.Diagnostics;

namespace ZonderqOS.Commands
{
    public sealed class CmdSysbench : ICommand
    {
        public string Name => "sysbench";
        public string Description => "Run ZOnderqOS System Benchmark & Diagnostics";

        public void Execute(string[] args, ref string currentPath)
        {
            bool soakRequested = false;
            bool rebootRequested = false;
            bool statusOnly = false;

            // args[0] is the command name, following CmdUpdate convention.
            if (args != null)
            {
                for (int i = 1; i < args.Length; i++)
                {
                    string option = args[i] ?? string.Empty;
                    if (option == "--help" || option == "-h")
                    {
                        CommandIO.WriteLine("Usage: sysbench [--quick|--full|--soak|--reboot-resume|--status]");
                        CommandIO.WriteLine("--quick: safe checks and reports on ZONDERQDATA");
                        CommandIO.WriteLine("--full/--soak: 60-minute soak currently NOT_TESTED (no fake PASS)");
                        CommandIO.WriteLine("--reboot-resume: NOT_TESTED; does not restart the device");
                        CommandIO.LastCommandSuccess = true;
                        return;
                    }
                    if (option == "--full" || option == "--soak")
                        soakRequested = true;
                    else if (option == "--reboot-resume")
                        rebootRequested = true;
                    else if (option == "--status")
                        statusOnly = true;
                    else if (option != "--quick")
                    {
                        CommandIO.WriteLine("[sysbench] Unknown option: " + option);
                        CommandIO.LastCommandSuccess = false;
                        return;
                    }
                }
            }

            SystemDiagnosticRunner runner = SystemDiagnosticRunner.Shared;
            if (statusOnly)
            {
                CommandIO.WriteLine("[sysbench] Running=" + runner.IsRunning +
                                    " Progress=" + runner.ProgressPercent +
                                    "% Scenario=" + runner.CurrentScenario);
                CommandIO.LastCommandSuccess = true;
                return;
            }
            if (runner.IsRunning)
            {
                CommandIO.WriteLine("[sysbench] Diagnostic already running; use --status");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            CommandIO.WriteLine("[sysbench] Running safe diagnostics...");
            if (soakRequested)
                CommandIO.WriteLine("[sysbench] Note: full 60-minute soak is not implemented here; it will be NOT_TESTED.");
            if (rebootRequested)
                CommandIO.WriteLine("[sysbench] Note: no automatic reboot will be performed.");

            runner.Start(soakRequested, rebootRequested);
            while (runner.Step())
            {
                CommandIO.WriteLine("[sysbench] " + runner.ProgressPercent +
                                    "% " + runner.CurrentScenario);
            }

            CommandIO.WriteLine(runner.ExportMarkdown());
            CommandIO.WriteLine("[sysbench] Report on USB: " + runner.LastReportPath);

            bool success = runner.LastReportPath == "/sysbench_report.md";
            for (int i = 0; i < runner.Results.Count; i++)
            {
                if (runner.Results[i].Status == DiagnosticStatus.Fail)
                    success = false;
            }
            CommandIO.LastCommandSuccess = success;
            if (!success)
                CommandIO.WriteLine("[sysbench] Report missing or a tested check FAILED; inspect details.");
        }
    }
}
