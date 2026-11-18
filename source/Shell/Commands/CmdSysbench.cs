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
            bool soak = false;
            bool reboot = false;
            bool statusOnly = false;

            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    string a = args[i];
                    if (a == "--help" || a == "-h")
                    {
                        Console.WriteLine("Uzycie: sysbench [--quick|--full|--soak|--reboot-resume|--status]");
                        return;
                    }
                    if (a == "--full" || a == "--soak") soak = true;
                    else if (a == "--reboot-resume") reboot = true;
                    else if (a == "--status") statusOnly = true;
                }
            }

            var runner = SystemDiagnosticRunner.Shared;
            if (statusOnly)
            {
                Console.WriteLine($"[sysbench] Running={runner.IsRunning} Progress={runner.ProgressPercent}% Scenario={runner.CurrentScenario}");
                return;
            }

            Console.WriteLine("[sysbench] Uruchamianie ZOnderqOS System Benchmark & Diagnostics...");
            runner.Start(soak, reboot);
            while (runner.Step())
            {
                Console.WriteLine($"[{runner.ProgressPercent}%] {runner.CurrentScenario}");
            }

            Console.WriteLine(runner.ExportMarkdown());
            Console.WriteLine($"[sysbench] Raport: {runner.LastReportPath}");
        }
    }
}
