using System;

namespace ZonderqOS.SystemCore.Processes
{
    [Flags]
    public enum VirtualMemoryPermissions
    {
        None = 0,
        Read = 1 << 0,
        Write = 1 << 1,
        Execute = 1 << 2,
        User = 1 << 3
    }

    public readonly struct VirtualMemoryMapping
    {
        public ulong VirtualAddress { get; }
        public ulong PhysicalAddress { get; }
        public ulong Length { get; }
        public VirtualMemoryPermissions Permissions { get; }

        public VirtualMemoryMapping(ulong virtualAddress, ulong physicalAddress, ulong length, VirtualMemoryPermissions permissions)
        {
            VirtualAddress = virtualAddress;
            PhysicalAddress = physicalAddress;
            Length = length;
            Permissions = permissions;
        }
    }

    /// <summary>
    /// Architecture-neutral virtual-memory contract. Implementations own page-table
    /// details (CR3/PML4 on x86_64, translation-table registers on ARM64); callers do
    /// not receive an architecture register as the address-space API.
    /// </summary>
    public interface IVirtualMemoryManager
    {
        ulong PageSize { get; }
        bool IsPageAligned(ulong address);
        bool IsValidUserRange(ulong address, ulong length);
        bool TryMap(ProcessAddressSpace addressSpace, VirtualMemoryMapping mapping);
        bool TryUnmap(ProcessAddressSpace addressSpace, ulong virtualAddress, ulong length);
        bool TryProtect(ProcessAddressSpace addressSpace, ulong virtualAddress, ulong length, VirtualMemoryPermissions permissions);
        bool TryQuery(ProcessAddressSpace addressSpace, ulong virtualAddress, out VirtualMemoryMapping mapping);
    }

    public static class VirtualAddressRange
    {
        // Canonical lower-half user range for the current x86_64 design. An
        // architecture backend may impose a stricter limit, but must never widen it
        // without changing the process ABI deliberately.
        public const ulong UserStart = 0x0000000000010000UL;
        public const ulong UserEndExclusive = 0x0000800000000000UL;

        public static bool IsPageAligned(ulong address, ulong pageSize)
        {
            return pageSize != 0 && (pageSize & (pageSize - 1)) == 0 && (address & (pageSize - 1)) == 0;
        }

        public static bool IsUserRange(ulong address, ulong length)
        {
            if (length == 0 || address < UserStart || address >= UserEndExclusive)
                return false;

            ulong last = address + length - 1;
            if (last < address)
                return false;

            return last < UserEndExclusive;
        }
    }
}
