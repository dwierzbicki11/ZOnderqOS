using System;
using System.Runtime.InteropServices;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Bounds-checked large scratch buffer backed by NativeMemory.
    /// Buffers >= 4096 bytes map to Cosmos LargeHeap/MediumHeap pages and return
    /// 100% of their physical PageAllocator pages immediately upon Dispose().
    /// </summary>
    internal unsafe struct NativeScratchBuffer<T> where T : unmanaged
    {
        public const int MinUnmanagedBytes = 4096;

        private T* ptr;
        private int length;
        private T[] fallbackManaged;

        public int Length => ptr != null ? length : (fallbackManaged != null ? fallbackManaged.Length : 0);
        public bool IsAllocated => ptr != null || fallbackManaged != null;
        public T* Pointer => ptr;

        public void Allocate(int elementCount)
        {
            Dispose();
            if (elementCount <= 0)
                return;

            long totalBytes = (long)elementCount * sizeof(T);
            if (totalBytes >= MinUnmanagedBytes && totalBytes <= int.MaxValue - 64)
            {
                ptr = (T*)NativeMemory.AllocZeroed((nuint)elementCount, (nuint)sizeof(T));
                if (ptr == null)
                {
                    fallbackManaged = new T[elementCount];
                }
                length = elementCount;
            }
            else
            {
                fallbackManaged = new T[elementCount];
                length = elementCount;
            }
        }

        public void Clear()
        {
            if (ptr != null && length > 0)
            {
                for (int i = 0; i < length; i++)
                    ptr[i] = default(T);
            }
            else if (fallbackManaged != null)
            {
                Array.Clear(fallbackManaged, 0, fallbackManaged.Length);
            }
        }

        public Span<T> AsSpan()
        {
            if (ptr != null)
                return new Span<T>(ptr, length);
            if (fallbackManaged != null)
                return fallbackManaged.AsSpan();
            return Span<T>.Empty;
        }

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)length)
                    return default(T);
                return ptr != null ? ptr[index] : fallbackManaged[index];
            }
            set
            {
                if ((uint)index >= (uint)length)
                    return;
                if (ptr != null)
                    ptr[index] = value;
                else if (fallbackManaged != null)
                    fallbackManaged[index] = value;
            }
        }

        public void Dispose()
        {
            T* toFree = ptr;
            ptr = null;
            length = 0;
            fallbackManaged = null;
            if (toFree != null)
                NativeMemory.Free(toFree);
        }
    }
}
