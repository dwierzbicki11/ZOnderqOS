namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Architecture-neutral syscall dispatch scaffold. It intentionally performs no
    /// user-memory dereference until VMM supplies a concrete per-process user range.
    /// </summary>
    public static class SyscallDispatcher
    {
        public readonly struct Context
        {
            public int ProcessId { get; }
            public UserKernelAbi.UserAddressRange UserRange { get; }

            public Context(int processId, UserKernelAbi.UserAddressRange userRange)
            {
                ProcessId = processId;
                UserRange = userRange;
            }
        }

        public static long Dispatch(
            ulong number,
            ulong arg0,
            ulong arg1,
            ulong arg2,
            ulong arg3,
            ulong arg4,
            ulong arg5,
            Context context)
        {
            switch ((UserKernelAbi.Syscall)number)
            {
                case UserKernelAbi.Syscall.GetProcessId:
                    return context.ProcessId > 0
                        ? context.ProcessId
                        : (long)UserKernelAbi.Result.InvalidArgument;

                case UserKernelAbi.Syscall.Write:
                case UserKernelAbi.Syscall.Read:
                    // arg0 = fd/handle, arg1 = user buffer, arg2 = byte count.
                    // Validate the complete buffer before any backend is allowed to
                    // dereference it. The actual I/O backend remains gated on VMM.
                    if (!UserKernelAbi.TryValidateUserRange(arg1, arg2, context.UserRange, out _))
                        return (long)UserKernelAbi.Result.InvalidPointer;
                    return (long)UserKernelAbi.Result.NotSupported;

                case UserKernelAbi.Syscall.Open:
                    // arg0 = user pathname pointer, arg1 = pathname byte length.
                    if (!UserKernelAbi.TryValidateUserRange(arg0, arg1, context.UserRange, out _))
                        return (long)UserKernelAbi.Result.InvalidPointer;
                    return (long)UserKernelAbi.Result.NotSupported;

                case UserKernelAbi.Syscall.Exit:
                case UserKernelAbi.Syscall.Close:
                case UserKernelAbi.Syscall.MemoryMap:
                case UserKernelAbi.Syscall.MemoryUnmap:
                case UserKernelAbi.Syscall.Yield:
                    return (long)UserKernelAbi.Result.NotSupported;

                default:
                    return (long)UserKernelAbi.Result.NotSupported;
            }
        }
    }
}
