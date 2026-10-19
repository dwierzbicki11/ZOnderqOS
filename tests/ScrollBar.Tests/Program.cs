using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

internal static class Program
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static Rectangle Thumb(ScrollBar bar)
    {
        var canvas = new Canvas();
        bar.Render(canvas);
        Require(canvas.Rectangles.Count == 2, "Expected track and thumb");
        return canvas.Rectangles[1];
    }

    private static int Main()
    {
        try
        {
            var bar = new ScrollBar(10, 20, 12, 200);
            bar.SetRange(int.MaxValue, int.MaxValue / 2);
            Require(Thumb(bar).Height == 99, "Large range produced wrong thumb height");

            bar.SetRange(int.MaxValue, 1);
            bar.Value = bar.MaxValue;
            Require(Thumb(bar).Y == 198, "Large value did not place thumb at bottom");

            bar.Value = 0;
            int notifications = 0;
            bar.ValueChanged = value => notifications++;
            Require(bar.HandleMouse(15, 21, true, false), "Thumb press not handled");
            Require(bar.HandleMouse(15, 199, true, true), "Thumb drag not handled");
            Require(bar.Value == bar.MaxValue, "Dragging to bottom overflowed");
            Require(notifications == 1, "Drag must notify exactly once");
            Require(bar.HandleMouse(15, 21, true, true), "Reverse drag not handled");
            Require(bar.Value == 0, "Reverse drag did not return to top");
            bar.HandleMouse(15, 21, false, true);

            bar.SetRange(100, 10);
            Require(bar.HandleMouse(15, 219, true, false), "Track click not handled");
            Require(bar.Value == 10, "Track click must advance one page");
            bar.Value = 90;
            bar.SetRange(5, 10);
            Require(bar.Value == 0, "Shrinking range must clamp value");
            Require(!bar.HandleMouse(15, 21, true, false), "Empty range consumed input");
            var canvas = new Canvas();
            bar.Render(canvas);
            Require(canvas.Rectangles.Count == 0, "Empty range rendered a thumb");

            Console.WriteLine("ScrollBar regression tests passed");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }
}
