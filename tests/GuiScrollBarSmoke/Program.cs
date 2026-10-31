using System;
using System.Collections.Generic;
using System.Drawing;
using ZonderqOS.GUI;

// Only Canvas is stubbed: the production Widget and ScrollBar are compiled.
namespace Cosmos.Kernel.System.Graphics
{
    public abstract class Canvas
    {
        public abstract void DrawFilledRectangle(Color color, int x, int y, int width, int height);
    }
}

internal sealed class RecordingCanvas : Cosmos.Kernel.System.Graphics.Canvas
{
    public readonly List<(int X, int Y, int Width, int Height)> Rectangles = new();
    public override void DrawFilledRectangle(Color color, int x, int y, int width, int height)
        => Rectangles.Add((x, y, width, height));
}

internal static class Program
{
    private static int assertions;
    private static void Check(bool ok, string message)
    {
        assertions++;
        if (!ok) throw new InvalidOperationException("ScrollBar regression: " + message);
    }

    private static (int X, int Y, int Width, int Height) Thumb(ScrollBar bar)
    {
        var canvas = new RecordingCanvas();
        bar.Render(canvas);
        Check(canvas.Rectangles.Count == 2, "track and thumb rendered");
        return canvas.Rectangles[1];
    }

    public static int Main()
    {
        var bar = new ScrollBar(10, 20, 6, 200);
        bar.SetRange(2_000_000_000, 1_000_000_000);
        Check(bar.MaxValue == 1_000_000_000, "large range");
        Check(Thumb(bar).Height == 100, "thumb proportional with large counts");
        bar.Value = 500_000_000;
        Check(Thumb(bar).Y == 70, "thumb midpoint without integer overflow");

        bar.Value = 0;
        int changes = 0;
        bar.ValueChanged = _ => changes++;
        Check(bar.HandleMouse(12, 21, true, false), "press thumb");
        Check(bar.HandleMouse(12, 500, true, true), "drag outside track");
        Check(bar.Value == bar.MaxValue && changes == 1, "drag reaches exact maximum");
        bar.HandleMouse(12, 500, false, true);
        Check(!bar.HandleMouse(12, 20, false, false), "release cancels drag");

        bar.Value = 0;
        Check(bar.HandleMouse(12, 21, true, false), "second drag");
        bar.SetRange(500, 100);
        Check(!bar.HandleMouse(12, 180, true, true), "range change cancels drag");
        Check(bar.Value == 0, "stale drag does not change value");

        var huge = new ScrollBar(0, 0, 6, 10_000);
        huge.SetRange(int.MaxValue, 1_000_000_000);
        huge.Value = huge.MaxValue - 1;
        var largeThumb = Thumb(huge);
        Check(largeThumb.Y + largeThumb.Height < 10_000, "bottom pixel is a page-down target");
        Check(huge.HandleMouse(2, 9999, true, false), "page down click");
        Check(huge.Value == huge.MaxValue, "page down saturates without overflow");

        foreach (int height in new[] { 1, 2, 10, 21, 22, 80 })
        foreach (int width in new[] { 1, 2, 3, 6 })
        {
            var tiny = new ScrollBar(4, 7, width, height);
            tiny.SetRange(1000, 1);
            var thumb = Thumb(tiny);
            Check(thumb.Height >= 1 && thumb.Y >= 7 &&
                thumb.Y + thumb.Height <= 7 + height, "thumb within short track");
            Check(thumb.Width >= 1 && thumb.X >= 4 &&
                thumb.X + thumb.Width <= 4 + width, "thumb within narrow track");
        }

        var zero = new ScrollBar(4, 7, 0, 0);
        zero.SetRange(100, 1);
        var empty = new RecordingCanvas();
        zero.Render(empty);
        Check(empty.Rectangles.Count == 0, "zero-size track is not drawn");
        Check(!zero.HandleMouse(4, 7, true, false), "zero-size track ignores input");

        var range = new ScrollBar(0, 0, 6, 100);
        range.SetRange(100, 10);
        range.Value = 80;
        int clamped = -1;
        range.ValueChanged = v => clamped = v;
        range.SetRange(4, 10);
        Check(range.Value == 0 && range.MaxValue == 0, "range shrink clamps value");
        Check(clamped == 0, "range shrink notifies listener");
        range.SetRange(-20, 0);
        Check(range.MaxValue == 0, "negative count normalizes");

        Console.WriteLine("PASS: " + assertions + " production ScrollBar C# assertions");
        return 0;
    }
}
