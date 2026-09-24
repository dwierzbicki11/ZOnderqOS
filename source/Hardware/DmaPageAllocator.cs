using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace ZonderqOS.Hardware;

/// <summary>
/// Narrow DMA-page seam for controller queues.
///
/// Cosmos Gen 3 deliberately keeps its page allocator and device-specific DMA
/// helpers internal. NativeAOT supports UnsafeAccessor/UnsafeAccessorType for
/// this unsupported-internals case, so keep the dependency isolated here.
/// The XhciDma type is named only by string in UnsafeAccessorType; therefore it
/// must also be explicitly rooted for NativeAOT so its EEType is emitted.
/// </summary>
internal static unsafe class DmaPageAllocator
{
    public const ulong PageSize = 4096;
    private const string XhciDmaTypeName = "Cosmos.Kernel.HAL.Devices.Usb.Xhci.XhciDma";
    private const string XhciDmaAssemblyName = "Cosmos.Kernel.HAL";

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

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, XhciDmaTypeName, XhciDmaAssemblyName)]
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

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, XhciDmaTypeName, XhciDmaAssemblyName)]
    public static void Free(Buffer buffer)
    {
        if (buffer.VirtualAddress != null)
            CosmosXhciDmaAccess.Free(null, buffer.VirtualAddress);
    }

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, XhciDmaTypeName, XhciDmaAssemblyName)]
    public static void InitializeBarrier() => CosmosXhciDmaAccess.Initialize(null);

    [DynamicDependency(DynamicallyAccessedMemberTypes.All, XhciDmaTypeName, XhciDmaAssemblyName)]
    public static void Barrier() => CosmosXhciDmaAccess.Barrier(null);

    private static class CosmosXhciDmaAccess
    {
        private const string XhciDmaType = XhciDmaTypeName + ", " + XhciDmaAssemblyName;

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
