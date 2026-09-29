using System;
using System.Collections.Generic;
using System.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.System.Diagnostics;
using Cosmos.Kernel.System.Storage;

namespace ZonderqOS.SystemCore
{
    public enum SystemCheckSeverity
    {
        Pass = 0,
        Warning = 1,
        Fail = 2
    }

    public sealed class SystemCheckResult
    {
        public string Name { get; }
        public SystemCheckSeverity Severity { get; }
        public string Message { get; }

        public SystemCheckResult(string name, SystemCheckSeverity severity, string message)
        {
            Name = string.IsNullOrWhiteSpace(name) ? "CHECK" : name;
            Severity = severity;
            Message = message ?? string.Empty;
        }
    }

    /// <summary>
    /// Read-only health checks for kernel invariants that can be evaluated
    /// without changing scheduler, VMM, driver or filesystem state.
    /// </summary>
    public static class SystemDoctor
    {
        private static readonly List<KernelProcess> ProcessScratch = new List<KernelProcess>(32);
        private static readonly object ProcessScratchLock = new object();

        public static int Run(List<SystemCheckResult> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            destination.Clear();

            CheckMemory(destination);
            CheckScheduler(destination);
            CheckStorage(destination);
            CheckFilesystem(destination);
            CheckSecurityContext(destination);
            CheckLogger(destination);
            CheckProcesses(destination);

            int failures = 0;
            for (int i = 0; i < destination.Count; i++)
            {
                if (destination[i].Severity == SystemCheckSeverity.Fail)
                    failures++;
            }

            return failures;
        }

        private static void CheckMemory(List<SystemCheckResult> results)
        {
            try
            {
                ulong total = PageAllocator.TotalPageCount;
                ulong free = PageAllocator.FreePageCount;
                ulong pageSize = PageAllocator.PageSize;

                if (total == 0 || pageSize == 0)
                {
                    AddFail(results, "memory", "Page allocator reports zero total pages or page size.");
                    return;
                }

                if (free > total)
                {
                    AddFail(results, "memory", "Free page count exceeds total page count.");
                    return;
                }

                AddPass(results, "memory",
                    "pages total=" + total + " free=" + free + " pageSize=" + pageSize);
            }
            catch (Exception ex)
            {
                AddFail(results, "memory", "Page allocator query failed: " + Safe(ex.Message));
            }
        }

        private static void CheckScheduler(List<SystemCheckResult> results)
        {
            try
            {
                uint cpus = SchedulerInfo.CpuCount;
                int slots = SchedulerInfo.ThreadSlotCount;
                string name = SchedulerInfo.SchedulerName;

                if (cpus == 0)
                {
                    AddFail(results, "scheduler", "Scheduler reports zero online CPUs.");
                    return;
                }

                if (slots < 0)
                {
                    AddFail(results, "scheduler", "Scheduler reports a negative thread-slot count.");
                    return;
                }

                AddPass(results, "scheduler",
                    Safe(name) + " cpus=" + cpus + " threadSlots=" + slots);
            }
            catch (Exception ex)
            {
                AddFail(results, "scheduler", "Scheduler query failed: " + Safe(ex.Message));
            }
        }

        private static void CheckStorage(List<SystemCheckResult> results)
        {
            try
            {
                int devices = StorageManager.DeviceCount;
                int partitions = StorageManager.Partitions == null
                    ? 0
                    : StorageManager.Partitions.Count;

                if (devices < 0 || partitions < 0)
                {
                    AddFail(results, "storage", "Storage manager returned negative inventory counts.");
                    return;
                }

                if (devices == 0)
                {
                    AddWarning(results, "storage", "No block devices detected.");
                    return;
                }

                AddPass(results, "storage",
                    "devices=" + devices + " partitions=" + partitions);
            }
            catch (Exception ex)
            {
                AddWarning(results, "storage", "Storage inventory unavailable: " + Safe(ex.Message));
            }
        }

        private static void CheckFilesystem(List<SystemCheckResult> results)
        {
            try
            {
                if (!Directory.Exists("/"))
                {
                    AddFail(results, "filesystem", "Root directory is not accessible.");
                    return;
                }

                bool etc = Directory.Exists("/etc");
                bool var = Directory.Exists("/var");

                if (!etc || !var)
                {
                    AddWarning(results, "filesystem",
                        "Root is accessible but expected system directories are missing: /etc=" +
                        YesNo(etc) + " /var=" + YesNo(var));
                    return;
                }

                AddPass(results, "filesystem", "root, /etc and /var are accessible");
            }
            catch (Exception ex)
            {
                AddFail(results, "filesystem", "Filesystem check failed: " + Safe(ex.Message));
            }
        }

        private static void CheckSecurityContext(List<SystemCheckResult> results)
        {
            try
            {
                if (!SecurityContext.IsAuthenticated)
                {
                    AddWarning(results, "security", "No authenticated user session is active.");
                    return;
                }

                if (SecurityContext.CurrentUid < 0 ||
                    string.IsNullOrWhiteSpace(SecurityContext.CurrentUser))
                {
                    AddFail(results, "security", "Authenticated session has invalid identity state.");
                    return;
                }

                AddPass(results, "security",
                    "authenticated uid=" + SecurityContext.CurrentUid);
            }
            catch (Exception ex)
            {
                AddFail(results, "security", "Security-context query failed: " + Safe(ex.Message));
            }
        }

        private static void CheckLogger(List<SystemCheckResult> results)
        {
            try
            {
                if (!File.Exists(SystemLogger.LogPath))
                {
                    AddWarning(results, "logger", "Persistent system log does not exist yet.");
                    return;
                }

                long length = new FileInfo(SystemLogger.LogPath).Length;
                if (length < 0)
                {
                    AddFail(results, "logger", "System log reports an invalid file length.");
                    return;
                }

                AddPass(results, "logger",
                    "persistent log available, bytes=" + length + " inMemory=" + SystemLogger.Count);
            }
            catch (Exception ex)
            {
                AddWarning(results, "logger", "Persistent logger status unavailable: " + Safe(ex.Message));
            }
        }

        private static void CheckProcesses(List<SystemCheckResult> results)
        {
            lock (ProcessScratchLock)
            {
                try
                {
                    int count = ProcessManager.FillActiveProcesses(ProcessScratch);
                    for (int i = 0; i < ProcessScratch.Count; i++)
                    {
                        KernelProcess process = ProcessScratch[i];
                        if (process == null || process.PID <= 0 || string.IsNullOrWhiteSpace(process.Name))
                        {
                            AddFail(results, "process-registry", "Invalid active process registry entry detected.");
                            ProcessScratch.Clear();
                            return;
                        }
                    }

                    ProcessScratch.Clear();
                    AddPass(results, "process-registry", "active entries=" + count);
                }
                catch (Exception ex)
                {
                    ProcessScratch.Clear();
                    AddFail(results, "process-registry", "Registry query failed: " + Safe(ex.Message));
                }
            }
        }

        private static void AddPass(List<SystemCheckResult> results, string name, string message) =>
            results.Add(new SystemCheckResult(name, SystemCheckSeverity.Pass, message));

        private static void AddWarning(List<SystemCheckResult> results, string name, string message) =>
            results.Add(new SystemCheckResult(name, SystemCheckSeverity.Warning, message));

        private static void AddFail(List<SystemCheckResult> results, string name, string message) =>
            results.Add(new SystemCheckResult(name, SystemCheckSeverity.Fail, message));

        private static string Safe(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "n/a";

            string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            return clean.Length <= 160 ? clean : clean.Substring(0, 160);
        }

        private static string YesNo(bool value) => value ? "yes" : "no";
    }
}
