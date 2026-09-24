using System;
using System.Threading;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// Minimal D2.3 MMIO write boundary. Only aligned 32-bit writes are exposed;
    /// controller-specific code remains responsible for register semantics.
    /// </summary>
    public static unsafe class PhysicalMmioWriter
    {
        public static void Write32(ulong physicalAddress, uint value)
        {
            if ((physicalAddress & 3UL) != 0)
                throw new ArgumentOutOfRangeException(nameof(physicalAddress), "MMIO dword writes must be aligned.");
            if (physicalAddress > (ulong)nuint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(physicalAddress), "MMIO address is outside the native address width.");

            Volatile.Write(ref *(uint*)(nuint)physicalAddress, value);
        }
    }
}
