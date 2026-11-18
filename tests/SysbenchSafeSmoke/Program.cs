using System;
using System.Linq;
using System.Text.Json;
using ZonderqOS.SystemCore.Diagnostics;

namespace SysbenchSafeSmoke
{
    internal static class Program
    {
        private static void Require(bool condition, string message)
        {
            if (!condition)
                throw new Exception("[SYSBENCH HOST SMOKE FAIL] " + message);
        }

        public static int Main()
        {
            // No actual OS partition is ever mounted in this host smoke.
            Require(!ZonderqOS.Disk.IsApprovedPersistentRootMounted,
                "Host must never expose a writable guest root");

            var runner = SystemDiagnosticRunner.Shared;
            runner.Start(false, false);
            int iterations = 0;
            while (runner.Step())
            {
                iterations++;
                Require(iterations < 20, "Quick mode must finish without a timed soak");
            }
            Require(!runner.IsRunning, "Quick runner stuck");
            Require(runner.ProgressPercent == 100, "Quick mode did not complete");
            Require(runner.Results.Any(r =>
                r.Name.Contains("100 GUI") && r.Status == DiagnosticStatus.NotTested),
                "Fake GUI cycles were accepted as PASS");
            Require(runner.Results.Any(r =>
                r.Name.Contains("Lock/Unlock") && r.Status == DiagnosticStatus.NotTested),
                "Synthetic password checks counted as real lock/unlock");
            Require(runner.Results.Any(r =>
                r.Name.Contains("FAT32") && r.Status == DiagnosticStatus.Skipped),
                "Unapproved host volume was accessed");
            Require(runner.Results.Any(r =>
                r.Name.Contains("60") && r.Status == DiagnosticStatus.Skipped),
                "Quick mode counted unperformed 60-min sample as PASS");

            var fat = Fat32DiagnosticHelper.RunIsolatedFat32Test("/");
            Require(!fat.ok, "Unapproved root must fail closed");

            using (JsonDocument document = JsonDocument.Parse(runner.ExportJson()))
                Require(document.RootElement.GetArrayLength() == runner.Results.Count,
                    "JSON report invalid");

            string path = "/";
            new ZonderqOS.Commands.CmdSysbench().Execute(
                new[] {"sysbench", "--quick"}, ref path);
            Require(!ZonderqOS.CommandIO.LastCommandSuccess,
                "No report saved; CLI should not report complete success");
            Require(runner.LastReportPath.StartsWith("NOT_SAVED", StringComparison.Ordinal),
                "Expected explicit output fallback");
            Console.WriteLine("[SYSBENCH HOST SMOKE] PASS - no host disks or user accounts touched");
            return 0;
        }
    }
}

namespace ZonderqOS
{
    // No CreateUser API in the stub: a regression that tries to create
    // a synthetic account will fail compilation, not just a runtime test.
    public static class UserManager
    {
        public static bool UserExists(string user) => user == "root";
    }

    public static class SecurityContext
    {
        public static bool IsAuthenticated => true;
        public static int CurrentUid => 0;
        public static string CurrentUser => "root";
    }

    public static class Disk
    {
        public static bool IsApprovedPersistentRootMounted => false;
    }

    public interface ICommand
    {
        string Name { get; }
        string Description { get; }
        void Execute(string[] args, ref string currentPath);
    }

    public static class CommandIO
    {
        public static bool LastCommandSuccess { get; set; }
        public static void WriteLine(string text) { }
    }
}
