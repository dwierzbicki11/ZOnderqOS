using System;
using System.Runtime.CompilerServices;

namespace ZonderqOS.Hardware;

/// <summary>
/// Narrow DMA-page seam for controller queues.
///
/// Cosmos Gen 3 deliberately keeps its page allocator and device-specific DMA
/// helpers internal.  NativeAOT supports UnsafeAccessor/UnsafeAccessorType for
/// exactly this unsupported-internals case, so keep the dependency isolated in
/// one file instead of leaking Cosmos internals through the storage model.
/// </summary>
internal static unsafe class DmaPageAllocator
{
    public const ulong PageSize = 4096;

    public readonly struct Buffer
    {
        public Buffer(byte* virtualAddress, ulong physicalAddress, ulong pageCount)
        {
            VirtualAddress = virtualAddress;
            PhysicalAddress = physicalAddress;
            PageCount = pageCount;
        }

        public byte* VirtualAddress { get; }
        public ulong PhysicalAddress { get; }
        public ulong PageCount { get; }
    }

    public static Buffer AllocateZeroed(ulong pageCount)
    {
        if (pageCount == 0)
            throw new ArgumentOutOfRangeException(nameof(pageCount));

        ulong physicalAddress;
        byte* virtualAddress = CosmosXhciDmaAccess.AllocPages(null, pageCount, out physicalAddress);
        if (virtualAddress == null)
            throw new InvalidOperationException("Cosmos DMA allocator returned null");
        if (((ulong)virtualAddress & (PageSize - 1)) != 0)
            throw new InvalidOperationException("DMA virtual address is not page aligned");
        if ((physicalAddress & (PageSize - 1)) != 0)
            throw new InvalidOperationException("DMA physical address is not page aligned");

        return new Buffer(virtualAddress, physicalAddress, pageCount);
    }

    public static void Free(Buffer buffer)
    {
        if (buffer.VirtualAddress != null)
            CosmosXhciDmaAccess.Free(null, buffer.VirtualAddress);
    }

    public static void InitializeBarrier() => CosmosXhciDmaAccess.Initialize(null);

    public static void Barrier() => CosmosXhciDmaAccess.Barrier(null);

    private static class CosmosXhciDmaAccess
    {
        private const string XhciDmaType = "Cosmos.Kernel.HAL.Devices.Usb.Xhci.XhciDma, Cosmos.Kernel.HAL";

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "AllocPages")]
        internal static extern byte* AllocPages(
            [UnsafeAccessorType(XhciDmaType)] object? target,
            ulong pageCount,
            out ulong physicalAddress);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Free")]
        internal static extern void Free(
            [UnsafeAccessorType(XhciDmaType)] object? target,
            void* pages);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Initialize")]
        internal static extern void Initialize(
            [UnsafeAccessorType(XhciDmaType)] object? target);

        [UnsafeAccessor(UnsafeAccessorKind.StaticMethod, Name = "Barrier")]
        internal static extern void Barrier(
            [UnsafeAccessorType(XhciDmaType)] object? target);
    }
}
