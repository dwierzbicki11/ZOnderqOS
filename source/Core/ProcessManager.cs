using System;
using System.Collections.Generic;
using System.Threading;

namespace ZonderqOS.SystemCore
{
    public class KernelProcess
    {
        public int PID { get; }
        public string Name { get; }
        public Thread ExecutionThread { get; }
        public CancellationTokenSource Cts { get; }
        private uint kernelThreadId = uint.MaxValue;
        private int lastSignal;
        public uint KernelThreadId => Volatile.Read(ref kernelThreadId);
        public int LastSignal => Volatile.Read(ref lastSignal);
        internal void BindKernelThread() => Volatile.Write(ref kernelThreadId, SchedulerTelemetry.CurrentThreadId());
        internal void RecordSignal(ProcessSignal signal) => Volatile.Write(ref lastSignal, (int)signal);
        public bool IsRunning => ExecutionThread != null && ExecutionThread.IsAlive;

        public KernelProcess(int pid, string name, Thread thread, CancellationTokenSource cts)
        {
            PID = pid;
            Name = name;
            ExecutionThread = thread;
            Cts = cts;
        }
    }

    public static partial class ProcessManager
    {
        private static readonly Dictionary<int, KernelProcess> _processes = new Dictionary<int, KernelProcess>();
        private static readonly List<int> _deadPidScratch = new List<int>(16);
        private static int _nextPid = 1;
        private static readonly object _registryLock = new object();

        public static int Start(string name, Action<CancellationToken> startMethod)
        {
            if (startMethod == null)
                throw new ArgumentNullException(nameof(startMethod));

            string processName = string.IsNullOrEmpty(name) ? "process" : name;
            int pid;
            CancellationTokenSource cts;
            Thread thread;
            KernelProcess process = null;

            lock (_registryLock)
            {
                if (_nextPid <= 0)
                    _nextPid = 1;

                while (_processes.ContainsKey(_nextPid))
                {
                    _nextPid++;
                    if (_nextPid <= 0)
                        _nextPid = 1;
                }

                pid = _nextPid++;
                cts = new CancellationTokenSource();
                thread = new Thread(() =>
                {
                    try
                    {
                        process.BindKernelThread();
                        SchedulerTelemetry.EnableCurrentManagedPreemption();
                        startMethod(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        WriteMessage.WriteError($"Proces {processName} (PID {pid}) zakończył się błędem: {ex.Message}", "PROC");
                    }
                    finally
                    {
                        RemoveProcess(pid);
                    }
                });

                process = new KernelProcess(pid, processName, thread, cts);
                _processes.Add(pid, process);
            }

            // Never enter CoreLib's thread-start handshake while holding the
            // process registry lock. On SMP the new thread can run immediately
            // on another CPU and may need this lock during cleanup.
            try
            {
                thread.Start();
            }
            catch
            {
                lock (_registryLock)
                {
                    if (_processes.Remove(pid))
                        cts.Dispose();
                }
                throw;
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
            return SendSignal(pid, ProcessSignal.Terminate) == ProcessSignalResult.Sent;
        }

        public static ProcessSignalResult SendSignal(int pid, ProcessSignal signal)
        {
            CancellationTokenSource cts = null;
            KernelProcess process = null;

            lock (_registryLock)
            {
                if (!_processes.TryGetValue(pid, out process))
                    return ProcessSignalResult.NotFound;

                if (!process.IsRunning)
                {
                    _processes.Remove(pid);
                    process.Cts.Dispose();
                    return ProcessSignalResult.NotFound;
                }

                if (signal == ProcessSignal.Check)
                    return ProcessSignalResult.Exists;

                // Managed CoreLib currently has no safe asynchronous hard-abort
                // primitive. Do not pretend SIGKILL exists by mapping it to
                // cooperative cancellation.
                if (signal == ProcessSignal.Kill)
                    return ProcessSignalResult.Unsupported;

                if (signal != ProcessSignal.Terminate && signal != ProcessSignal.Interrupt)
                    return ProcessSignalResult.Unsupported;

                process.RecordSignal(signal);
                cts = process.Cts;
            }

            // Cancellation callbacks run synchronously and may query the process
            // registry, so never invoke them while _registryLock is held.
            try
            {
                cts.Cancel();
                return ProcessSignalResult.Sent;
            }
            catch (ObjectDisposedException)
            {
                return ProcessSignalResult.NotFound;
            }
        }

        public static bool IsRunning(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            lock (_registryLock)
            {
                CleanupDeadProcessesLocked();
                foreach (var process in _processes.Values)
                {
                    if (process.IsRunning && string.Equals(process.Name, name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Fills a caller-owned list with the active process registry without allocating
        /// a new snapshot list on every refresh. This is intended for GUI monitors that
        /// poll frequently. The destination list is cleared and reused.
        /// </summary>
        public static int FillActiveProcesses(List<KernelProcess> destination)
        {
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            lock (_registryLock)
            {
                destination.Clear();
                CleanupDeadProcessesLocked();

                foreach (var process in _processes.Values)
                    destination.Add(process);

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
            lock (_registryLock)
            {
                if (_processes.TryGetValue(pid, out var process))
                {
                    _processes.Remove(pid);
                    process.Cts.Dispose();
                }
            }
        }

        private static void CleanupDeadProcessesLocked()
        {
            _deadPidScratch.Clear();

            foreach (var kvp in _processes)
            {
                if (kvp.Value == null || !kvp.Value.IsRunning)
                    _deadPidScratch.Add(kvp.Key);
            }

            for (int i = 0; i < _deadPidScratch.Count; i++)
            {
                int pid = _deadPidScratch[i];
                if (_processes.TryGetValue(pid, out var process))
                {
                    _processes.Remove(pid);
                    process.Cts.Dispose();
                }
            }

            _deadPidScratch.Clear();
        }
    }
}
