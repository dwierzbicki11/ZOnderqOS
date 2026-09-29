using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cosmos.Kernel.System.Diagnostics;
using ZonderqOS.SystemCore;

namespace ZonderqOS.Commands
{
    public sealed class CmdSmtCheck : ICommand
    {
        public string Name => "smtcheck";
        public string Description => "Verify automatic managed-thread distribution across logical CPUs";

        public void Execute(string[] args, ref string currentPath)
        {
            int cpuCount = checked((int)SchedulerInfo.CpuCount);
            int workerCount = cpuCount;

            if (args.Length > 2 || (args.Length == 2 && (!int.TryParse(args[1], out workerCount) || workerCount < 1 || workerCount > 128)))
            {
                CommandIO.WriteLine("Usage: smtcheck [workers]");
                CommandIO.WriteLine("Default workers = number of logical CPUs; allowed range: 1..128.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (cpuCount <= 0)
            {
                CommandIO.WriteLine("[SMTCHECK] FAIL: scheduler reports no logical CPUs.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            var pids = new List<int>(workerCount);
            int started = 0;
            int release = 0;

            try
            {
                CommandIO.WriteLine("ZOnderqOS SMT runtime check");
                CommandIO.WriteLine("Logical CPUs: " + cpuCount + " | workers: " + workerCount);
                CommandIO.WriteLine("Starting ordinary ProcessManager managed threads...");

                for (int i = 0; i < workerCount; i++)
                {
                    int workerIndex = i;
                    int pid = ProcessManager.Start("smtcheck-" + workerIndex, token =>
                    {
                        Interlocked.Increment(ref started);
                        while (Volatile.Read(ref release) == 0 && !token.IsCancellationRequested)
                            Thread.SpinWait(256);
                    });
                    pids.Add(pid);
                }

                long timeout = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 5;
                while (Volatile.Read(ref started) < workerCount && Stopwatch.GetTimestamp() < timeout)
                    Thread.Sleep(10);

                if (Volatile.Read(ref started) != workerCount)
                {
                    CommandIO.WriteLine("[SMTCHECK] FAIL: only " + Volatile.Read(ref started) + "/" + workerCount + " workers entered their delegates.");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                // Give the production scheduler a short window to place/wake AP workers.
                Thread.Sleep(250);

                ulong cpuMask = 0;
                int uniqueCpus = 0;
                int mappedWorkers = 0;
                bool[] seenCpu = new bool[cpuCount];
                var processes = ProcessManager.GetActiveProcesses();

                CommandIO.WriteLine("PID    TID    CPU    NAME");
                CommandIO.WriteLine("----------------------------------------");

                for (int i = 0; i < pids.Count; i++)
                {
                    KernelProcess process = FindProcess(processes, pids[i]);
                    if (process == null)
                    {
                        CommandIO.WriteLine(pids[i].ToString().PadRight(7) + "-      -      <exited>");
                        continue;
                    }

                    uint tid = process.KernelThreadId;
                    if (tid == uint.MaxValue || !TryFindThread(tid, out KernelThreadInfo thread))
                    {
                        CommandIO.WriteLine(process.PID.ToString().PadRight(7) + "-      -      " + process.Name);
                        continue;
                    }

                    mappedWorkers++;
                    int cpu = checked((int)thread.CpuId);
                    if (cpu >= 0 && cpu < cpuCount && !seenCpu[cpu])
                    {
                        seenCpu[cpu] = true;
                        uniqueCpus++;
                        if (cpu < 64)
                            cpuMask |= 1UL << cpu;
                    }

                    CommandIO.WriteLine(
                        process.PID.ToString().PadRight(7) +
                        tid.ToString().PadRight(7) +
                        cpu.ToString().PadRight(7) +
                        process.Name);
                }

                ulong expectedMask = cpuCount >= 64 ? ulong.MaxValue : ((1UL << cpuCount) - 1UL);
                bool fullCoverage = workerCount >= cpuCount && cpuCount <= 64 && cpuMask == expectedMask;
                bool assignmentWorking = mappedWorkers == workerCount && (cpuCount == 1 ? uniqueCpus == 1 : uniqueCpus > 1);

                CommandIO.WriteLine("----------------------------------------");
                CommandIO.WriteLine("Mapped workers: " + mappedWorkers + "/" + workerCount);
                CommandIO.WriteLine("Unique logical CPUs used: " + uniqueCpus + "/" + cpuCount);
                CommandIO.WriteLine("CPU mask: 0x" + cpuMask.ToString("X"));

                if (fullCoverage)
                {
                    CommandIO.WriteLine("[SMTCHECK] PASS: scheduler automatically distributed ordinary managed processes across ALL logical CPUs.");
                    CommandIO.LastCommandSuccess = true;
                }
                else if (assignmentWorking)
                {
                    CommandIO.WriteLine("[SMTCHECK] PASS-PARTIAL: automatic multi-CPU assignment works, but this snapshot did not cover every logical CPU.");
                    CommandIO.WriteLine("Run again with more workers, e.g. smtcheck " + Math.Min(128, Math.Max(cpuCount * 2, workerCount + 1)) + ".");
                    CommandIO.LastCommandSuccess = true;
                }
                else
                {
                    CommandIO.WriteLine("[SMTCHECK] FAIL: ordinary managed workers were not observed on multiple logical CPUs.");
                    CommandIO.LastCommandSuccess = false;
                }
            }
            finally
            {
                Volatile.Write(ref release, 1);
                for (int i = 0; i < pids.Count; i++)
                    ProcessManager.Kill(pids[i]);
            }
        }

        private static KernelProcess FindProcess(List<KernelProcess> processes, int pid)
        {
            for (int i = 0; i < processes.Count; i++)
                if (processes[i].PID == pid)
                    return processes[i];
            return null;
        }

        private static bool TryFindThread(uint tid, out KernelThreadInfo thread)
        {
            int slots = SchedulerInfo.ThreadSlotCount;
            for (int slot = 0; slot < slots; slot++)
            {
                if (SchedulerInfo.TryGetThreadInSlot(slot, out KernelThreadInfo candidate) && candidate.Id == tid)
                {
                    thread = candidate;
                    return true;
                }
            }

            thread = default;
            return false;
        }
    }
}
