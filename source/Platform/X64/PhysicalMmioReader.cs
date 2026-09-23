using System;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// Minimal D2.2 read-only MMIO boundary. This type intentionally exposes no
    /// writes: reset, queue setup and DMA belong to later D2 gates.
    /// </summary>
    public static unsafe class PhysicalMmioReader
    {
        public static uint Read32(ulong physicalAddress)
        {
            if ((physicalAddress & 3UL) != 0)
                throw new ArgumentOutOfRangeException(nameof(physicalAddress), "MMIO dword reads must be aligned.");
            if (physicalAddress > (ulong)nuint.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(physicalAddress), "MMIO address is outside the native address width.");

            return *(volatile uint*)(nuint)physicalAddress;
        }

        public static ulong Read64(ulong physicalAddress)
        {
            if ((physicalAddress & 7UL) != 0)
                throw new ArgumentOutOfRangeException(nameof(physicalAddress), "MMIO qword reads must be aligned.");
            uint low = Read32(physicalAddress);
            uint high = Read32(checked(physicalAddress + 4UL));
            return ((ulong)high << 32) | low;
        }
    }
}
