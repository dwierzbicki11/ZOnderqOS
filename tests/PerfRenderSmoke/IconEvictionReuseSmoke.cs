using System;
using System.Reflection;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

// Host-only production cache eviction test. A forced wrong-length slot is the
// old allocation path; an equal-length slot is the reusable path. No disk I/O.
internal static class IconEvictionReuseSmoke
{
    private static readonly Type Manager = typeof(IconManager);
    private static readonly Func<IconType, int[], int, int, int, int, int[]> GetScaled =
        (Func<IconType, int[], int, int, int, int, int[]>)Manager
            .GetMethod("GetScaledPixels", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Func<IconType, int[], int, int, int, int, int[]>));
    private static readonly Action<Canvas, int[], int, int, int, int> DrawPoints =
        (Action<Canvas, int[], int, int, int, int>)Manager
            .GetMethod("BlitPoints", BindingFlags.Static | BindingFlags.NonPublic)
            .CreateDelegate(typeof(Action<Canvas, int[], int, int, int, int>));

    private static T Field<T>(string name) =>
        (T)Manager.GetField(name, BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);

    private static int[] LegacyScale(int[] source, int width, int height)
    {
        int[] result = new int[width * height];
        int xRatio = ((8 << 16) / width) + 1;
        int yRatio = ((8 << 16) / height) + 1;
        for (int y = 0; y < height; y++)
        {
            int sy = Math.Min(7, (y * yRatio) >> 16);
            for (int x = 0; x < width; x++)
                result[y * width + x] = source[sy * 8 + Math.Min(7, (x * xRatio) >> 16)];
        }
        return result;
    }

    internal static void Verify()
    {
        const int width = 64;
        const int height = 100;
        const int area = width * height;
        const int samples = 32;
        const IconType type = IconType.FilePdf;
        IconType[] types = Field<IconType[]>("scaledTypes");
        int[] widths = Field<int[]>("scaledWidths");
        int[] heights = Field<int[]>("scaledHeights");
        int[][] pixels = Field<int[][]>("scaledPixels");
        bool[] runs = Field<bool[]>("scaledUseRuns");
        int[] recent = Field<int[]>("scaledRecentSlots");
        int[] previous = Field<int[]>("scaledPreviousSlots");
        int[][] sources = Field<int[][]>("sourcePixels");
        int[] sourceWidths = Field<int[]>("sourceWidths");
        int[] sourceHeights = Field<int[]>("sourceHeights");
        FieldInfo countField = Manager.GetField("scaledCacheCount", BindingFlags.Static | BindingFlags.NonPublic);
        FieldInfo evictionField = Manager.GetField("nextScaledEvictionSlot", BindingFlags.Static | BindingFlags.NonPublic);

        int count = (int)countField.GetValue(null);
        if (count != 256)
            throw new Exception("Eviction reuse test requires a full 256-slot cache");
        for (int i = 0; i < count; i++)
            if (types[i] == type && widths[i] == width && heights[i] == height)
                throw new Exception("Eviction reuse test key unexpectedly exists");

        int slot = (int)evictionField.GetValue(null);
        int index = (int)type;
        IconType savedType = types[slot];
        int savedWidth = widths[slot];
        int savedHeight = heights[slot];
        int[] savedPixels = pixels[slot];
        bool savedRuns = runs[slot];
        int savedRecent = recent[index];
        int savedPrevious = previous[index];
        int[] savedSource = sources[index];
        int savedSourceWidth = sourceWidths[index];
        int savedSourceHeight = sourceHeights[index];

        int[] source = new int[64];
        for (int i = 0; i < source.Length; i++)
            source[i] = i % 7 == 0 ? 0 : unchecked((int)0xFF123456u) + i * 17;
        int[] expected = LegacyScale(source, width, height);
        int[] reusable = new int[area];
        int[] wrongLength = new int[area + 1];
        long allocatedBaseline = 0;
        long allocatedReuse = 0;
        try
        {
            // Warm the production scaler and JIT before measuring allocations.
            for (int sample = -2; sample < samples; sample++)
            {
                bool reuse = (sample & 1) != 0;
                evictionField.SetValue(null, slot);
                types[slot] = IconType.File;
                // Previous cached geometry is 80x80, whereas the new
                // request is 64x100: equal area, different row stride.
                widths[slot] = 80;
                heights[slot] = 80;
                pixels[slot] = reuse ? reusable : wrongLength;

                long before = GC.GetAllocatedBytesForCurrentThread();
                int[] actual = GetScaled(type, source, 8, 8, width, height);
                long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
                if (actual == null || actual.Length != area)
                    throw new Exception("Cache eviction returned wrong pixel buffer");
                if (reuse && !object.ReferenceEquals(actual, reusable))
                    throw new Exception("Equal-area cache slot was not reused");
                if (!reuse && object.ReferenceEquals(actual, wrongLength))
                    throw new Exception("Wrong-size cache slot was reused");
                for (int pixel = 0; pixel < area; pixel++)
                    if (actual[pixel] != expected[pixel])
                        throw new Exception("Reused scaled pixels changed at " + pixel);

                if (sample >= 0)
                {
                    if (reuse) allocatedReuse += bytes;
                    else allocatedBaseline += bytes;
                }
            }

            // Exercise the public draw path: the new key must select the
            // freshly classified run/point renderer, not an evicted hint.
            sources[index] = source;
            sourceWidths[index] = 8;
            sourceHeights[index] = 8;
            Canvas oldFrame = new Canvas(90, 120);
            Canvas newFrame = new Canvas(90, 120);
            DrawPoints(oldFrame, expected, width, height, 7, 9);
            IconManager.DrawScaled(newFrame, type, 7, 9, width, height);
            for (int i = 0; i < oldFrame.Pixels.Length; i++)
                if (oldFrame.Pixels[i] != newFrame.Pixels[i])
                    throw new Exception("Eviction reuse changed framebuffer at " + i);
        }
        finally
        {
            types[slot] = savedType;
            widths[slot] = savedWidth;
            heights[slot] = savedHeight;
            pixels[slot] = savedPixels;
            runs[slot] = savedRuns;
            recent[index] = savedRecent;
            previous[index] = savedPrevious;
            sources[index] = savedSource;
            sourceWidths[index] = savedSourceWidth;
            sourceHeights[index] = savedSourceHeight;
            evictionField.SetValue(null, slot);
        }

        if (allocatedReuse != 0 || allocatedBaseline < 16L * area * sizeof(int))
            throw new Exception("Cache-full allocation A/B failed: forced new=" +
                allocatedBaseline + " B, reuse=" + allocatedReuse + " B");
        Console.WriteLine("Host .NET cache-full equal-area eviction: 16 forced-new misses " +
            allocatedBaseline + " B vs 16 reused-buffer misses " + allocatedReuse +
            " B; exact pixels and public framebuffer PASS (not QEMU).");
    }
}
