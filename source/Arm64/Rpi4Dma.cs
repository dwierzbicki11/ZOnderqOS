using System;
using System.Runtime.InteropServices;

namespace ZonderqOS
{
    /// <summary>
    /// DMA arena for the Raspberry Pi 4 VL805.
    ///
    /// PFTF exposes XHC0 as non-coherent and limits PCIe DMA to the first 3 GiB.
    /// The arena is one large unmanaged allocation, page-aligned internally and
    /// verified to be physically contiguous before xHCI is allowed to use it.
    /// Cache maintenance is explicit through the small AArch64 native helper.
    /// </summary>
    internal sealed unsafe class Rpi4DmaArena
    {
        private const ulong PageSize = 4096UL;
        private const ulong DmaLimitExclusive = 0xC0000000UL;

        private readonly byte* _raw;
        private readonly byte* _base;
        private readonly nuint _size;
        private readonly ulong _basePhysical;
        private nuint _offset;

        private Rpi4DmaArena(byte* raw, byte* alignedBase, nuint size, ulong basePhysical)
        {
            _raw = raw;
            _base = alignedBase;
            _size = size;
            _basePhysical = basePhysical;
            _offset = 0;
        }

        public ulong BasePhysical => _basePhysical;
        public nuint Size => _size;

        public static bool TryCreate(nuint size, out Rpi4DmaArena arena, out string error)
        {
            arena = null!;
            error = string.Empty;

            if (size < 64 * 1024)
            {
                error = "DMA arena is too small";
                return false;
            }

            nuint total = size + (nuint)PageSize;
            byte* raw;
            try
            {
                raw = (byte*)NativeMemory.AllocZeroed(total, 1);
            }
            catch (OutOfMemoryException)
            {
                error = "NativeMemory allocation failed";
                return false;
            }

            if (raw == null)
            {
                error = "NativeMemory returned null";
                return false;
            }

            ulong alignedValue = AlignUp((ulong)raw, PageSize);
            byte* aligned = (byte*)alignedValue;

            ulong firstPhysical = Rpi4PageTableNative.VirtToPhys(alignedValue);
            ulong lastVirtual = alignedValue + (ulong)size - 1UL;
            ulong lastPhysical = Rpi4PageTableNative.VirtToPhys(lastVirtual);

            if (firstPhysical == 0 || lastPhysical == 0)
            {
                NativeMemory.Free(raw);
                error = "VA->PA translation failed for DMA arena";
                return false;
            }

            ulong expectedLast = firstPhysical + (ulong)size - 1UL;
            if (lastPhysical != expectedLast)
            {
                NativeMemory.Free(raw);
                error = "DMA arena is not physically contiguous";
                return false;
            }

            if (firstPhysical >= DmaLimitExclusive || expectedLast >= DmaLimitExclusive)
            {
                NativeMemory.Free(raw);
                error = "DMA arena is outside VL805's first-3GiB DMA window";
                return false;
            }

            arena = new Rpi4DmaArena(raw, aligned, size, firstPhysical);
            return true;
        }

        public void* Alloc(nuint size, nuint alignment)
        {
            if (size == 0)
                return null;

            if (alignment == 0 || ((alignment & (alignment - 1)) != 0))
                return null;

            ulong current = (ulong)_base + (ulong)_offset;
            ulong aligned = AlignUp(current, (ulong)alignment);
            nuint newOffset = (nuint)(aligned - (ulong)_base) + size;
            if (newOffset > _size)
                return null;

            _offset = newOffset;
            return (void*)aligned;
        }

        public ulong Physical(void* pointer)
        {
            if (pointer == null)
                return 0;

            ulong value = (ulong)pointer;
            ulong start = (ulong)_base;
            ulong end = start + (ulong)_size;
            if (value < start || value >= end)
                return 0;

            return _basePhysical + (value - start);
        }

        public bool Contains(void* pointer, nuint length)
        {
            if (pointer == null)
                return false;

            ulong start = (ulong)_base;
            ulong end = start + (ulong)_size;
            ulong p = (ulong)pointer;
            ulong pEnd = p + (ulong)length;
            return p >= start && pEnd >= p && pEnd <= end;
        }

        public static void Clean(void* pointer, nuint length)
        {
            if (pointer != null && length != 0)
                Rpi4DmaNative.Clean(pointer, (ulong)length);
        }

        public static void Invalidate(void* pointer, nuint length)
        {
            if (pointer != null && length != 0)
                Rpi4DmaNative.Invalidate(pointer, (ulong)length);
        }

        public static void CleanInvalidate(void* pointer, nuint length)
        {
            if (pointer != null && length != 0)
                Rpi4DmaNative.CleanInvalidate(pointer, (ulong)length);
        }

        public static void DelayMicroseconds(ulong microseconds)
        {
            Rpi4DmaNative.DelayMicroseconds(microseconds);
        }

        public static void DelayMilliseconds(uint milliseconds)
        {
            Rpi4DmaNative.DelayMicroseconds((ulong)milliseconds * 1000UL);
        }

        private static ulong AlignUp(ulong value, ulong alignment)
        {
            return (value + alignment - 1UL) & ~(alignment - 1UL);
        }
    }

    internal static partial class Rpi4DmaNative
    {
        [LibraryImport("*", EntryPoint = "_zonderq_arm64_dma_clean")]
        [SuppressGCTransition]
        internal static partial void Clean(void* address, ulong length);

        [LibraryImport("*", EntryPoint = "_zonderq_arm64_dma_invalidate")]
        [SuppressGCTransition]
        internal static partial void Invalidate(void* address, ulong length);

        [LibraryImport("*", EntryPoint = "_zonderq_arm64_dma_clean_invalidate")]
        [SuppressGCTransition]
        internal static partial void CleanInvalidate(void* address, ulong length);

        [LibraryImport("*", EntryPoint = "_zonderq_arm64_delay_us")]
        [SuppressGCTransition]
        internal static partial void DelayMicroseconds(ulong microseconds);
    }

    internal static partial class Rpi4PageTableNative
    {
        [LibraryImport("*", EntryPoint = "_native_arm64_va_to_pa")]
        [SuppressGCTransition]
        internal static partial ulong VirtToPhys(ulong virtualAddress);
    }
}
