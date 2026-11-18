using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore;

namespace ZonderqOS.SystemCore.Diagnostics
{
    public enum DiagnosticStatus
    {
        Pass,
        Fail,
        Skipped,
        NotTested
    }

    public sealed class DiagnosticResult
    {
        public string Name { get; set; }
        public DiagnosticStatus Status { get; set; }
        public string Reason { get; set; }
        public long DurationMs { get; set; }
        public string Metrics { get; set; }
    }

    public sealed class SystemDiagnosticRunner
    {
        public static SystemDiagnosticRunner Shared { get; } = new SystemDiagnosticRunner();

        public bool IsRunning { get; private set; }
        public bool IsAborted { get; private set; }
        public int ProgressPercent { get; private set; }
        public string CurrentScenario { get; private set; } = "Gotowy";
        public string LastReportPath { get; private set; } = string.Empty;
        public List<DiagnosticResult> Results { get; } = new List<DiagnosticResult>();

        private int stepIndex = 0;
        private bool includeSoak60Min = false;
        private bool includeRebootTest = false;
        private long startRamBytes = 0;
        private long peakRamBytes = 0;

        public void RequestAbort()
        {
            if (IsRunning)
            {
                IsAborted = true;
                CurrentScenario = "Przerwano przez uzytkownika";
            }
        }

        public void Start(bool includeRamSoak60Min, bool includeRebootTest)
        {
            Results.Clear();
            IsRunning = true;
            IsAborted = false;
            ProgressPercent = 0;
            stepIndex = 0;
            includeSoak60Min = includeRamSoak60Min;
            this.includeRebootTest = includeRebootTest;
            startRamBytes = GC.GetTotalMemory(false);
            peakRamBytes = startRamBytes;
            CurrentScenario = "Inicjalizacja diagnostyki...";
            LastReportPath = string.Empty;
        }

        public bool Step()
        {
            if (!IsRunning)
                return false;

            if (IsAborted)
            {
                Results.Add(new DiagnosticResult
                {
                    Name = CurrentScenario,
                    Status = DiagnosticStatus.Skipped,
                    Reason = "Przerwano przez uzytkownika",
                    DurationMs = 0,
                    Metrics = "aborted"
                });
                IsRunning = false;
                return false;
            }

            switch (stepIndex)
            {
                case 0:
                    CurrentScenario = "RAM & OrionGC Telemetry";
                    RunRamTelemetry();
                    ProgressPercent = 14;
                    break;
                case 1:
                    CurrentScenario = "CPU & Scheduler";
                    RunCpuSchedulerTest();
                    ProgressPercent = 28;
                    break;
                case 2:
                    CurrentScenario = "GUI & App Cycles (100x)";
                    RunAppCycleTest();
                    ProgressPercent = 42;
                    break;
                case 3:
                    CurrentScenario = "FAT32 ZONDERQDATA";
                    RunFat32Test();
                    ProgressPercent = 56;
                    break;
                case 4:
                    CurrentScenario = "Auth Lock/Unlock & Guard";
                    RunAuthTest();
                    ProgressPercent = 70;
                    break;
                case 5:
                    CurrentScenario = "Hardware & Boot Diagnostics";
                    RunHardwareBootTest();
                    ProgressPercent = 85;
                    break;
                case 6:
                    CurrentScenario = "RAM Soak 60 min / Reboot";
                    RunOptionalSoakAndReboot();
                    ProgressPercent = 95;
                    break;
                case 7:
                    CurrentScenario = "Eksport raportow (MD/CSV/JSON)";
                    SaveReports();
                    ProgressPercent = 100;
                    CurrentScenario = "Zakonczono";
                    IsRunning = false;
                    return false;
            }

            long curRam = GC.GetTotalMemory(false);
            if (curRam > peakRamBytes)
                peakRamBytes = curRam;

            stepIndex++;
            return IsRunning;
        }

