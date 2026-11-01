using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
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
            .CreateDelegate(typeof(Func<IconType, int[], int, int, int, int, int[]>));
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
    private static readonly int[][] Sources = (int[][])Manager
        .GetField("sourcePixels", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] SourceWidths = (int[])Manager
        .GetField("sourceWidths", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] SourceHeights = (int[])Manager
        .GetField("sourceHeights", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly Action<Canvas, int[], int, int, int, int> DrawPoints =
        (Action<Canvas, int[], int, int, int, int>)Manager
            .GetMethod("BlitPoints", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int>));
    private static readonly FieldInfo Count = Manager
        .GetField("scaledCacheCount", BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo Eviction = Manager
        .GetField("nextScaledEvictionSlot", BindingFlags.Static | BindingFlags.NonPublic);

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

    // Faithful old single-recent-slot hit/miss lookup, kept outside the timed
    // loop so its method-call overhead is not unfairly omitted.
    private static int[] OldLookup(IconType type, int width, int height,
        int count, ref int recent)
    {
        if (recent >= 0 && recent < count && Types[recent] == type &&
            Widths[recent] == width && Heights[recent] == height)
            return Pixels[recent];
        recent = FindSlot(type, width, height, count);
        return recent < 0 ? null : Pixels[recent];
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
                pixels = OldLookup(type, width, height, count, ref oldRecent);
            }
            if (pixels == null || pixels.Length != width * height)
                throw new Exception("Alternating-size lookup returned invalid pixels");
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static double TimeDraw(bool optimized, IconType type, int[] source,
        Canvas frame, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int width = (i & 1) == 0 ? WidthA : WidthB;
            int height = (i & 1) == 0 ? HeightA : HeightB;
            if (optimized)
            {
                IconManager.DrawScaled(frame, type, -12, 9, width, height);
            }
            else
            {
                // Previous production hot path: always enter GetScaledPixels,
                // then choose the point blitter for this gradient icon.
                int[] pixels = GetScaled(type, source, 8, 8, width, height);
                if (pixels == null)
                    throw new Exception("Missing cached bitmap in draw baseline");
                DrawPoints(frame, pixels, width, height, -12, 9);
            }
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

        // Exercise the public production entry point, not just the private
        // lookup. A stale size hint could otherwise select the wrong run/point
        // blitter even when the cache returned the right bitmap.
        int[] oldSource = Sources[index];
        int oldWidth = SourceWidths[index];
        int oldHeight = SourceHeights[index];
        try
        {
            Sources[index] = source;
            SourceWidths[index] = 8;
            SourceHeights[index] = 8;
            for (int i = 0; i < 16; i++)
            {
                bool first = (i & 1) == 0;
                int width = first ? WidthA : WidthB;
                int height = first ? HeightA : HeightB;
                int[] scaled = first ? a : b;
                Canvas expected = new Canvas(128, 128);
                Canvas actual = new Canvas(128, 128);
                DrawPoints(expected, scaled, width, height, -12, 9);
                IconManager.DrawScaled(actual, type, -12, 9, width, height);
                for (int pixel = 0; pixel < expected.Pixels.Length; pixel++)
                    if (expected.Pixels[pixel] != actual.Pixels[pixel])
                        throw new Exception("Alternating icon changed framebuffer at " + pixel);
            }

            Canvas oldFrame = new Canvas(128, 128);
            Canvas newFrame = new Canvas(128, 128);
            TimeDraw(false, type, source, oldFrame, 20);
            TimeDraw(true, type, source, newFrame, 20);
            const int renders = 200;
            double[] beforeDraw = new double[5];
            double[] afterDraw = new double[5];
            for (int sample = 0; sample < 5; sample++)
            {
                if ((sample & 1) == 0)
                {
                    beforeDraw[sample] = TimeDraw(false, type, source, oldFrame, renders);
                    afterDraw[sample] = TimeDraw(true, type, source, newFrame, renders);
                }
                else
                {
                    afterDraw[sample] = TimeDraw(true, type, source, newFrame, renders);
                    beforeDraw[sample] = TimeDraw(false, type, source, oldFrame, renders);
                }
            }
            Array.Sort(beforeDraw);
            Array.Sort(afterDraw);
            Console.WriteLine("Host .NET alternating icon DrawScaled: " + renders +
                " rendered icons, median of 5 A/B; old scaler entry " +
                beforeDraw[2].ToString("F2") + " ms, hot-size draw " +
                afterDraw[2].ToString("F2") + " ms (not QEMU FPS).");
        }
        finally
        {
            Sources[index] = oldSource;
            SourceWidths[index] = oldWidth;
            SourceHeights[index] = oldHeight;
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

        Benchmark(type, source, count, "early cache slots");

        // Exercise the same workload with the two sizes at the END of a
        // saturated cache. Both positions occur with FIFO slot recycling.
        // Report both: a tiny early linear scan can beat the extra hint check,
        // while late-slot scans are the actual target of this optimization.
        int oldEviction = (int)Eviction.GetValue(null);
        try
        {
            Eviction.SetValue(null, 254);
            IconType lateType = IconType.FileDocx;
            int[] lateA = GetScaled(lateType, source, 8, 8, WidthA, HeightA);
            int[] lateB = GetScaled(lateType, source, 8, 8, WidthB, HeightB);
            if (lateA == null || lateB == null ||
                FindSlot(lateType, WidthA, HeightA, count) < 254 ||
                FindSlot(lateType, WidthB, HeightB, count) < 254)
                throw new Exception("Could not seed late-slot cache benchmark");
            Benchmark(lateType, source, count, "late cache slots");
        }
        finally
        {
            Eviction.SetValue(null, oldEviction);
        }
        Console.WriteLine("Two-size icon cache: 2000 identity checks, 16 exact framebuffers, stale-hint recovery, " +
            "2000 warmed calls with " + allocated + " B allocations.");
    }

    private static void Benchmark(IconType type, int[] source, int count, string label)
    {
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
        Console.WriteLine("Host .NET alternating icon sizes (" + label + "): " + repeats +
            " lookups, median of 5 alternating A/B; old single-slot " +
            before[2].ToString("F2") + " ms, production two-slot " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
