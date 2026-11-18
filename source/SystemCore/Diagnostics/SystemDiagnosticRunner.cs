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

    /// <summary>
    /// Diagnostics with truthful result boundaries. Real FAT32 I/O is allowed
    /// only on the policy-bound ZONDERQDATA root, and no account is created.
    /// Full soak measures managed heap over >=3600 monotonic seconds, not
    /// physical RAM pages (which are tested by a separate QEMU serial harness).
    /// </summary>
    public sealed class SystemDiagnosticRunner
    {
        public static SystemDiagnosticRunner Shared { get; } = new SystemDiagnosticRunner();

        public bool IsRunning { get; private set; }
        public bool IsAborted { get; private set; }
        public int ProgressPercent { get; private set; }
        public string CurrentScenario { get; private set; } = "Ready";
        public string LastReportPath { get; private set; } = string.Empty;
        public List<DiagnosticResult> Results { get; } = new List<DiagnosticResult>();

        private static readonly int[] SoakMarksSec = { 0, 600, 1200, 1800, 3600 };
        private readonly long[] soakHeapBytes = new long[5];
        private readonly long[] soakRealSeconds = new long[5];
        private int stepIndex;
        private bool includeSoak60Min;
        private bool includeRebootTest;
        private bool soakStarted;
        private int nextSoakMark;
        private long soakStartTicks;
        private int lastReportedSoakMinute;
        private long startHeapBytes;
        private long peakHeapBytes;

        public void RequestAbort()
        {
            if (IsRunning)
            {
                IsAborted = true;
                CurrentScenario = "Interrupted by user";
            }
        }

        public void Start(bool includeRamSoak60Min, bool includeRebootTest)
        {
            if (IsRunning)
                return;

            Results.Clear();
            IsRunning = true;
            IsAborted = false;
            ProgressPercent = 0;
            stepIndex = 0;
            includeSoak60Min = includeRamSoak60Min;
            this.includeRebootTest = includeRebootTest;
            soakStarted = false;
            nextSoakMark = 0;
            lastReportedSoakMinute = -1;
            soakStartTicks = 0;
            startHeapBytes = GC.GetTotalMemory(false);
            peakHeapBytes = startHeapBytes;
            CurrentScenario = "Initializing...";
            LastReportPath = string.Empty;
            for (int i = 0; i < SoakMarksSec.Length; i++)
            {
                soakHeapBytes[i] = 0;
                soakRealSeconds[i] = 0;
            }
        }

        public bool Step()
        {
            if (!IsRunning)
                return false;

            if (IsAborted)
            {
                Results.Add(new DiagnosticResult
                {
                    Name = "Diagnostic interrupted",
                    Status = DiagnosticStatus.Skipped,
                    Reason = "User requested stop; incomplete tests are not PASS",
                    Metrics = "aborted"
                });
                IsRunning = false;
                SaveReports();
                return false;
            }

            switch (stepIndex)
            {
                case 0:
                    CurrentScenario = "Managed heap telemetry";
                    RunRamTelemetry();
                    ProgressPercent = 14;
                    break;
                case 1:
                    CurrentScenario = "CPU basic correctness";
                    RunCpuSchedulerTest();
                    ProgressPercent = 28;
                    break;
                case 2:
                    CurrentScenario = "GUI app cycle coverage";
                    RunAppCycleTest();
                    ProgressPercent = 42;
                    break;
                case 3:
                    CurrentScenario = "FAT32 ZONDERQDATA I/O";
                    RunFat32Test();
                    ProgressPercent = 56;
                    break;
                case 4:
                    CurrentScenario = "Read-only session identity";
                    RunAuthTest();
                    ProgressPercent = 70;
                    break;
                case 5:
                    CurrentScenario = "Basic CPU / uptime";
                    RunHardwareBootTest();
                    ProgressPercent = 85;
                    break;
                case 6:
                    if (!RunOptionalSoakAndReboot())
                        return true;
                    ProgressPercent = 95;
                    break;
                case 7:
                    CurrentScenario = "Saving ZONDERQDATA reports";
                    SaveReports();
                    ProgressPercent = 100;
                    CurrentScenario = "Complete";
                    IsRunning = false;
                    return false;
            }

            long currentHeap = GC.GetTotalMemory(false);
            if (currentHeap > peakHeapBytes)
                peakHeapBytes = currentHeap;
            stepIndex++;
            return IsRunning;
        }

        private void RunRamTelemetry()
        {
            var watch = Stopwatch.StartNew();
            long managedHeap = GC.GetTotalMemory(false);
            watch.Stop();
            Results.Add(new DiagnosticResult
            {
                Name = "Managed heap snapshot",
                Status = managedHeap >= 0 ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = "Managed heap only, NOT total physical RAM usage",
                DurationMs = watch.ElapsedMilliseconds,
                Metrics = "heapKiB=" + (managedHeap / 1024)
            });
        }

        private void RunCpuSchedulerTest()
        {
            var watch = Stopwatch.StartNew();
            int primes = 0;
            for (int n = 2; n < 5000; n++)
            {
                bool prime = true;
                for (int d = 2; d * d <= n; d++)
                {
                    if (n % d == 0)
                    {
                        prime = false;
                        break;
                    }
                }
                if (prime)
                    primes++;
            }
            watch.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "CPU arithmetic (primes < 5000)",
                Status = primes == 669 ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = primes == 669 ? "Correct prime count" : "Incorrect prime count",
                DurationMs = watch.ElapsedMilliseconds,
                Metrics = "primes=" + primes + ",expected=669"
            });
        }

        private void RunAppCycleTest()
        {
            // The old loop only incremented an integer; it never opened a window.
            Results.Add(new DiagnosticResult
            {
                Name = "100 GUI open/close cycles",
                Status = DiagnosticStatus.NotTested,
                Reason = "Requires real GUI/QEMU input automation; no fake 100x PASS",
                Metrics = "guest_ui=not_executed"
            });
        }

        private void RunFat32Test()
        {
            var watch = Stopwatch.StartNew();
            if (!Fat32DiagnosticHelper.TryFindZonderqDataRoot(out string root))
            {
                watch.Stop();
                Results.Add(new DiagnosticResult
                {
                    Name = "ZONDERQDATA FAT32 4 KiB I/O",
                    Status = DiagnosticStatus.Skipped,
                    Reason = "No policy-approved, mounted persistent ZONDERQDATA root",
                    DurationMs = watch.ElapsedMilliseconds,
                    Metrics = "no_approved_root"
                });
                return;
            }

            if (!global::ZonderqOS.SecurityContext.IsAuthenticated ||
                global::ZonderqOS.SecurityContext.CurrentUid != 0)
            {
                watch.Stop();
                Results.Add(new DiagnosticResult
                {
                    Name = "ZONDERQDATA FAT32 4 KiB I/O",
                    Status = DiagnosticStatus.Skipped,
                    Reason = "Write test requires authenticated root",
                    DurationMs = watch.ElapsedMilliseconds,
                    Metrics = "needs_root"
                });
                return;
            }

            var result = Fat32DiagnosticHelper.RunIsolatedFat32Test(root);
            watch.Stop();
            Results.Add(new DiagnosticResult
            {
                Name = "ZONDERQDATA FAT32 4 KiB I/O",
                Status = result.ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = result.reason,
                DurationMs = watch.ElapsedMilliseconds,
                Metrics = result.metrics
            });
        }

        private void RunAuthTest()
        {
            var watch = Stopwatch.StartNew();
            var result = AuthDiagnosticHelper.CheckSessionReadOnly();
            watch.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "Authenticated session identity",
                Status = result.ok ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = result.reason,
                DurationMs = watch.ElapsedMilliseconds,
                Metrics = result.metrics
            });
            Results.Add(new DiagnosticResult
            {
                Name = "50 Lock/Unlock + 20 failed passwords",
                Status = DiagnosticStatus.NotTested,
                Reason = "No real lock-screen interaction here; covered separately by QEMU E2E",
                Metrics = "accounts_created=0"
            });
        }

        private void RunHardwareBootTest()
        {
            var watch = Stopwatch.StartNew();
            int cpus = Environment.ProcessorCount;
            long uptimeMs = Environment.TickCount64;
            watch.Stop();

            Results.Add(new DiagnosticResult
            {
                Name = "Runtime CPU count and uptime",
                Status = cpus > 0 ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                Reason = "Runtime responds; this does not verify BootGuard EFI",
                DurationMs = watch.ElapsedMilliseconds,
                Metrics = "cpus=" + cpus + ",uptimeMs=" + uptimeMs
            });
            Results.Add(new DiagnosticResult
            {
                Name = "UEFI BootGuard -> Limine -> kernel",
                Status = DiagnosticStatus.NotTested,
                Reason = "Booted OS alone cannot prove that the EFI BootGuard executed",
                Metrics = "firmware_chain=not_inspected"
            });
        }

        // Returns false while a REAL measured hour is still in progress.
        // Does not busy-wait in Step(), so the GUI may keep rendering.
        private bool RunOptionalSoakAndReboot()
        {
            if (!includeSoak60Min)
            {
                Results.Add(new DiagnosticResult
                {
                    Name = "Managed heap soak >=3600 seconds",
                    Status = DiagnosticStatus.Skipped,
                    Reason = "Use sysbench --soak for real 60-minute elapsed sampling",
                    Metrics = "not_started"
                });
            }
            else
            {
                if (Stopwatch.Frequency <= 0)
                {
                    Results.Add(new DiagnosticResult
                    {
                        Name = "Managed heap soak >=3600 seconds",
                        Status = DiagnosticStatus.Fail,
                        Reason = "Monotonic clock not available",
                        Metrics = "no_monotonic_clock"
                    });
                }
                else
                {
                    if (!soakStarted)
                    {
                        soakStarted = true;
                        soakStartTicks = Stopwatch.GetTimestamp();
                        soakHeapBytes[0] = GC.GetTotalMemory(false);
                        soakRealSeconds[0] = 0;
                        nextSoakMark = 1;
                        lastReportedSoakMinute = 0;
                        CurrentScenario = "Managed heap soak: 0/60 minutes";
                        return false;
                    }

                    long ticks = Stopwatch.GetTimestamp() - soakStartTicks;
                    if (ticks < 0)
                    {
                        Results.Add(new DiagnosticResult
                        {
                            Name = "Managed heap soak >=3600 seconds",
                            Status = DiagnosticStatus.Fail,
                            Reason = "Monotonic time went backwards",
                            Metrics = "timer_error"
                        });
                    }
                    else
                    {
                        long elapsedSeconds = ticks / Stopwatch.Frequency;
                        while (nextSoakMark < SoakMarksSec.Length &&
                               elapsedSeconds >= SoakMarksSec[nextSoakMark])
                        {
                            soakHeapBytes[nextSoakMark] = GC.GetTotalMemory(false);
                            soakRealSeconds[nextSoakMark] = elapsedSeconds;
                            nextSoakMark++;
                        }

                        if (elapsedSeconds < 3600)
                        {
                            int minute = (int)(elapsedSeconds / 60);
                            if (minute != lastReportedSoakMinute)
                            {
                                lastReportedSoakMinute = minute;
                                CurrentScenario = "Managed heap soak: " + minute + "/60 minutes";
                            }
                            ProgressPercent = 85 + (int)(elapsedSeconds * 10 / 3600);
                            return false;
                        }

                        long delta = soakHeapBytes[4] - soakHeapBytes[0];
                        bool complete = nextSoakMark == SoakMarksSec.Length &&
                                        soakRealSeconds[4] >= 3600;
                        // This is managed heap growth, not whole-system RAM.
                        // The 10 MiB bound is a basic diagnostic threshold.
                        bool withinLimit = delta <= 10L * 1024L * 1024L;
                        Results.Add(new DiagnosticResult
                        {
                            Name = "Managed heap soak >=3600 seconds",
                            Status = complete && withinLimit
                                ? DiagnosticStatus.Pass : DiagnosticStatus.Fail,
                            Reason = !complete ? "Insufficient elapsed samples"
                                : withinLimit ? "Real monotonic 60-min managed heap check complete"
                                              : "Managed heap growth exceeded 10 MiB",
                            DurationMs = elapsedSeconds * 1000,
                            Metrics = "seconds=" + soakRealSeconds[4] +
                                      ",deltaKiB=" + (delta / 1024) +
                                      ",t0KiB=" + (soakHeapBytes[0] / 1024) +
                                      ",t10KiB=" + (soakHeapBytes[1] / 1024) +
                                      ",t20KiB=" + (soakHeapBytes[2] / 1024) +
                                      ",t30KiB=" + (soakHeapBytes[3] / 1024) +
                                      ",t60KiB=" + (soakHeapBytes[4] / 1024)
                        });
                    }
                }
            }

            Results.Add(new DiagnosticResult
            {
                Name = "Reboot resume",
                Status = DiagnosticStatus.NotTested,
                Reason = includeRebootTest
                    ? "Requested, but automatic reboot/resume is NOT implemented; no reboot triggered"
                    : "Not requested; never triggers an automatic reboot",
                Metrics = "reboots=0"
            });
            return true;
        }

        private void SaveReports()
        {
            LastReportPath = "NOT_SAVED (see terminal output)";
            if (!Fat32DiagnosticHelper.TryFindZonderqDataRoot(out string root) ||
                root != "/" || !global::ZonderqOS.SecurityContext.IsAuthenticated ||
                global::ZonderqOS.SecurityContext.CurrentUid != 0)
                return;

            // Stable file names at the actual USB data partition root, so
            // Linux can read sysbench_report.md after a normal shutdown.
            try
            {
                string md = Path.Combine(root, "sysbench_report.md");
                File.WriteAllText(md, ExportMarkdown());
                LastReportPath = md;

                // Optional machine-readable companions must not make the
                // already persisted Markdown report disappear on failure.
                try
                {
                    File.WriteAllText(Path.Combine(root, "sysbench_report.csv"), ExportCsv());
                    File.WriteAllText(Path.Combine(root, "sysbench_report.json"), ExportJson());
                }
                catch (Exception)
                {
                    // Markdown is the canonical user-facing report.
                }
            }
            catch (Exception ex)
            {
                LastReportPath = "NOT_SAVED: " + ex.Message;
            }
        }

        public string ExportMarkdown()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("# ZOnderqOS System Benchmark & Diagnostics");
            sb.AppendLine("Report destination: /sysbench_report.md on policy-approved ZONDERQDATA");
            sb.AppendLine("PASS only means the named check actually executed.");
            sb.AppendLine("NOT_TESTED entries are not software failures.");
            sb.AppendLine("| Test | Status | Time (ms) | Metrics | Reason |");
            sb.AppendLine("|------|--------|-----------|---------|--------|");
            for (int i = 0; i < Results.Count; i++)
            {
                DiagnosticResult r = Results[i];
                sb.Append("| ").Append(MarkdownSafe(r.Name)).Append(" | ")
                  .Append(r.Status == DiagnosticStatus.NotTested ? "NOT_TESTED" : r.Status.ToString())
                  .Append(" | ").Append(r.DurationMs)
                  .Append(" | ").Append(MarkdownSafe(r.Metrics))
                  .Append(" | ").Append(MarkdownSafe(r.Reason)).AppendLine(" |");
            }
            return sb.ToString();
        }

        public string ExportCsv()
        {
            var sb = new StringBuilder(2048);
            sb.AppendLine("Test,Status,DurationMs,Metrics,Reason");
            for (int i = 0; i < Results.Count; i++)
            {
                DiagnosticResult r = Results[i];
                sb.Append(CsvField(r.Name)).Append(",")
                  .Append(r.Status).Append(",")
                  .Append(r.DurationMs).Append(",")
                  .Append(CsvField(r.Metrics)).Append(",")
                  .Append(CsvField(r.Reason)).AppendLine();
            }
            return sb.ToString();
        }

        public string ExportJson()
        {
            var sb = new StringBuilder(2048);
            sb.Append("[");
            for (int i = 0; i < Results.Count; i++)
            {
                DiagnosticResult r = Results[i];
                if (i != 0) sb.Append(",");
                sb.Append("{\"name\":\"").Append(JsonSafe(r.Name))
                  .Append("\",\"status\":\"").Append(r.Status)
                  .Append("\",\"durationMs\":").Append(r.DurationMs)
                  .Append(",\"metrics\":\"").Append(JsonSafe(r.Metrics))
                  .Append("\",\"reason\":\"").Append(JsonSafe(r.Reason))
                  .Append("\"}");
            }
            sb.Append("]");
            return sb.ToString();
        }

        private static string MarkdownSafe(string value) =>
            (value ?? string.Empty).Replace("|", "/").Replace("\r", " ").Replace("\n", " ");

        private static string CsvField(string value) =>
            "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";

        private static string JsonSafe(string value)
        {
            if (value == null)
                return string.Empty;
            var sb = new StringBuilder(value.Length);
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                switch (c)
                {
                    case '\\': sb.Append("\\\\"); break;
                    case '"': sb.Append("\\\""); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        sb.Append(c < ' ' ? '?' : c);
                        break;
                }
            }
            return sb.ToString();
        }
    }
}