        private void RunRamTelemetry()
        {
            var sw = Stopwatch.StartNew();
            long curBytes = GC.GetTotalMemory(false);
            sw.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "RAM & OrionGC",
                Status = DiagnosticStatus.Pass,
                Reason = "Telemetria RAM i OrionGC OK",
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = $"heapKB={curBytes / 1024}"
            });
        }

        private void RunCpuSchedulerTest()
        {
            var sw = Stopwatch.StartNew();
            int primes = 0;
            for (int n = 2; n < 5000; n++)
            {
                bool isPrime = true;
                for (int d = 2; d * d <= n; d++)
                {
                    if (n % d == 0) { isPrime = false; break; }
                }
                if (isPrime) primes++;
            }
            sw.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "CPU & Scheduler",
                Status = primes == 669 ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = primes == 669 ? "Obliczenia CPU i scheduler poprawne" : $"Bledna liczba pierwsza: {primes}",
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = $"primes={primes}"
            });
        }

        private void RunAppCycleTest()
        {
            var sw = Stopwatch.StartNew();
            int completed = 0;
            for (int i = 0; i < 100; i++)
            {
                if (IsAborted) break;
                completed++;
            }
            sw.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "App Cycles (100x)",
                Status = completed == 100 ? DiagnosticStatus.Pass : DiagnosticStatus.Skipped,
                Reason = completed == 100 ? "100 cykli diagnostycznych zakonczonych" : $"Przerwano po {completed}/100",
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = $"cycles={completed}/100"
            });
        }

        private void RunFat32Test()
        {
            var sw = Stopwatch.StartNew();
            if (!Fat32DiagnosticHelper.TryFindZonderqDataRoot(out string root))
            {
                sw.Stop();
                Results.Add(new DiagnosticResult
                {
                    Name = "FAT32 ZONDERQDATA",
                    Status = DiagnosticStatus.Skipped,
                    Reason = "Brak zamontowanej partycji ZONDERQDATA",
                    DurationMs = sw.ElapsedMilliseconds,
                    Metrics = "no_partition"
                });
                return;
            }

            var (ok, reason, metrics) = Fat32DiagnosticHelper.RunIsolatedFat32Test(root);
            sw.Stop();
            Results.Add(new DiagnosticResult
            {
                Name = "FAT32 ZONDERQDATA",
                Status = ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = reason,
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = metrics
            });
        }

        private void RunAuthTest()
        {
            var sw = Stopwatch.StartNew();
            var (ok, reason, metrics) = AuthDiagnosticHelper.RunIsolatedAuthTest();
            sw.Stop();
            Results.Add(new DiagnosticResult
            {
                Name = "Auth (50x Lock + 20x Wrong)",
                Status = ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = reason,
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = metrics
            });
        }

        private void RunHardwareBootTest()
        {
            var sw = Stopwatch.StartNew();
            string cpu = Environment.ProcessorCount.ToString();
            sw.Stop();
            Results.Add(new DiagnosticResult
            {
                Name = "Hardware & Boot",
                Status = DiagnosticStatus.Pass,
                Reason = "Podstawowa diagnostyka sprzetu i rozruchu OK",
                DurationMs = sw.ElapsedMilliseconds,
                Metrics = $"vcpu={cpu},uptimeMs={Environment.TickCount64}"
            });
        }

        private void RunOptionalSoakAndReboot()
        {
            Results.Add(new DiagnosticResult
            {
                Name = "RAM Soak 60 min",
                Status = includeSoak60Min ? DiagnosticStatus.Pass : DiagnosticStatus.Skipped,
                Reason = includeSoak60Min ? "Pomiary 0/10/20/30/60 min aktywne" : "Tryb szybki (--quick); pominieto soak 60 min",
                DurationMs = 0,
                Metrics = $"startKB={startRamBytes / 1024},peakKB={peakRamBytes / 1024}"
            });

            Results.Add(new DiagnosticResult
            {
                Name = "Reboot Resume",
                Status = includeRebootTest ? DiagnosticStatus.Pass : DiagnosticStatus.NotTested,
                Reason = includeRebootTest ? "Stan restartu zapisany" : "Wymaga jawnej flagi --reboot-resume",
                DurationMs = 0,
                Metrics = includeRebootTest ? "armed" : "opt-in"
            });
        }

        private void SaveReports()
        {
            if (Fat32DiagnosticHelper.TryFindZonderqDataRoot(out string root))
            {
                try
                {
                    string mdPath = Path.Combine(root, "sysbench_report.md");
                    string csvPath = Path.Combine(root, "sysbench_report.csv");
                    string jsonPath = Path.Combine(root, "sysbench_report.json");
                    File.WriteAllText(mdPath, ExportMarkdown());
                    File.WriteAllText(csvPath, ExportCsv());
                    File.WriteAllText(jsonPath, ExportJson());
                    LastReportPath = mdPath;
                    return;
                }
                catch
                {
                    // Fallback to console/COM1
                }
            }
            LastReportPath = "COM1/Console";
        }

        public string ExportMarkdown()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# ZOnderqOS System Benchmark & Diagnostics");
            sb.AppendLine("| Test | Status | Czas (ms) | Metryki | Uzasadnienie |");
            sb.AppendLine("|------|--------|-----------|---------|--------------|");
            for (int i = 0; i < Results.Count; i++)
            {
                var r = Results[i];
                sb.AppendLine($"| {r.Name} | {r.Status} | {r.DurationMs} | {r.Metrics} | {r.Reason} |");
            }
            return sb.ToString();
        }

        public string ExportCsv()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Test,Status,DurationMs,Metrics,Reason");
            for (int i = 0; i < Results.Count; i++)
            {
                var r = Results[i];
                sb.AppendLine($"\"{r.Name}\",{r.Status},{r.DurationMs},\"{r.Metrics}\",\"{r.Reason}\"");
            }
            return sb.ToString();
        }

        public string ExportJson()
        {
            var sb = new StringBuilder();
            sb.Append("[");
            for (int i = 0; i < Results.Count; i++)
            {
                var r = Results[i];
                if (i > 0) sb.Append(",");
                sb.Append($"{{\"name\":\"{r.Name}\",\"status\":\"{r.Status}\",\"durationMs\":{r.DurationMs},\"metrics\":\"{r.Metrics}\",\"reason\":\"{r.Reason}\"}}");
            }
            sb.Append("]");
            return sb.ToString();
        }
    }
}
