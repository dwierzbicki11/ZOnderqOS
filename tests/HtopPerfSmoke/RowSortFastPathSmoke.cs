using System;
using System.Diagnostics;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

internal static class RowSortFastPathSmoke
{
    private static long sink;

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void VerifyOrderedAndReversed(int count)
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(1000 + i), CpuId = 0, State = KernelThreadState.READY,
                    IsManaged = true
                };
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            SchedulerInfo.Threads = threads;
            var monitor = new HtopMonitor();
            monitor.Refresh();
            for (int i = 0; i < count; i++)
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(i, threads[i], 0, 0, "managed thread")),
                    "already ordered snapshot " + count + " index " + i);
            monitor.SortRows();
            monitor.SortRowsUnconditionallyForBenchmark();
            for (int i = 0; i < count; i++)
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(i, threads[i], 0, 0, "managed thread")),
                    "sort mutated a stable row cache " + i);

            // Force the full-sort fallback: scheduler slot order is descending
            // while the expected CPU-sort tie breaker is ascending TID.
            Array.Reverse(threads);
            monitor.Refresh();
            for (int i = 0; i < count; i++)
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(count - 1 - i, threads[count - 1 - i],
                        0, 0, "managed thread")),
                    "reversed scheduler slots not sorted " + count + " index " + i);
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    private static void VerifyPidModeSwitch()
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            const int count = 64;
            var threads = new KernelThreadInfo[count];
            var processes = new KernelProcess[count];
            for (int i = 0; i < count; i++)
            {
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(2000 + i), CpuId = 0, State = KernelThreadState.READY,
                    IsManaged = true
                };
                processes[i] = new KernelProcess(count - i, "worker", threads[i].Id);
            }
            SchedulerInfo.Threads = threads;
            ProcessManager.TestProcesses = processes;
            var monitor = new HtopMonitor();
            monitor.SortByCpu = true;
            monitor.Refresh();
            for (int i = 0; i < count; i++)
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(i, threads[i], processes[i].PID, 0, "worker")),
                    "CPU mode ordering " + i);

            monitor.SortByCpu = false;
            monitor.Refresh();
            for (int i = 0; i < count; i++)
            {
                int slot = count - 1 - i;
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(slot, threads[slot], processes[slot].PID, 0, "worker")),
                    "PID mode ordering " + i);
            }
            monitor.SortByCpu = true;
            monitor.Refresh();
            for (int i = 0; i < count; i++)
                Check(object.ReferenceEquals(monitor.Lines[8 + i],
                    monitor.GetThreadRowLine(i, threads[i], processes[i].PID, 0, "worker")),
                    "return to CPU mode " + i);
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    private static double Time(Action action, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++) action();
        return (Stopwatch.GetTimestamp() - start) * 1000d /
            Stopwatch.Frequency / repeats;
    }

    private static double Median(double[] values)
    {
        Array.Sort(values);
        return values[values.Length / 2];
    }

    private static void BenchmarkOrdered(int count)
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(10000 + i), CpuId = 0,
                    State = KernelThreadState.READY, IsManaged = true
                };
            SchedulerInfo.Threads = threads;
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            var monitor = new HtopMonitor();
            monitor.Refresh();

            Action before = () => { monitor.SortRowsUnconditionallyForBenchmark(); sink++; };
            Action after = () => { monitor.SortRows(); sink++; };
            for (int i = 0; i < 16; i++) { before(); after(); }

            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) before();
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) after();
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(oldBytes == 0 && newBytes == 0,
                "warmed production row sort must allocate 0 bytes: " +
                oldBytes + " / " + newBytes);

            double[] oldSamples = new double[7], newSamples = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    oldSamples[i] = Time(before, 1024);
                    newSamples[i] = Time(after, 1024);
                }
                else
                {
                    newSamples[i] = Time(after, 1024);
                    oldSamples[i] = Time(before, 1024);
                }
            }
            double oldMs = Median(oldSamples), newMs = Median(newSamples);
            Console.WriteLine("HOST A/B ordered htop " + count +
                " thread rows: List.Sort=" + oldMs.ToString("F5") +
                "ms guarded=" + newMs.ToString("F5") +
                "ms ratio=" + (oldMs / newMs).ToString("F2") +
                "x allocations=" + oldBytes + "B -> " + newBytes +
                "B (7 alternating samples; not kernel CPU/idle)");
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    private static void VerifyRandomizedFallback()
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            const int count = 256;
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(5000 + i), CpuId = 0,
                    State = KernelThreadState.READY, IsManaged = true
                };
            SchedulerInfo.Threads = threads;
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            var monitor = new HtopMonitor();
            var random = new Random(3095);
            for (int trial = 0; trial < 32; trial++)
            {
                for (int i = count - 1; i > 0; i--)
                {
                    int j = random.Next(i + 1);
                    KernelThreadInfo item = threads[i];
                    threads[i] = threads[j];
                    threads[j] = item;
                }
                monitor.Refresh();
                for (int i = 0; i < count; i++)
                {
                    uint tid = uint.Parse(monitor.Lines[8 + i].Substring(6, 6).Trim());
                    Check(tid == (uint)(5000 + i),
                        "random fallback trial " + trial + " row " + i);
                }
            }
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    private static void BenchmarkReversed(int count)
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(30000 + i), CpuId = 0,
                    State = KernelThreadState.READY, IsManaged = true
                };
            SchedulerInfo.Threads = threads;
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            var monitor = new HtopMonitor();
            monitor.Refresh();

            // Each invocation starts from the same strictly descending rows,
            // then restores ascending order. Both paths include List.Reverse
            // setup, so the comparison isolates the sort implementation.
            Action before = () =>
            {
                monitor.ReverseRowsForBenchmark();
                monitor.SortRowsUnconditionallyForBenchmark();
                sink++;
            };
            Action after = () =>
            {
                monitor.ReverseRowsForBenchmark();
                monitor.SortRows();
                sink++;
            };
            for (int i = 0; i < 16; i++) { before(); after(); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) before();
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) after();
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(oldBytes == 0 && newBytes == 0,
                "reversed row sorts must remain allocation-free");

            double[] oldSamples = new double[7], newSamples = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    oldSamples[i] = Time(before, 1024);
                    newSamples[i] = Time(after, 1024);
                }
                else
                {
                    newSamples[i] = Time(after, 1024);
                    oldSamples[i] = Time(before, 1024);
                }
            }
            double oldMs = Median(oldSamples), newMs = Median(newSamples);
            Console.WriteLine("HOST A/B reversed htop " + count +
                " thread rows: List.Sort=" + oldMs.ToString("F5") +
                "ms guarded=" + newMs.ToString("F5") +
                "ms ratio=" + (oldMs / newMs).ToString("F2") +
                "x allocations=" + oldBytes + "B -> " + newBytes +
                "B (7 alternating samples; not kernel CPU/idle)");
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    // A single scheduler-slot displacement should be corrected without a
    // full sort. Compare against the exact production List.Sort baseline.
    private static void VerifySingleMovedRows(int count)
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(40000 + i), CpuId = 0,
                    State = KernelThreadState.READY, IsManaged = true
                };
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            SchedulerInfo.Threads = threads;
            var monitor = new HtopMonitor();
            int[,] moves = {
                { 0, count - 1 },
                { count - 1, 0 },
                { count / 4, count * 3 / 4 },
                { count * 3 / 4, count / 4 },
                { count / 2, count / 2 + 1 },
                { count / 2 + 1, count / 2 }
            };
            for (int trial = 0; trial < moves.GetLength(0); trial++)
            {
                // Restore ascending slots, then displace exactly one item.
                for (int i = 0; i < count; i++)
                    threads[i].Id = (uint)(40000 + i);
                int from = moves[trial, 0], to = moves[trial, 1];
                uint moved = threads[from].Id;
                if (from < to)
                    for (int i = from; i < to; i++)
                        threads[i].Id = threads[i + 1].Id;
                else
                    for (int i = from; i > to; i--)
                        threads[i].Id = threads[i - 1].Id;
                threads[to].Id = moved;
                monitor.Refresh();
                for (int i = 0; i < count; i++)
                {
                    uint tid = uint.Parse(monitor.Lines[8 + i].Substring(6, 6).Trim());
                    Check(tid == (uint)(40000 + i),
                        "single move " + count + " trial " + trial + " row " + i);
                }
            }
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    private static void BenchmarkSingleMove(int count, bool moveToFront)
    {
        KernelProcess[] oldProcesses = ProcessManager.TestProcesses;
        KernelThreadInfo[] oldThreads = SchedulerInfo.Threads;
        try
        {
            var threads = new KernelThreadInfo[count];
            for (int i = 0; i < count; i++)
                threads[i] = new KernelThreadInfo
                {
                    Id = (uint)(10000 + i), CpuId = 0,
                    State = KernelThreadState.READY, IsManaged = true
                };
            SchedulerInfo.Threads = threads;
            ProcessManager.TestProcesses = Array.Empty<KernelProcess>();
            var monitor = new HtopMonitor();
            monitor.Refresh();
            Action before = () =>
            {
                monitor.MoveRowForBenchmark(moveToFront ? count - 1 : 0,
                    moveToFront ? 0 : count - 1);
                monitor.SortRowsUnconditionallyForBenchmark();
                sink++;
            };
            Action after = () =>
            {
                monitor.MoveRowForBenchmark(moveToFront ? count - 1 : 0,
                    moveToFront ? 0 : count - 1);
                monitor.SortRows();
                sink++;
            };
            for (int i = 0; i < 16; i++) { before(); after(); }
            long start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) before();
            long oldBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            start = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) after();
            long newBytes = GC.GetAllocatedBytesForCurrentThread() - start;
            Check(oldBytes == 0 && newBytes == 0,
                "single move htop sort must not allocate");
            double[] oldSamples = new double[7], newSamples = new double[7];
            for (int i = 0; i < 7; i++)
            {
                if ((i & 1) == 0)
                {
                    oldSamples[i] = Time(before, 1024);
                    newSamples[i] = Time(after, 1024);
                }
                else
                {
                    newSamples[i] = Time(after, 1024);
                    oldSamples[i] = Time(before, 1024);
                }
            }
            double oldMs = Median(oldSamples), newMs = Median(newSamples);
            Console.WriteLine("HOST A/B single-move htop " + count +
                (moveToFront ? " front" : " back") +
                " rows: List.Sort=" + oldMs.ToString("F5") +
                "ms insertion=" + newMs.ToString("F5") +
                "ms ratio=" + (oldMs / newMs).ToString("F2") +
                "x allocations=" + oldBytes + "B -> " + newBytes +
                "B (7 alternating samples; includes identical move setup)");
        }
        finally
        {
            ProcessManager.TestProcesses = oldProcesses;
            SchedulerInfo.Threads = oldThreads;
        }
    }

    internal static void Run()
    {
        VerifyOrderedAndReversed(1);
        VerifyOrderedAndReversed(31);
        VerifyOrderedAndReversed(32);
        VerifyOrderedAndReversed(128);
        VerifyOrderedAndReversed(512);
        VerifyPidModeSwitch();
        VerifyRandomizedFallback();
        VerifySingleMovedRows(32);
        VerifySingleMovedRows(128);
        VerifySingleMovedRows(512);
        BenchmarkOrdered(128);
        BenchmarkOrdered(512);
        BenchmarkReversed(128);
        BenchmarkReversed(512);
        BenchmarkSingleMove(128, false);
        BenchmarkSingleMove(128, true);
        BenchmarkSingleMove(256, false);
        BenchmarkSingleMove(256, true);
        BenchmarkSingleMove(512, false);
        BenchmarkSingleMove(512, true);
        GC.KeepAlive(sink);
        Console.WriteLine("PASS: production htop ordered/reversed/PID sort fast-path regression");
    }
}
