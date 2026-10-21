using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Early graphical boot screen for the normal Cosmos 3.0.94 desktop image.
    /// It draws a procedural Z mark: no filesystem, PNG decoder, icons or
    /// background animation thread is required while boot services initialize.
    /// The boot pipeline advances the animation at safe checkpoints.
    /// </summary>
    internal static class BootSplash
    {
        private static readonly Color Background = Color.FromArgb(9, 15, 24);
        private static readonly Color Panel = Color.FromArgb(17, 29, 43);
        private static readonly Color Border = Color.FromArgb(46, 70, 91);
        private static readonly Color Accent = Color.FromArgb(61, 162, 227);
        private static readonly Color AccentBright = Color.FromArgb(129, 214, 255);
        private static readonly Color Track = Color.FromArgb(36, 52, 69);
        private static readonly Color Text = Color.FromArgb(226, 238, 247);
        private static readonly Color Muted = Color.FromArgb(115, 143, 165);

        private static readonly int[] OrbitX =
            { 0, 50, 87, 100, 87, 50, 0, -50, -87, -100, -87, -50 };
        private static readonly int[] OrbitY =
            { -100, -87, -50, 0, 50, 87, 100, 87, 50, 0, -50, -87 };

        private static Canvas canvas;
        private static int frame;
        private static int progress;
        private static string currentStep = "URUCHAMIANIE SYSTEMU";

        internal static bool IsActive { get; private set; }

        internal static void Start()
        {
            if (IsActive)
                return;

            try
            {
                // Same physical-mode negotiation as login and the real desktop.
                canvas = DisplayModeSelector.OpenNativeOrBest();
                if (canvas.Width < 480 || canvas.Height < 360)
                {
                    Abort();
                    return;
                }

                progress = 0;
                frame = 0;
                IsActive = true;

                // Four short reveal frames; later frames follow actual boot stages.
                // No fake multi-second delay or concurrently writing render thread.
                for (int i = 0; i < 4 && IsActive; i++)
                {
                    Render();
                    if (i < 3)
                        Thread.Sleep(20);
                }
            }
            catch
            {
                // Video is optional. A broken framebuffer must not break boot.
                Abort();
            }
        }

        internal static void Advance(int percent, string step)
        {
            if (!IsActive)
                return;

            try
            {
                progress = Math.Max(0, Math.Min(100, percent));
                if (!string.IsNullOrEmpty(step))
                    currentStep = step;
                Render();
            }
            catch
            {
                Abort();
            }
        }

        internal static void Complete(bool handOffToGui)
        {
            if (!IsActive)
                return;

            Advance(100, "GOTOWE");
            // Keep the last rendered frame until the login/setup GUI repaints.
            // Disabling graphics here would expose the boot chatter for a frame.
            IsActive = false;
            canvas = null;
            if (!handOffToGui)
            {
                try { Canvas.DisableFullScreen(); }
                catch { }
                Console.Clear();
            }
        }

        internal static void Abort()
        {
            bool hadCanvas = canvas != null;
            IsActive = false;
            canvas = null;
            if (hadCanvas)
            {
                try { Canvas.DisableFullScreen(); }
                catch { }
            }
        }

        private static void Render()
        {
            if (canvas == null)
                return;

            int width = canvas.Width;
            int height = canvas.Height;
            int centerX = width / 2;
            int centerY = height / 2 - 38;
            int symbolSize = Math.Min(108, Math.Min(width / 5, height / 4));
            int left = centerX - symbolSize / 2;
            int top = centerY - symbolSize / 2;
            int stroke = Math.Max(9, symbolSize / 9);
            int orbitRadius = symbolSize / 2 + 37;

            canvas.Clear(Background);

            int panelSize = symbolSize + 45;
            canvas.DrawFilledRectangle(Panel, centerX - panelSize / 2,
                centerY - panelSize / 2, panelSize, panelSize);
            canvas.DrawRectangle(Border, centerX - panelSize / 2,
                centerY - panelSize / 2, panelSize, panelSize);

            // Twelve orbit points with a moving highlight animate the logo.
            int phase = frame % OrbitX.Length;
            for (int i = 0; i < OrbitX.Length; i++)
            {
                int distance = (phase - i + OrbitX.Length) % OrbitX.Length;
                Color dot = distance == 0 ? AccentBright : distance <= 2 ? Accent : Border;
                int dotSize = distance == 0 ? 7 : 4;
                int dotX = centerX + OrbitX[i] * orbitRadius / 100;
                int dotY = centerY + OrbitY[i] * orbitRadius / 100;
                canvas.DrawFilledRectangle(dot, dotX - dotSize / 2,
                    dotY - dotSize / 2, dotSize, dotSize);
            }

            Color glyph = (frame % 4 == 0) ? AccentBright : Accent;
            canvas.DrawFilledRectangle(glyph, left, top, symbolSize, stroke);
            for (int i = 0; i < stroke; i++)
            {
                canvas.DrawLine(glyph,
                    left + symbolSize - stroke + i, top + stroke,
                    left + i, top + symbolSize - stroke);
            }
            canvas.DrawFilledRectangle(glyph, left, top + symbolSize - stroke,
                symbolSize, stroke);

            string name = "ZONDERQ OS";
            canvas.DrawString(name, PCScreenFont.DefaultFont, Text,
                centerX - name.Length * 4, centerY + orbitRadius + 27);

            int barWidth = Math.Min(300, width - 72);
            int barX = centerX - barWidth / 2;
            int barY = centerY + orbitRadius + 64;
            canvas.DrawFilledRectangle(Track, barX, barY, barWidth, 5);
            if (progress > 0)
                canvas.DrawFilledRectangle(Accent, barX, barY,
                    Math.Max(1, barWidth * progress / 100), 5);

            canvas.DrawString(currentStep, PCScreenFont.DefaultFont, Muted,
                centerX - currentStep.Length * 4, barY + 24);
            canvas.DrawString("COSMOS GEN 3  /  3.0.94", PCScreenFont.DefaultFont,
                Muted, centerX - 104, height - 36);

            canvas.Display();
            frame++;
        }
    }
}
