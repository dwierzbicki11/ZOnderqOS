using System;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI.Icons;

// Runs the actual production icon-cache lookup with in-memory 8x8 pixels.
// A/B baseline implements the previous single-recent-slot lookup (including
// its full linear scan on alternating sizes). No filesystem or QEMU involved.
internal static class IconTwoSizeCacheSmoke
{
    private static readonly Type Manager = typeof(IconManager);
    private static readonly Func<IconType, int[], int, int, int, int, int[]> GetScaled =
        (Func<IconType, int[], int, int, int, int, int[]>)Manager
            .GetMethod("GetScaledPixels", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<IconType, int[], int, int, int, int, int, int[]>));
    private static readonly IconType[] Types = (IconType[])Manager
        .GetField("scaledTypes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Widths = (int[])Manager
        .GetField("scaledWidths", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Heights = (int[])Manager
        .GetField("scaledHeights", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[][] Pixels = (int[][])Manager
        .GetField("scaledPixels", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Recent = (int[])Manager
        .GetField("scaledRecentSlots", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Previous = (int[])Manager
        .GetField("scaledPreviousSlots", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly FieldInfo Count = Manager
        .GetField("scaledCacheCount", BindingFlags.Static | BindingFlags.NonPublic);

    private const int WidthA = 79;
    private const int HeightA = 73;
    private const int WidthB = 83;
    private const int HeightB = 77;

    private static int FindSlot(IconType type, int width, int height, int count)
    {
        for (int i = 0; i < count; i++)
            if (Types[i] == type && Widths[i] == width && Heights[i] == height)
                return i;
        return -1;
    }

    private static double Time(bool optimized, IconType type, int[] source,
        int count, int repeats)
    {
        // The old implementation had only one per-type recent slot. Its
        // second size always missed and fell through to the full cache scan.
        int oldRecent = -1;
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int width = (i & 1) == 0 ? WidthA : WidthB;
            int height = (i & 1) == 0 ? HeightA : HeightB;
            int[] pixels;
            if (optimized)
            {
                pixels = GetScaled(type, source, 8, 8, width, height);
            }
            else
            {
                if (oldRecent < 0 || Types[oldRecent] != type ||
                    Widths[oldRecent] != width || Heights[oldRecent] != height)
                    oldRecent = FindSlot(type, width, height, count);
                pixels = oldRecent < 0 ? null : Pixels[oldRecent];
            }
            if (pixels == null || pixels.Length != width * height)
                throw new Exception("Alternating-size lookup returned invalid pixels");
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int count = (int)Count.GetValue(null);
        if (count != 256)
            throw new Exception("Two-size cache smoke requires the full 256-slot cache");

        IconType type = IconType.FilePdf;
        int index = (int)type;
        int[] source = new int[64];
        for (int i = 0; i < source.Length; i++)
            source[i] = unchecked((int)0xFF000000u) | (i * 3197);

        int[] a = GetScaled(type, source, 8, 8, WidthA, HeightA);
        int[] b = GetScaled(type, source, 8, 8, WidthB, HeightB);
        if (a == null || b == null || object.ReferenceEquals(a, b) ||
            (int)Count.GetValue(null) != 256)
            throw new Exception("Two-size cache initialization failed");

        for (int i = 0; i < 2000; i++)
        {
            int[] expected = (i & 1) == 0 ? a : b;
            int[] actual = (i & 1) == 0
                ? GetScaled(type, source, 8, 8, WidthA, HeightA)
                : GetScaled(type, source, 8, 8, WidthB, HeightB);
            if (!object.ReferenceEquals(expected, actual))
                throw new Exception("Two-size cache lost buffer identity");
        }

        // Warmed hits must not allocate new scaled bitmaps or per-call objects.
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 2000; i++)
        {
            if ((i & 1) == 0)
                GetScaled(type, source, 8, 8, WidthA, HeightA);
            else
                GetScaled(type, source, 8, 8, WidthB, HeightB);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        if (allocated != 0)
            throw new Exception("Warmed two-size cache allocated " + allocated + " B");

        // Both hints can point to evicted entries. Validate their full keys
        // before accepting them, then recover via the exhaustive scan.
        int wrong = -1;
        for (int i = 0; i < count; i++)
            if (Types[i] != type) { wrong = i; break; }
        if (wrong < 0)
            throw new Exception("Missing unrelated cache entry");
        Recent[index] = wrong;
        Previous[index] = wrong;
        if (!object.ReferenceEquals(a, GetScaled(type, source, 8, 8, WidthA, HeightA)) ||
            !object.ReferenceEquals(b, GetScaled(type, source, 8, 8, WidthB, HeightB)))
            throw new Exception("Stale two-size hints returned the wrong bitmap");

        Time(false, type, source, count, 1000);
        Time(true, type, source, count, 1000);
        const int repeats = 100000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, type, source, count, repeats);
                after[sample] = Time(true, type, source, count, repeats);
            }
            else
            {
                after[sample] = Time(true, type, source, count, repeats);
                before[sample] = Time(false, type, source, count, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Console.WriteLine("Two-size icon cache: 2000 identity checks, stale-hint recovery, " +
            "2000 warmed calls with " + allocated + " B allocations.");
        Console.WriteLine("Host .NET alternating icon sizes: " + repeats +
            " lookups, median of 5 alternating A/B; old single-slot " +
            before[2].ToString("F2") + " ms, production two-slot " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
