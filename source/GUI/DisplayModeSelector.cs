using System;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Cosmos Gen3 stock 3.0.95 display mode negotiation.
    /// Prefer the highest 32-bit mode explicitly advertised by the display
    /// driver. For firmware-only displays (no mode switching), preserve
    /// the framebuffer mode supplied by UEFI/Limine. Never fake canvas sizes.
    /// </summary>
    internal static class DisplayModeSelector
    {
        // 4K limit prevents malformed/overly large modes from exhausting
        // memory on Broadwell, while still allowing common laptop and 4K panels.
        private const int MaxWidth = 3840;
        private const int MaxHeight = 2160;
        private const long MaxPixels = (long)MaxWidth * MaxHeight;

        internal static Canvas OpenNativeOrBest()
        {
            Canvas canvas = Canvas.GetFullScreen();
#if ZONDERQ_GUI_TEST_NATIVE_FRAMEBUFFER
            // Opt-in CI profile: keep the *physical* firmware mode requested
            // by the disposable test ISO. Never fake Width/Height or resize
            // the backing buffer. Production builds still negotiate best mode.
            Console.WriteLine("[DISPLAY-TEST] Native framebuffer=" +
                canvas.Width + "x" + canvas.Height);
            return canvas;
#endif
            Mode current = canvas.Mode;
            Mode chosen = current;
            try
            {
                chosen = SelectBest(current, canvas.AvailableModes);
            }
            catch (NotSupportedException)
            {
                // Firmware-only displays may not expose a mode list at all.
                // Preserve the real framebuffer instead of aborting the GUI.
            }
            catch (InvalidOperationException)
            {
                // A display driver may be initialized but unable to enumerate.
            }

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
                catch (NotSupportedException)
                {
                    // A mode may be advertised but not switchable by the driver.
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
            // A firmware framebuffer may be 16/24-bit or outside our safe
            // geometry limits. Do not let its pixel count outrank every
            // valid 32-bit mode exposed by the driver.
            long currentPixels = (long)current.Width * current.Height;
            bool currentUsable = (int)current.ColorDepth == 32 &&
                current.Width >= 640 && current.Height >= 480 &&
                current.Width <= MaxWidth && current.Height <= MaxHeight &&
                currentPixels <= MaxPixels;
            long bestPixels = currentUsable ? currentPixels : -1;
            if (advertised == null)
                return best;

            for (int i = 0; i < advertised.Count; i++)
            {
                Mode mode = advertised[i];
                long pixels = (long)mode.Width * mode.Height;
                // Pixel count alone is insufficient: 8000x1000 fits an 8M
                // pixel budget but exceeds the advertised 4K axis limits.
                if ((int)mode.ColorDepth != 32 || mode.Width < 640 ||
                    mode.Height < 480 || mode.Width > MaxWidth ||
                    mode.Height > MaxHeight || pixels > MaxPixels || pixels <= bestPixels)
                    continue;
                bestPixels = pixels;
                best = mode;
            }

            return best;
        }
    }
}
