using System;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

// Executes the production DrawScaled entry point with injected in-memory
// source pixels. No filesystem, PNG decode, QEMU, or disk root is involved.
internal static class IconVisibilitySmoke
{
    private static readonly Type Manager = typeof(IconManager);
    private static readonly int[][] Sources = (int[][])Manager
        .GetField("sourcePixels", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly int[] SourceWidths = (int[])Manager
        .GetField("sourceWidths", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly int[] SourceHeights = (int[])Manager
        .GetField("sourceHeights", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly bool[] LoadAttempted = (bool[])Manager
        .GetField("sourceLoadAttempted", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly int[][] CachePixels = (int[][])Manager
        .GetField("scaledPixels", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
    private static readonly FieldInfo CacheCount = Manager
        .GetField("scaledCacheCount", BindingFlags.NonPublic | BindingFlags.Static);
    private static readonly Func<IconType, int[], int, int, int, int, int[]> Scale =
        (Func<IconType, int[], int, int, int, int, int[]>)Manager
            .GetMethod("GetScaledPixels", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Func<IconType, int[], int, int, int, int, int[]>));
    private static readonly Action<Canvas, int[], int, int, int, int> Blit =
        (Action<Canvas, int[], int, int, int, int>)Manager
            .GetMethod("BlitPoints", BindingFlags.NonPublic | BindingFlags.Static)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int>));

    private static void SamePixels(Canvas actual, Canvas expected, string label)
    {
        // The production renderer may coalesce repeated opaque pixels into
        // rectangles, so fewer Canvas calls are correct when pixels match.
        if (actual.DrawCalls > expected.DrawCalls)
            throw new Exception(label + ": Canvas call count increased");
        for (int i = 0; i < actual.Pixels.Length; i++)
            if (actual.Pixels[i] != expected.Pixels[i])
                throw new Exception(label + ": pixel mismatch " + i);
    }

    // Conservative pre-change baseline: it omits EnsureSource and type
    // canonicalization, but retains the scaled-cache lookup and clipped blit.
    // Therefore any measured advantage of the new early return is not caused
    // by making the old path perform extra PNG or filesystem operations.
    private static void Baseline(Canvas canvas, IconType type, int[] source,
        int x, int y, int width, int height)
    {
        int[] scaled = Scale(type, source, 8, 8, width, height);
        if (scaled != null)
            Blit(canvas, scaled, width, height, x, y);
    }

    private static double Time(bool optimized, Canvas canvas, IconType type,
        int[] source, int repeats)
    {
        long start = Stopwatch.GetTimestamp();
        for (int i = 0; i < repeats; i++)
        {
            if (optimized)
                IconManager.DrawScaled(canvas, type, 100, 20, 73, 37);
            else
                Baseline(canvas, type, source, 100, 20, 73, 37);
        }
        return (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
    }

    internal static void Verify()
    {
        Canvas frame = new Canvas(64, 64);
        // Unloaded, invisible icons must not trigger EnsureSource, which can
        // otherwise write the embedded PNG into /root/.zonderq-icons.
        int unloaded = (int)IconType.FileSvg;
        if (Sources[unloaded] != null || LoadAttempted[unloaded])
            throw new Exception("Visibility smoke expected an unloaded icon");
        int initialCount = (int)CacheCount.GetValue(null);
        int[][] originalSlots = (int[][])CachePixels.Clone();
        int[,] hidden = {
            { 64, 0, 40, 40 }, { 0, 64, 40, 40 },
            { -40, 0, 40, 40 }, { 0, -40, 40, 40 },
            { int.MaxValue, 1, 40, 40 }, { 1, int.MaxValue, 40, 40 },
            { int.MinValue, 1, 40, 40 }, { 1, int.MinValue, 40, 40 }
        };
        for (int i = 0; i < hidden.GetLength(0); i++)
            IconManager.DrawScaled(frame, IconType.FileSvg,
                hidden[i, 0], hidden[i, 1], hidden[i, 2], hidden[i, 3]);
        if (LoadAttempted[unloaded] || Sources[unloaded] != null)
            throw new Exception("Offscreen icon attempted PNG load");
        if ((int)CacheCount.GetValue(null) != initialCount)
            throw new Exception("Offscreen icon inserted cache entry");
        for (int i = 0; i < CachePixels.Length; i++)
            if (!object.ReferenceEquals(originalSlots[i], CachePixels[i]))
                throw new Exception("Offscreen icon replaced cache entry");
        if (frame.DrawCalls != 0)
            throw new Exception("Offscreen icon reached Canvas");

        // Partially visible icons must still render identically. Use a
        // gradient with opaque pixels so the point blitter is selected.
        IconType type = IconType.About;
        int index = (int)type;
        int[] source = new int[64];
        for (int i = 0; i < source.Length; i++)
            source[i] = unchecked((int)0xFF000000u) | (i * 1973);
        Sources[index] = source;
        SourceWidths[index] = 8;
        SourceHeights[index] = 8;
        int[] scaled = Scale(type, source, 8, 8, 73, 37);
        if (scaled == null)
            throw new Exception("Could not seed icon cache");
        int[,] visible = {
            { 0, 0 }, { -72, 0 }, { 63, 0 }, { 0, -36 },
            { 0, 63 }, { -30, -20 }, { 50, 50 }
        };
        for (int i = 0; i < visible.GetLength(0); i++)
        {
            Canvas expected = new Canvas(64, 64);
            Canvas actual = new Canvas(64, 64);
            int x = visible[i, 0], y = visible[i, 1];
            Blit(expected, scaled, 73, 37, x, y);
            IconManager.DrawScaled(actual, type, x, y, 73, 37);
            SamePixels(actual, expected, "partially visible icon " + i);
        }

        Canvas oldFrame = new Canvas(64, 64);
        Canvas newFrame = new Canvas(64, 64);
        Time(false, oldFrame, type, source, 1000);
        Time(true, newFrame, type, source, 1000);
        const int repeats = 100000;
        double[] before = new double[5];
        double[] after = new double[5];
        for (int sample = 0; sample < 5; sample++)
        {
            if ((sample & 1) == 0)
            {
                before[sample] = Time(false, oldFrame, type, source, repeats);
                after[sample] = Time(true, newFrame, type, source, repeats);
            }
            else
            {
                after[sample] = Time(true, newFrame, type, source, repeats);
                before[sample] = Time(false, oldFrame, type, source, repeats);
            }
        }
        Array.Sort(before);
        Array.Sort(after);
        if (oldFrame.DrawCalls != 0 || newFrame.DrawCalls != 0)
            throw new Exception("Offscreen benchmark touched framebuffer");
        Console.WriteLine("Icon visibility: 8 offscreen culls, 7 partial-frame equivalence checks; no PNG load/cache writes.");
        Console.WriteLine("Host .NET offscreen icon: " + repeats +
            " calls, median of 5 alternating A/B; pre-change cache+clip " +
            before[2].ToString("F2") + " ms, early cull " +
            after[2].ToString("F2") + " ms (not QEMU FPS).");
    }
}
