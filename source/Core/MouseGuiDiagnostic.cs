#if ZONDERQ_READONLY_HARDWARE_PROBE && ZONDERQ_COSMOS_INPUT_API
using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// On-screen USB pointer proof for Cosmos 3.0.94. No filesystems,
    /// settings, authentication, user database or disk writes are involved.
    /// It deliberately does NOT initialize the normal desktop.
    /// </summary>
    internal static class MouseGuiDiagnostic
    {
        internal static void Run()
        {
            Canvas canvas = Canvas.GetFullScreen();
            int width = canvas.Width;
            int height = canvas.Height;
            if (width < 320 || height < 200)
            {
                Console.WriteLine("[MOUSE-GUI] No usable framebuffer mode.");
                return;
            }

            MouseManager.SetScreenSize(width, height);
            int lastX = -1;
            int lastY = -1;
            bool lastLeft = false;
            bool lastRight = false;
            bool lastMiddle = false;
            int moves = 0;
            int presses = 0;
            int ticksWithoutRedraw = 0;
            bool exit = false;
            bool first = true;

            Console.Write("[MOUSE-GUI] START Cosmos 3.0.94 framebuffer cursor test; no disk writes.\n");
            while (!exit)
            {
                while (KeyboardManager.TryReadKey(out KeyEvent key))
                {
                    if (key != null && key.Key == Key.Escape)
                    {
                        exit = true;
                        break;
                    }
                }
                if (exit)
                    break;

                int x = MouseManager.X;
                int y = MouseManager.Y;
                bool left = MouseManager.LeftButton;
                bool right = MouseManager.RightButton;
                bool middle = MouseManager.MiddleButton;
                bool moved = x != lastX || y != lastY;
                bool changedButtons = left != lastLeft || right != lastRight || middle != lastMiddle;
                if (moved && !first)
                    moves++;
                if (changedButtons && !first)
                    presses++;

                // Refresh the framebuffer only on input changes and at 1 Hz.
                // This is important for older Broadwell/iGPU laptops.
                if (first || moved || changedButtons || ticksWithoutRedraw >= 25)
                {
                    Render(canvas, width, height, x, y, left, right, middle, moves, presses);
                    ticksWithoutRedraw = 0;
                }
                else
                {
                    ticksWithoutRedraw++;
                }

                if (moved && moves == 1)
                    Console.Write("[MOUSE-GUI] FIRST MOVEMENT OBSERVED\n");
                if (changedButtons && presses == 1)
                    Console.Write("[MOUSE-GUI] FIRST BUTTON EVENT OBSERVED\n");

                first = false;
                lastX = x;
                lastY = y;
                lastLeft = left;
                lastRight = right;
                lastMiddle = middle;
                Thread.Sleep(40);
            }

            Console.Write("[MOUSE-GUI] EXIT moves=");
            Console.Write((uint)moves);
            Console.Write(" button-events=");
            Console.Write((uint)presses);
            Console.Write("\n");

            // Cosmos 3.0.94 has no guaranteed text-mode restoration after a
            // framebuffer hand-off. Return to console logically; the user
            // can reboot if the firmware framebuffer stays visible.
            Canvas.DisableFullScreen();
            Console.Clear();
            Console.WriteLine("Mouse graphics test finished. Reboot if the console is not visible.");
        }

        private static void Render(Canvas canvas, int width, int height, int x, int y,
            bool left, bool right, bool middle, int moves, int presses)
        {
            canvas.Clear(Color.FromArgb(14, 23, 38));
            canvas.DrawFilledRectangle(Color.FromArgb(24, 40, 60), 0, 0, width, 100);
            canvas.DrawString("ZonderqOS 3.0.94 - USB MOUSE LIVE TEST",
                PCScreenFont.DefaultFont, Color.White, 20, 18);
            canvas.DrawString("Move mouse, click L/R/M. ESC exits. Nothing is written to disks.",
                PCScreenFont.DefaultFont, Color.LightGray, 20, 47);
            canvas.DrawString("Position: " + x + ", " + y + "   movement events: " + moves,
                PCScreenFont.DefaultFont, Color.White, 20, 115);
            canvas.DrawString("Button changes: " + presses,
                PCScreenFont.DefaultFont, Color.White, 20, 143);

            canvas.DrawFilledRectangle(left ? Color.Lime : Color.DarkGreen, 20, 180, 72, 37);
            canvas.DrawFilledRectangle(right ? Color.Lime : Color.DarkGreen, 102, 180, 72, 37);
            canvas.DrawFilledRectangle(middle ? Color.Lime : Color.DarkGreen, 184, 180, 72, 37);
            canvas.DrawString("LEFT", PCScreenFont.DefaultFont, Color.White, 31, 190);
            canvas.DrawString("RIGHT", PCScreenFont.DefaultFont, Color.White, 108, 190);
            canvas.DrawString("MID", PCScreenFont.DefaultFont, Color.White, 204, 190);
            canvas.DrawString(moves > 0 ? "Movement: PASS" : "Movement: waiting for USB reports...",
                PCScreenFont.DefaultFont, moves > 0 ? Color.Lime : Color.Gold, 20, 239);

            // High-contrast crosshair; visible on the laptop's GOP framebuffer.
            // Draw the cursor last so it is never hidden by diagnostic widgets.
            Color cursor = left ? Color.Lime : right ? Color.Orange : Color.White;
            canvas.DrawLine(Color.Black, x - 11, y, x + 11, y);
            canvas.DrawLine(Color.Black, x, y - 11, x, y + 11);
            canvas.DrawLine(cursor, x - 8, y, x + 8, y);
            canvas.DrawLine(cursor, x, y - 8, x, y + 8);
            canvas.DrawFilledCircle(cursor, x, y, 2);
            canvas.Display();
        }
    }
}
#endif
