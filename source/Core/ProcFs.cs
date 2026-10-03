using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Cosmos.Kernel.System.Diagnostics;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Lightweight virtual /proc surface for the shell. No proc data is persisted
    /// to FAT; every read is generated from live kernel/process telemetry.
    /// </summary>
    public static class ProcFs
    {
        public static bool IsProcPath(string path)
        {
            string normalized = Normalize(path);
            return normalized == "/proc" || normalized.StartsWith("/proc/", StringComparison.Ordinal);
        }

        public static bool TryRead(string path, out string content)
        {
            string normalized = Normalize(path);
            content = null;

            if (normalized == "/proc/cpuinfo")
            {
                content = BuildCpuInfo();
                return true;
            }

            if (normalized == "/proc/meminfo")
            {
                content = BuildMemInfo();
                return true;
            }

            if (normalized == "/proc/uptime")
            {
                content = BuildUptime();
                return true;
            }

            if (normalized == "/proc/version")
            {
                content = "ZOnderqOS Gen 3 (Cosmos Kernel)\n";
                return true;
            }

            if (normalized == "/proc/stat")
            {
                content = BuildStat();
                return true;
            }

            if (TryParsePidPath(normalized, out int pid, out string leaf))
            {
                KernelProcess process = FindProcess(pid);
                if (process == null)
                    return false;

                if (leaf == "comm")
                {
                    content = process.Name + "\n";
                    return true;
                }

                if (leaf == "status")
                {
                    content = BuildProcessStatus(process);
                    return true;
                }

                if (leaf == "stat")
                {
                    content = BuildProcessStat(process);
                    return true;
                }
            }

            return false;
        }

        public static bool TryList(string path, out string[] entries)
        {
            string normalized = Normalize(path);
            entries = null;

            if (normalized == "/proc")
            {
                List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
                entries = new string[5 + processes.Count];
                entries[0] = "cpuinfo";
                entries[1] = "meminfo";
                entries[2] = "uptime";
                entries[3] = "version";
                entries[4] = "stat";
                for (int i = 0; i < processes.Count; i++)
                    entries[5 + i] = processes[i].PID.ToString();
                return true;
            }

            if (TryParsePidDirectory(normalized, out int pid))
            {
                KernelProcess process = FindProcess(pid);
                if (process == null)
                    return false;

                entries = new[] { "comm", "status", "stat" };
                return true;
            }

            return false;
        }

        private static string BuildCpuInfo()
        {
            var sb = new StringBuilder();
            int count = checked((int)SchedulerInfo.CpuCount);

            for (int cpu = 0; cpu < count; cpu++)
            {
                sb.Append("processor\t: ").Append(cpu).Append('\n');
                sb.Append("scheduler\t: ").Append(SchedulerInfo.SchedulerName).Append('\n');
                sb.Append("logical cpu\t: ").Append(cpu).Append('\n');
                sb.Append("smp\t\t: ").Append(count > 1 ? "yes" : "no").Append('\n');
                sb.Append('\n');
            }

            return sb.ToString();
        }

        private static string BuildMemInfo()
        {
            ulong totalPages = MemoryInfo.TotalPages;
            ulong freePages = Math.Min(totalPages, MemoryInfo.FreePages);
            ulong pageSize = MemoryInfo.PageSizeBytes;
            ulong totalKiB = totalPages * pageSize / 1024UL;
            ulong freeKiB = freePages * pageSize / 1024UL;
            ulong usedKiB = totalKiB >= freeKiB ? totalKiB - freeKiB : 0;

            var sb = new StringBuilder();
            sb.Append("MemTotal:       ").Append(totalKiB).Append(" kB\n");
            sb.Append("MemFree:        ").Append(freeKiB).Append(" kB\n");
            sb.Append("MemUsed:        ").Append(usedKiB).Append(" kB\n");
            sb.Append("PageSize:       ").Append(pageSize).Append(" B\n");
            sb.Append("PagesTotal:     ").Append(totalPages).Append('\n');
            sb.Append("PagesFree:      ").Append(freePages).Append('\n');
            return sb.ToString();
        }

        private static string BuildUptime()
        {
            long uptimeTicks = BootTelemetry.UptimeTicks;
            double uptimeSeconds = Stopwatch.Frequency > 0
                ? (double)uptimeTicks / Stopwatch.Frequency
                : 0d;

            if (!SchedulerTelemetry.HasCpuAccounting || Stopwatch.Frequency <= 0)
                return uptimeSeconds.ToString("0.00") + " N/A\n";

            int cpuCount = checked((int)SchedulerInfo.CpuCount);
            ulong totalCapacity = cpuCount > 0 && uptimeTicks > 0
                ? SaturatingMultiply((ulong)uptimeTicks, (ulong)cpuCount)
                : 0;

            ulong busyTicks = 0;
            for (int cpu = 0; cpu < cpuCount; cpu++)
            {
                long busy = SchedulerTelemetry.CpuBusyTicks((uint)cpu);
                if (busy <= 0)
                    continue;

                ulong value = (ulong)busy;
                busyTicks = busyTicks > ulong.MaxValue - value
                    ? ulong.MaxValue
                    : busyTicks + value;
            }

            ulong idleTicks = totalCapacity >= busyTicks ? totalCapacity - busyTicks : 0;
            double idleSeconds = (double)idleTicks / Stopwatch.Frequency;
            return uptimeSeconds.ToString("0.00") + " " + idleSeconds.ToString("0.00") + "\n";
        }


        private static string BuildStat()
        {
            var sb = new StringBuilder();
            int cpuCount = checked((int)SchedulerInfo.CpuCount);
            long uptimeTicks = BootTelemetry.UptimeTicks;
            bool accounting = SchedulerTelemetry.HasCpuAccounting;

            ulong totalBusy = 0;
            ulong totalIdle = 0;

            for (int cpu = 0; cpu < cpuCount; cpu++)
            {
                if (!accounting)
                {
                    sb.Append("cpu").Append(cpu).Append(" busy=N/A idle=N/A\n");
                    continue;
                }

                long busyRaw = SchedulerTelemetry.CpuBusyTicks((uint)cpu);
                ulong busy = busyRaw > 0 ? (ulong)busyRaw : 0UL;
                ulong capacity = uptimeTicks > 0 ? (ulong)uptimeTicks : 0UL;
                ulong idle = capacity >= busy ? capacity - busy : 0UL;

                totalBusy = AddSaturated(totalBusy, busy);
                totalIdle = AddSaturated(totalIdle, idle);

                sb.Append("cpu").Append(cpu)
                  .Append(" busy=").Append(busy)
                  .Append(" idle=").Append(idle)
                  .Append('\n');
            }

            if (accounting)
            {
                sb.Insert(0, "cpu busy=" + totalBusy + " idle=" + totalIdle + "\n");
            }
            else
            {
                sb.Insert(0, "cpu busy=N/A idle=N/A\n");
            }

            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            int running = 0;
            for (int i = 0; i < processes.Count; i++)
            {
                if (processes[i].IsRunning)
                    running++;
            }

            sb.Append("processes ").Append(processes.Count).Append('\n');
            sb.Append("procs_running ").Append(running).Append('\n');
            return sb.ToString();
        }

        private static ulong AddSaturated(ulong left, ulong right)
        {
            return left > ulong.MaxValue - right ? ulong.MaxValue : left + right;
        }

        private static ulong SaturatingMultiply(ulong left, ulong right)
        {
            if (left == 0 || right == 0)
                return 0;
            if (left > ulong.MaxValue / right)
                return ulong.MaxValue;
            return left * right;
        }

        private static string BuildProcessStatus(KernelProcess process)
        {
            var sb = new StringBuilder();
            sb.Append("Name:\t").Append(process.Name).Append('\n');
            sb.Append("State:\t").Append(process.IsRunning ? "R (running)" : "Z (zombie)").Append('\n');
            sb.Append("Pid:\t").Append(process.PID).Append('\n');
            sb.Append("Tgid:\t").Append(process.PID).Append('\n');
            sb.Append("KernelTid:\t");
            if (process.KernelThreadId == uint.MaxValue)
                sb.Append("-1");
            else
                sb.Append(process.KernelThreadId);
            sb.Append('\n');
            sb.Append("LastSignal:\t");
            if (process.LastSignal == 0)
                sb.Append("none");
            else
                sb.Append(process.LastSignal).Append(" (")
                  .Append(ProcessSignalNames.Name((ProcessSignal)process.LastSignal)).Append(')');
            sb.Append('\n');
            return sb.ToString();
        }

        private static string BuildProcessStat(KernelProcess process)
        {
            string state = process.IsRunning ? "R" : "Z";
            long tid = process.KernelThreadId == uint.MaxValue ? -1 : process.KernelThreadId;
            return process.PID + " (" + process.Name + ") " + state + " 0 0 0 " + tid + "\n";
        }

        private static KernelProcess FindProcess(int pid)
        {
            List<KernelProcess> processes = ProcessManager.GetActiveProcesses();
            for (int i = 0; i < processes.Count; i++)
            {
                if (processes[i].PID == pid)
                    return processes[i];
            }
            return null;
        }

        private static bool TryParsePidDirectory(string path, out int pid)
        {
            pid = 0;
            if (!path.StartsWith("/proc/", StringComparison.Ordinal))
                return false;

            string tail = path.Substring(6);
            return tail.Length > 0 && tail.IndexOf('/') < 0 && int.TryParse(tail, out pid) && pid > 0;
        }

        private static bool TryParsePidPath(string path, out int pid, out string leaf)
        {
            pid = 0;
            leaf = string.Empty;
            if (!path.StartsWith("/proc/", StringComparison.Ordinal))
                return false;

            string tail = path.Substring(6);
            int slash = tail.IndexOf('/');
            if (slash <= 0 || slash == tail.Length - 1)
                return false;

            if (!int.TryParse(tail.Substring(0, slash), out pid) || pid <= 0)
                return false;

            leaf = tail.Substring(slash + 1);
            return leaf.IndexOf('/') < 0;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            string value = path.Replace('\\', '/').Trim();
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
    }
}
