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
        public bool IsRunning => ExecutionThread.IsAlive;

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
        private static readonly Dictionary<int, KernelProcess> _processes = new();
        private static int _nextPid = 1;
        private static readonly object _registryLock = new object();

        // Uruchamianie procesu z obsługą tokena anulowania
        public static int Start(string name, Action<CancellationToken> startMethod)
        {
            lock (_registryLock)
            {
                int pid = _nextPid++;
                var cts = new CancellationTokenSource();
                var thread = new Thread(() => startMethod(cts.Token));
                var process = new KernelProcess(pid, name, thread, cts);
                
                _processes.Add(pid, process);
                thread.Start();
                
                return pid;
            }
        }

        // Przeciążenie dla prostych metod bez tokena (wsteczna kompatybilność)
        public static int Start(string name, Action startMethod)
        {
            return Start(name, _ => startMethod());
        }

        // Bezpieczne zatrzymywanie procesu po PID (odpowiednik kill)
        public static bool Kill(int pid)
        {
            lock (_registryLock)
            {
                if (_processes.TryGetValue(pid, out var process))
                {
                    process.Cts.Cancel(); // Wysyła sygnał przerwania do wątku
                    return true;
                }
                return false;
            }
        }

        public static List<KernelProcess> GetActiveProcesses()
        {
            lock (_registryLock)
            {
                var deadPids = new List<int>();
                var activeList = new List<KernelProcess>();
                
                foreach (var kvp in _processes)
                {
                    if (kvp.Value.IsRunning)
                        activeList.Add(kvp.Value);
                    else
                        deadPids.Add(kvp.Key);
                }

                foreach(var pid in deadPids)
                    _processes.Remove(pid);

                return activeList;
            }
        }
    }
}