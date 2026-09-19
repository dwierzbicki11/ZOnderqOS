using System;

namespace ZonderqOS.SystemCore.Processes
{
    /// <summary>
    /// Pure x86_64 page-table encoding/validation used by the hardware backend.
    /// This type deliberately does not claim to install mappings or touch CR3;
    /// those operations remain gated on a real physical page allocator and QEMU proof.
    /// </summary>
    internal static class X64PageTableModel
    {
        internal const ulong PageSize = 4096UL;
        internal const ulong AddressMask = 0x000FFFFFFFFFF000UL;
        internal const ulong Present = 1UL << 0;
        internal const ulong Writable = 1UL << 1;
        internal const ulong User = 1UL << 2;
        internal const ulong NoExecute = 1UL << 63;

        internal static bool TryCreateLeafEntry(ulong physicalAddress, VirtualMemoryPermissions permissions, out ulong entry)
        {
            entry = 0;
            if (!VirtualAddressRange.IsPageAligned(physicalAddress, PageSize))
                return false;
            if ((physicalAddress & ~AddressMask) != 0)
                return false;
            if ((permissions & VirtualMemoryPermissions.Read) == 0)
                return false;

            ulong value = (physicalAddress & AddressMask) | Present;
            if ((permissions & VirtualMemoryPermissions.Write) != 0)
                value |= Writable;
            if ((permissions & VirtualMemoryPermissions.User) != 0)
                value |= User;
            if ((permissions & VirtualMemoryPermissions.Execute) == 0)
                value |= NoExecute;

            entry = value;
            return true;
        }

        internal static ulong PhysicalAddress(ulong entry) => entry & AddressMask;
        internal static bool IsPresent(ulong entry) => (entry & Present) != 0;
        internal static bool IsWritable(ulong entry) => (entry & Writable) != 0;
        internal static bool IsUser(ulong entry) => (entry & User) != 0;
        internal static bool IsExecutable(ulong entry) => (entry & NoExecute) == 0;

        internal static int Pml4Index(ulong virtualAddress) => (int)((virtualAddress >> 39) & 0x1FFUL);
        internal static int PdptIndex(ulong virtualAddress) => (int)((virtualAddress >> 30) & 0x1FFUL);
        internal static int PdIndex(ulong virtualAddress) => (int)((virtualAddress >> 21) & 0x1FFUL);
        internal static int PtIndex(ulong virtualAddress) => (int)((virtualAddress >> 12) & 0x1FFUL);

        internal static bool IsCanonical(ulong virtualAddress)
        {
            ulong upper = virtualAddress >> 48;
            bool sign = ((virtualAddress >> 47) & 1UL) != 0;
            return sign ? upper == 0xFFFFUL : upper == 0;
        }
    }
}
