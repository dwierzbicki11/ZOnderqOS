using System;
using System.Collections.Generic;
using System.Threading;
using ZonderqOS.SystemCore.Processes;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Compatibility view used by the existing shell and GUI. Entries created by
    /// Start are kernel tasks backed by managed threads and share the kernel address
    /// space. They are not isolated user processes.
    /// </summary>
    public class KernelProcess
    {
        private readonly ProcessControlBlock _controlBlock;

        public int PID => _controlBlock.PID;
        public string Name => _controlBlock.Name;
        public Thread ExecutionThread => _controlBlock.ExecutionThread;
        public CancellationTokenSource Cts => _controlBlock.Cancellation;
        public bool IsRunning => _controlBlock.IsRunning;
        public ProcessKind Kind => _controlBlock.Kind;
        public ProcessState State => _controlBlock.State;
        public bool HasIsolatedAddressSpace => _controlBlock.AddressSpace.IsIsolated;

        internal KernelProcess(ProcessControlBlock controlBlock)
        {
            _controlBlock = controlBlock ?? throw new ArgumentNullException(nameof(controlBlock));
        }
    }

    public static class ProcessManager
    {
        private static readonly ProcessRegistry _registry = new ProcessRegistry();

        /// <summary>
        /// Starts a kernel task in the shared kernel address space. This API is kept
        /// for compatibility; it does not create a protected Ring-3 process.
        /// </summary>
        public static int Start(string name, Action<CancellationToken> startMethod)
        {
            if (startMethod == null)
                throw new ArgumentNullException(nameof(startMethod));

            string processName = string.IsNullOrEmpty(name) ? "process" : name;
            int pid;
            CancellationTokenSource cts;
            Thread thread;
            ProcessControlBlock controlBlock = null;

            lock (_registry.SyncRoot)
            {
                pid = _registry.AllocatePidLocked();
                cts = new CancellationTokenSource();
                thread = new Thread(() =>
                {
                    bool faulted = false;
                    try
                    {
                        startMethod(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        faulted = true;
                        controlBlock.MarkFaulted();
                        WriteMessage.WriteError($"Proces {processName} (PID {pid}) zakończył się błędem: {ex.Message}", "PROC");
                    }
                    finally
                    {
                        if (!faulted)
                            controlBlock.MarkExited();
                        RemoveProcess(pid);
                    }
                });

                controlBlock = new ProcessControlBlock(pid, processName, thread, cts);
                _registry.Entries.Add(pid, controlBlock);
                controlBlock.MarkRunning();
                thread.Start();
            }

            return pid;
        }

        public static int Start(string name, Action startMethod)
        {
            if (startMethod == null)
                throw new ArgumentNullException(nameof(startMethod));

            return Start(name, _ => startMethod());
        }

        public static bool Kill(int pid)
        {
            lock (_registry.SyncRoot)
            {
                if (!_registry.Entries.TryGetValue(pid, out var process))
                    return false;

                if (!process.IsRunning)
                {
                    _registry.Entries.Remove(pid);
                    process.Cancellation.Dispose();
                    return false;
                }

                process.MarkStopRequested();
                process.Cancellation.Cancel();
                return true;
            }
        }

        public static bool IsRunning(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            lock (_registry.SyncRoot)
            {
                CleanupDeadProcessesLocked();
                foreach (var process in _registry.Entries.Values)
                {
                    if (process.IsRunning && string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
        }

        public static int FillActiveProcesses(List<KernelProcess> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (_registry.SyncRoot)
            {
                destination.Clear();
                CleanupDeadProcessesLocked();

                foreach (var process in _registry.Entries.Values)
                    destination.Add(new KernelProcess(process));

                return destination.Count;
            }
        }

        public static List<KernelProcess> GetActiveProcesses()
        {
            var activeList = new List<KernelProcess>();
            FillActiveProcesses(activeList);
            return activeList;
        }

        private static void RemoveProcess(int pid)
        {
            lock (_registry.SyncRoot)
            {
                if (_registry.Entries.TryGetValue(pid, out var process))
                {
                    _registry.Entries.Remove(pid);
                    process.Cancellation.Dispose();
                }
            }
        }

        private static void CleanupDeadProcessesLocked()
        {
            var deadPidScratch = _registry.DeadPidScratch;
            deadPidScratch.Clear();

            foreach (var kvp in _registry.Entries)
            {
                if (kvp.Value == null || !kvp.Value.IsRunning)
                    deadPidScratch.Add(kvp.Key);
            }

            for (int i = 0; i < deadPidScratch.Count; i++)
            {
                int pid = deadPidScratch[i];
                if (_registry.Entries.TryGetValue(pid, out var process))
                {
                    _registry.Entries.Remove(pid);
                    process.Cancellation.Dispose();
                }
            }

            deadPidScratch.Clear();
        }
    }
}
