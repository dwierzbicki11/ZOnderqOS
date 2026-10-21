using System.Drawing;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// UI-only animation budget: no dedicated threads, timers, sleeps, textures,
    /// allocations or spin loops. The desktop requests a few extra render frames
    /// only after real interaction. The normal idle heartbeat still applies.
    /// </summary>
    internal static class UiAnimator
    {
        private static int framesLeft;
        internal static bool NeedsRedraw => framesLeft > 0;

        internal static void RequestFrames(int frames = 9)
        {
            if (frames > framesLeft)
                framesLeft = frames > 12 ? 12 : frames;
        }

        internal static void FrameRendered()
        {
            if (framesLeft > 0)
                framesLeft--;
        }

        internal static float Approach(float current, float target, float amount)
        {
            if (current < target)
                return current + amount > target ? target : current + amount;
            return current - amount < target ? target : current - amount;
        }

        internal static Color Blend(Color a, Color b, float amount)
        {
            if (amount <= 0f) return a;
            if (amount >= 1f) return b;
            int r = a.R + (int)((b.R - a.R) * amount);
            int g = a.G + (int)((b.G - a.G) * amount);
            int blue = a.B + (int)((b.B - a.B) * amount);
            return Color.FromArgb(r, g, blue);
        }
    }
}
