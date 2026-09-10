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
        public bool IsRunning => ExecutionThread != null && ExecutionThread.IsAlive;

        public KernelProcess(int pid, string name, Thread thread, CancellationTokenSource cts)
        {
            PID = pid;
            Name = name;
            ExecutionThread = thread;
            Cts = cts;
        }
    }

    public static class ProcessManager
    {
        private static readonly Dictionary<int, KernelProcess> _processes = new Dictionary<int, KernelProcess>();
        private static int _nextPid = 1;
        private static readonly object _registryLock = new object();

        public static int Start(string name, Action<CancellationToken> startMethod)
        {
            if (startMethod == null)
            {
                throw new ArgumentNullException(nameof(startMethod));
            }

            lock (_registryLock)
            {
                int pid = _nextPid++;
                var cts = new CancellationTokenSource();
                var thread = new Thread(() =>
                {
                    try
                    {
                        startMethod(cts.Token);
                    }
                    catch (Exception ex)
                    {
                        WriteMessage.WriteError($"Proces {name} (PID {pid}) zakończył się błędem: {ex.Message}", "PROC");
                    }
                });

                var process = new KernelProcess(pid, name ?? "process", thread, cts);
                _processes.Add(pid, process);
                thread.Start();
                return pid;
            }
        }

        public static int Start(string name, Action startMethod)
        {
            if (startMethod == null)
            {
                throw new ArgumentNullException(nameof(startMethod));
            }

            return Start(name, _ => startMethod());
        }

        public static bool Kill(int pid)
        {
            lock (_registryLock)
            {
                if (!_processes.TryGetValue(pid, out var process))
                {
                    return false;
                }

                if (!process.IsRunning)
                {
                    _processes.Remove(pid);
                    process.Cts.Dispose();
                    return false;
                }

                process.Cts.Cancel();
                return true;
            }
        }

        public static List<KernelProcess> GetActiveProcesses()
        {
            lock (_registryLock)
            {
                var activeList = new List<KernelProcess>();
                var deadPids = new List<int>();

                foreach (var kvp in _processes)
                {
                    if (kvp.Value.IsRunning)
                    {
                        activeList.Add(kvp.Value);
                    }
                    else
                    {
                        deadPids.Add(kvp.Key);
                    }
                }

                foreach (int pid in deadPids)
                {
                    if (_processes.TryGetValue(pid, out var process))
                    {
                        _processes.Remove(pid);
                        process.Cts.Dispose();
                    }
                }

                return activeList;
            }
        }
    }
}