using System;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Architecture-neutral contract for the future user/kernel boundary.
    /// This file deliberately does not claim that Ring 3 is active yet: the
    /// x86_64 entry/exit path depends on per-process address spaces from VMM.
    /// </summary>
    public static class UserKernelAbi
    {
        public const uint AbiVersion = 1;

        // Keep syscall numbers stable once user binaries start depending on them.
        public enum Syscall : ulong
        {
            Exit = 0,
            Write = 1,
            Read = 2,
            Open = 3,
            Close = 4,
            MemoryMap = 5,
            MemoryUnmap = 6,
            GetProcessId = 7,
            Yield = 8
        }

        public enum Result : long
        {
            Success = 0,
            InvalidArgument = -1,
            InvalidPointer = -2,
            NotSupported = -3,
            NotFound = -4,
            AccessDenied = -5,
            Fault = -6
        }

        // x86_64 canonical-address split. Until VMM supplies a concrete layout,
        // no address is accepted as a user pointer. This fail-closed behaviour is
        // intentional and prevents scaffolding from bypassing memory isolation.
        public const ulong X64LowerCanonicalMax = 0x00007FFFFFFFFFFFUL;

        public readonly struct UserAddressRange
        {
            public ulong Start { get; }
            public ulong EndExclusive { get; }

            public UserAddressRange(ulong start, ulong endExclusive)
            {
                Start = start;
                EndExclusive = endExclusive;
            }

            public bool IsConfigured => EndExclusive > Start;
        }

        public static bool IsCanonicalLowerHalf(ulong address)
        {
            return address <= X64LowerCanonicalMax;
        }

        public static bool TryValidateUserRange(
            ulong pointer,
            ulong length,
            UserAddressRange processUserRange,
            out ulong endExclusive)
        {
            endExclusive = 0;

            if (!processUserRange.IsConfigured || length == 0)
                return false;

            if (!IsCanonicalLowerHalf(pointer) || pointer < processUserRange.Start)
                return false;

            // Addition must not wrap. End is exclusive.
            ulong end = pointer + length;
            if (end < pointer)
                return false;

            if (end > processUserRange.EndExclusive)
                return false;

            // A configured user range itself must remain in the lower canonical half.
            if (processUserRange.EndExclusive - 1 > X64LowerCanonicalMax)
                return false;

            endExclusive = end;
            return true;
        }
    }
}
