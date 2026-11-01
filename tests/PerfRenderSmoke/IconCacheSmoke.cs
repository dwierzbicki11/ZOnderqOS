using System;
using System.Diagnostics;
using System.Reflection;
using ZonderqOS.GUI.Icons;
using Cosmos.Kernel.System.Graphics;

// Exercises the production icon scaler/cache with synthetic pixels. Does not
// read or write disk, initialize GUI, or claim QEMU rendering performance.
internal static class IconCacheSmoke
{
    private static readonly Func<IconType, int[], int, int, int, int, int[]> GetScaled =
        (Func<IconType, int[], int, int, int, int, int[]>)typeof(IconManager)
            .GetMethod("GetScaledPixels", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<IconType, int[], int, int, int, int, int[]>));

    private static readonly IconType[] Types = (IconType[])typeof(IconManager)
        .GetField("scaledTypes", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Widths = (int[])typeof(IconManager)
        .GetField("scaledWidths", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[] Heights = (int[])typeof(IconManager)
        .GetField("scaledHeights", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly int[][] Pixels = (int[][])typeof(IconManager)
        .GetField("scaledPixels", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
    private static readonly FieldInfo Count = typeof(IconManager)
        .GetField("scaledCacheCount", BindingFlags.Static | BindingFlags.NonPublic);

    private static readonly Action<Canvas, int[], int, int, int, int> DrawCachedRuns =
        (Action<Canvas, int[], int, int, int, int>)typeof(IconManager)
            .GetMethod("BlitRuns", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int>));
    private static readonly Action<Canvas, int[], int, int, int, int, int, int> DrawDirect =
        (Action<Canvas, int[], int, int, int, int, int, int>)typeof(IconManager)
            .GetMethod("BlitScaledDirect", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int, int, int>));

    private static double TimeCacheFullDraw(bool cached, Canvas canvas, IconType type,
        int[] source, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (cached)
            {
                int[] pixels = GetScaled(type, source, 8, 8, 80, 80);
                DrawCachedRuns(canvas, pixels, 80, 80, 0, 0);
            }
            else
            {
                // Conservative old baseline: excludes the old 256-slot miss scan.
                DrawDirect(canvas, source, 8, 8, 0, 0, 80, 80);
            }
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    private static int[] BaselineLookup(IconType type, int width, int height)
    {
        int count = (int)Count.GetValue(null);
        for (int i = 0; i < count; i++)
            if (Types[i] == type && Widths[i] == width && Heights[i] == height)
                return Pixels[i];
        return null;
    }

    // Benchmark baseline must NOT use reflection inside the timed loop.
    // Capture the production cache count once, then compare the original
    // linear search to the new recent-slot hit under identical conditions.
    private static int[] BaselineLookupFast(IconType type, int width, int height, int count)
    {
        for (int i = 0; i < count; i++)
            if (Types[i] == type && Widths[i] == width && Heights[i] == height)
                return Pixels[i];
        return null;
    }

    private static double Time(bool recent, IconType type, int[] source, int width, int height, int repeats)
    {
        int count = (int)Count.GetValue(null);
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            int[] result = recent
                ? GetScaled(type, source, 8, 8, width, height)
                : BaselineLookupFast(type, width, height, count);
            if (result == null) throw new Exception("Missing cached icon");
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        int[] source = new int[64];
        for (int i = 0; i < source.Length; i++)
            source[i] = unchecked((int)0xFF000000u) | (i * 997);

        // Populate enough sizes/types to expose the linear-scan bottleneck.
        for (int i = 0; i < 120; i++)
        {
            IconType type = (IconType)(i % 40);
            int width = 16 + (i / 40) * 4;
            int height = 18 + (i / 40) * 4;
            int[] scaled = GetScaled(type, source, 8, 8, width, height);
            if (scaled == null || scaled.Length != width * height)
                throw new Exception("Icon scaler returned invalid pixels");
            if (!object.ReferenceEquals(scaled, BaselineLookup(type, width, height)))
                throw new Exception("Icon cache mismatch after insertion");
        }

        for (int i = 0; i < 120; i++)
        {
            IconType type = (IconType)(i % 40);
            int width = 16 + (i / 40) * 4;
            int height = 18 + (i / 40) * 4;
            int[] cached = BaselineLookup(type, width, height);
            if (!object.ReferenceEquals(cached, GetScaled(type, source, 8, 8, width, height)))
                throw new Exception("Icon cache returned a different buffer");
        }
        Console.WriteLine("Icon size cache: 120 insertion + 120 hit identity checks");

        // Warm the per-type recent slot for a late entry, then compare the
        // original 120-slot search to the production recent-slot fast path.
        IconType selected = (IconType)(119 % 40);
        const int selectedWidth = 24;
        const int selectedHeight = 26;
        GetScaled(selected, source, 8, 8, selectedWidth, selectedHeight);
        Time(false, selected, source, selectedWidth, selectedHeight, 1000);
        Time(true, selected, source, selectedWidth, selectedHeight, 1000);
        const int iterations = 100000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, selected, source, selectedWidth, selectedHeight, iterations);
                after[sample] = Time(true, selected, source, selectedWidth, selectedHeight, iterations);
            }
            else
            {
                after[sample] = Time(true, selected, source, selectedWidth, selectedHeight, iterations);
                before[sample] = Time(false, selected, source, selectedWidth, selectedHeight, iterations);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        Console.WriteLine("Host .NET icon cache: " + iterations +
            " lookups, median of 5 alternating A/B; linear " +
            before[2].ToString("F2") + " ms, recent-slot " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
        // Fill the 256-slot cache without touching any filesystem. A new
        // ordinary icon size must replace one slot, not become a permanent
        // per-frame direct-scaling miss. Stale recent-slot pointers must be
        // checked against the replacement key before returning pixels.
        int fill = 0;
        while ((int)Count.GetValue(null) < 256)
        {
            GetScaled((IconType)0, source, 8, 8, 30 + fill, 35 + fill);
            if (++fill > 256)
                throw new Exception("Could not fill icon cache");
        }
        int[] solid = new int[64];
        for (int i = 0; i < solid.Length; i++)
            solid[i] = unchecked((int)0xFF336699u);
        IconType overflowType = IconType.FileTxt;
        int[] overflow = GetScaled(overflowType, solid, 8, 8, 80, 80);
        if (overflow == null || overflow.Length != 6400 ||
            (int)Count.GetValue(null) != 256 ||
            !object.ReferenceEquals(overflow, BaselineLookup(overflowType, 80, 80)))
            throw new Exception("Cache-full replacement did not retain new size");
        for (int i = 0; i < 100; i++)
            if (!object.ReferenceEquals(overflow, GetScaled(overflowType, solid, 8, 8, 80, 80)))
                throw new Exception("Cache-full replacement missed subsequent hit");
        if (GetScaled((IconType)45, solid, 8, 8, 256, 256) != null)
            throw new Exception("Large transient icon unexpectedly replaced a cache slot");
        int[] original = GetScaled((IconType)0, source, 8, 8, 16, 18);
        if (original == null || !object.ReferenceEquals(original, BaselineLookup((IconType)0, 16, 18)))
            throw new Exception("Stale recent-slot pointer returned incorrect icon");
        Console.WriteLine("Icon cache full: 256 bounded entries, replacement/hit/large-size/stale-slot checks PASS.");

        Canvas oldFrame = new Canvas(100, 100);
        Canvas newFrame = new Canvas(100, 100);
        DrawDirect(oldFrame, solid, 8, 8, 0, 0, 80, 80);
        DrawCachedRuns(newFrame, overflow, 80, 80, 0, 0);
        for (int i = 0; i < oldFrame.Pixels.Length; i++)
            if (oldFrame.Pixels[i] != newFrame.Pixels[i])
                throw new Exception("Cache-full icon frame changed at pixel " + i);
        TimeCacheFullDraw(false, oldFrame, overflowType, solid, 20);
        TimeCacheFullDraw(true, newFrame, overflowType, solid, 20);
        double[] oldTimes = new double[5];
        double[] newTimes = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                oldTimes[sample] = TimeCacheFullDraw(false, oldFrame, overflowType, solid, 200);
                newTimes[sample] = TimeCacheFullDraw(true, newFrame, overflowType, solid, 200);
            }
            else
            {
                newTimes[sample] = TimeCacheFullDraw(true, newFrame, overflowType, solid, 200);
                oldTimes[sample] = TimeCacheFullDraw(false, oldFrame, overflowType, solid, 200);
            }
        }
        Array.Sort(oldTimes);
        Array.Sort(newTimes);
        Console.WriteLine("Host .NET cache-full solid icon: 200 blits, median of 5 A/B; " +
            "old direct fallback " + oldTimes[2].ToString("F2") + " ms, " +
            "bounded cached runs " + newTimes[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
