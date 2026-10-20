using System;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Cosmos 3.0.94 display mode negotiation.
    /// Prefer the highest 32-bit mode explicitly advertised by the display
    /// driver. For firmware-only displays (no mode switching), preserve
    /// the framebuffer mode supplied by UEFI/Limine. Never fake canvas sizes.
    /// </summary>
    internal static class DisplayModeSelector
    {
        // 4K limit prevents malformed/overly large modes from exhausting
        // memory on Broadwell, while still allowing common laptop and 4K panels.
        private const long MaxPixels = 3840L * 2160L;

        internal static Canvas OpenNativeOrBest()
        {
            Canvas canvas = Canvas.GetFullScreen();
            Mode current = canvas.Mode;
            Mode chosen = SelectBest(current, canvas.AvailableModes);

            if (chosen != current)
            {
                try
                {
                    canvas = Canvas.GetFullScreen(chosen);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // A driver may advertise a mode but refuse a switch:
                    // retain its actual physical framebuffer dimensions.
                    canvas = Canvas.GetFullScreen();
                }
                catch (InvalidOperationException)
                {
                    canvas = Canvas.GetFullScreen();
                }
            }

            Console.WriteLine("[DISPLAY3094] " + canvas.Name +
                " framebuffer=" + canvas.Width + "x" + canvas.Height +
                " refresh=" + canvas.RefreshRate +
                "Hz; preferred=" + chosen.Width + "x" + chosen.Height);
            if (canvas.Width != chosen.Width || canvas.Height != chosen.Height)
                Console.WriteLine("[DISPLAY3094] Physical display/firmware mode retained; unsupported mode was not forced.");
            return canvas;
        }

        internal static Mode SelectBest(Mode current, System.Collections.Generic.IReadOnlyList<Mode> advertised)
        {
            Mode best = current;
            long bestPixels = (long)current.Width * current.Height;
            if (advertised == null)
                return best;

            for (int i = 0; i < advertised.Count; i++)
            {
                Mode mode = advertised[i];
                long pixels = (long)mode.Width * mode.Height;
                if ((int)mode.ColorDepth != 32 || mode.Width < 640 ||
                    mode.Height < 480 || pixels > MaxPixels || pixels <= bestPixels)
                    continue;
                bestPixels = pixels;
                best = mode;
            }

            return best;
        }
    }
}
