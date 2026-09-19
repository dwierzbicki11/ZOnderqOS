using System;
using System.Collections.Generic;
using System.Threading;

namespace ZonderqOS.SystemCore.Processes
{
    /// <summary>
    /// Describes what kind of execution object a PID represents. KernelTask is
    /// deliberately not an isolated process: it shares the kernel address space.
    /// UserProcess is reserved for the later VMM/Ring-3 implementation.
    /// </summary>
    public enum ProcessKind
    {
        KernelTask = 0,
        UserProcess = 1
    }

    public enum ProcessState
    {
        Created = 0,
        Running = 1,
        StopRequested = 2,
        Exited = 3,
        Faulted = 4
    }

    public sealed class ProcessAddressSpace
    {
        public static readonly ProcessAddressSpace Kernel = new ProcessAddressSpace(0, true);

        public ulong RootPageTablePhysicalAddress { get; }
        public bool IsKernelShared { get; }
        public bool IsIsolated => !IsKernelShared && RootPageTablePhysicalAddress != 0;

        public ProcessAddressSpace(ulong rootPageTablePhysicalAddress, bool isKernelShared)
        {
            if (!isKernelShared && rootPageTablePhysicalAddress == 0)
                throw new ArgumentException("An isolated address space requires a page-table root.", nameof(rootPageTablePhysicalAddress));

            RootPageTablePhysicalAddress = rootPageTablePhysicalAddress;
            IsKernelShared = isKernelShared;
        }
    }

    /// <summary>
    /// Architecture-neutral process record. The current implementation can only
    /// create KernelTask records; UserProcess construction is intentionally gated
    /// until an architecture VMM supplies an isolated page-table root.
    /// </summary>
    public sealed class ProcessControlBlock
    {
        private ProcessState _state;

        public int PID { get; }
        public string Name { get; }
        public ProcessKind Kind { get; }
        public ProcessAddressSpace AddressSpace { get; }
        public Thread ExecutionThread { get; }
        public CancellationTokenSource Cancellation { get; }
        public ProcessState State => _state;
        public bool IsRunning => _state == ProcessState.Running && ExecutionThread != null && ExecutionThread.IsAlive;

        internal ProcessControlBlock(int pid, string name, Thread thread, CancellationTokenSource cancellation)
        {
            PID = pid;
            Name = name;
            Kind = ProcessKind.KernelTask;
            AddressSpace = ProcessAddressSpace.Kernel;
            ExecutionThread = thread;
            Cancellation = cancellation;
            _state = ProcessState.Created;
        }

        internal void MarkRunning() => _state = ProcessState.Running;
        internal void MarkStopRequested() => _state = ProcessState.StopRequested;
        internal void MarkExited() => _state = ProcessState.Exited;
        internal void MarkFaulted() => _state = ProcessState.Faulted;
    }

    /// <summary>
    /// Owns PID allocation independently from the execution backend. Keeping PID
    /// allocation here lets a future user-process loader share the same namespace
    /// without pretending System.Threading.Thread provides memory isolation.
    /// </summary>
    public sealed class ProcessRegistry
    {
        private readonly Dictionary<int, ProcessControlBlock> _processes = new Dictionary<int, ProcessControlBlock>();
        private readonly List<int> _deadPidScratch = new List<int>(16);
        private readonly object _sync = new object();
        private int _nextPid = 1;

        public object SyncRoot => _sync;
        public Dictionary<int, ProcessControlBlock> Entries => _processes;
        public List<int> DeadPidScratch => _deadPidScratch;

        public int AllocatePidLocked()
        {
            if (_nextPid <= 0)
                _nextPid = 1;

            while (_processes.ContainsKey(_nextPid))
            {
                _nextPid++;
                if (_nextPid <= 0)
                    _nextPid = 1;
            }

            return _nextPid++;
        }
    }
}
