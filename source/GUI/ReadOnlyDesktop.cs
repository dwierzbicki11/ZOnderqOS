#if ZONDERQ_READONLY_HARDWARE_PROBE && ZONDERQ_COSMOS_INPUT_API
using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Diskless integration of the REAL Gen3 window, button and cursor widgets
    /// with the USB mouse supplied by Cosmos 3.0.94. This is not an
    /// authenticated desktop and deliberately never starts its services,
    /// wallpaper/icon file cache, shell or VFS.
    /// </summary>
    internal static class ReadOnlyDesktop
    {
        private const int BarHeight = 46;
        private const int MenuWidth = 232;
        private const int MenuHeight = 170;

        internal static void Run()
        {
            Canvas canvas = Canvas.GetFullScreen();
            int width = canvas.Width;
            int height = canvas.Height;
            if (width < 480 || height < 360)
            {
                Console.WriteLine("[DESKTOP3094] Display too small.");
                return;
            }

            MouseManager.SetScreenSize(width, height);
            Window.ConfigureDesktop(width, height - BarHeight);
            int mainWidth = Math.Min(690, width - 40);
            int mainHeight = Math.Min(440, height - BarHeight - 40);

            Window main = new Window((width - mainWidth) / 2,
                (height - BarHeight - mainHeight) / 2,
                mainWidth, mainHeight, "ZonderqOS / USB mouse - Cosmos 3.0.94");
            main.IsActive = true;
            main.CloseAction = () => main.Visible = false;

            int helpWidth = Math.Min(525, width - 40);
            int helpHeight = Math.Min(295, height - BarHeight - 40);
            Window help = new Window((width - helpWidth) / 2,
                (height - BarHeight - helpHeight) / 2,
                helpWidth, helpHeight, "About / diskless desktop");
            help.Visible = false;
            help.CloseAction = () => help.Visible = false;

            string status = "USB HID: move mouse and click buttons.";
            int totalClicks = 0;
            Button inspect = new Button(24, mainHeight - 96, 230, 37,
                "TEST MOUSE", () => status = "USB pointer input OK - live X/Y below.");
            Button openHelp = new Button(270, mainHeight - 96, 180, 37,
                "ABOUT", () => { help.Visible = true; help.IsActive = true; main.IsActive = false; });
            main.AddChild(inspect);
            main.AddChild(openHelp);
            Button closeHelp = new Button(20, helpHeight - 75, 165, 34,
                "BACK", () => { help.Visible = false; main.IsActive = true; });
            help.AddChild(closeHelp);

            bool startOpen = false;
            bool lastLeft = false;
            bool lastRight = false;
            int lastX = -1, lastY = -1;
            int movementCount = 0;
            int buttonCount = 0;
            int idleFrames = 0;
            bool exit = false;
            Window captured = null;

            Console.WriteLine("[DESKTOP3094] START read-only GUI integration. No filesystem mounts or disk writes.");
            while (!exit)
            {
                while (KeyboardManager.TryReadKey(out KeyEvent key))
                {
                    if (key != null && key.Key == Key.Escape)
                    {
                        if (startOpen)
                            startOpen = false;
                        else if (help.Visible)
                            help.Visible = false;
                        else
                            exit = true;
                    }
                }
                if (exit)
                    break;

                int x = MouseManager.X;
                int y = MouseManager.Y;
                bool left = MouseManager.LeftButton;
                bool right = MouseManager.RightButton;
                bool moved = x != lastX || y != lastY;
                bool buttonChanged = left != lastLeft || right != lastRight;
                if (lastX >= 0 && moved) movementCount++;
                if (buttonChanged) buttonCount++;

                inspect.IsHovered = main.Visible && !help.Visible && inspect.Contains(x, y);
                openHelp.IsHovered = main.Visible && !help.Visible && openHelp.Contains(x, y);
                closeHelp.IsHovered = help.Visible && closeHelp.Contains(x, y);

                if (left && !lastLeft)
                {
                    totalClicks++;
                    if (y >= height - BarHeight)
                    {
                        if (x < 116)
                        {
                            startOpen = !startOpen;
                        }
                        else if (x < 255)
                        {
                            Show(main);
                            main.IsActive = true;
                            help.IsActive = false;
                            startOpen = false;
                        }
                        else if (x < 405)
                        {
                            Show(help);
                            help.IsActive = true;
                            main.IsActive = false;
                            startOpen = false;
                        }
                    }
                    else if (startOpen && x >= 0 && x < MenuWidth &&
                             y >= height - BarHeight - MenuHeight &&
                             y < height - BarHeight)
                    {
                        int relative = y - (height - BarHeight - MenuHeight);
                        if (relative < 60)
                        {
                            Show(main);
                            main.IsActive = true;
                            help.IsActive = false;
                        }
                        else if (relative < 110)
                        {
                            Show(help);
                            help.IsActive = true;
                            main.IsActive = false;
                        }
                        else
                        {
                            exit = true;
                        }
                        startOpen = false;
                    }
                    else
                    {
                        startOpen = false;
                        captured = null;
                        if (help.Visible && help.ContainsPoint(x, y))
                        {
                            captured = help;
                            help.IsActive = true;
                            main.IsActive = false;
                            if (closeHelp.Contains(x, y))
                                closeHelp.InvokeClick();
                        }
                        else if (main.Visible && !main.IsMinimized && main.ContainsPoint(x, y))
                        {
                            captured = main;
                            main.IsActive = true;
                            help.IsActive = false;
                            if (inspect.Contains(x, y))
                                inspect.InvokeClick();
                            else if (openHelp.Contains(x, y))
                                openHelp.InvokeClick();
                        }
                        captured?.HandleMouse(x, y, true, false);
                    }
                }
                else if (left && captured != null)
                {
                    captured.HandleMouse(x, y, true, true);
                }

                if (!left && lastLeft)
                {
                    captured?.HandleMouse(x, y, false, true);
                    captured = null;
                }

                if (firstFrame(lastX) || moved || buttonChanged || idleFrames >= 25)
                {
                    Draw(canvas, width, height, main, help, startOpen,
                        x, y, left, right, movementCount, buttonCount, totalClicks, status);
                    idleFrames = 0;
                }
                else
                {
                    idleFrames++;
                }

                lastX = x;
                lastY = y;
                lastLeft = left;
                lastRight = right;
                Thread.Sleep(40);
            }

            Console.WriteLine("[DESKTOP3094] EXIT mouseMoves=" + movementCount +
                " buttonEvents=" + buttonCount + " clicks=" + totalClicks);
            Canvas.DisableFullScreen();
            Console.Clear();
            Console.WriteLine("Read-only desktop closed. Reboot if the text console is not visible.");
        }

        private static bool firstFrame(int lastX) => lastX < 0;

        private static void Show(Window window)
        {
            if (window.IsMinimized)
                window.RestoreFromMinimized();
            else
                window.Visible = true;
        }

        private static void Draw(Canvas canvas, int width, int height,
            Window main, Window help, bool startOpen, int mouseX, int mouseY,
            bool left, bool right, int moves, int buttonEvents, int clicks, string status)
        {
            canvas.Clear(Color.FromArgb(17, 29, 46));
            canvas.DrawFilledRectangle(Color.FromArgb(23, 44, 63), 0, 0, width, 72);
            canvas.DrawString("ZonderqOS Gen 3  /  Cosmos 3.0.94",
                PCScreenFont.DefaultFont, Color.White, 25, 22);
            canvas.DrawString("READ-ONLY LIVE USB DESKTOP - no files or user sessions",
                PCScreenFont.DefaultFont, Color.LightGray, 25, 47);

            if (main.Visible && !main.IsMinimized)
            {
                main.Render(canvas);
                canvas.DrawString("Connected USB pointer -> native Gen3 Window + Button + Cursor",
                    PCScreenFont.DefaultFont, Color.White, main.X + 23, main.Y + 67);
                canvas.DrawString("Drag title bar; resize borders; minimize, maximize or close.",
                    PCScreenFont.DefaultFont, Color.LightGray, main.X + 23, main.Y + 96);
                canvas.DrawString("Move events: " + moves + "     Button events: " + buttonEvents,
                    PCScreenFont.DefaultFont, Color.White, main.X + 23, main.Y + 136);
                canvas.DrawString("Pointer: " + mouseX + "," + mouseY + "     Clicks: " + clicks,
                    PCScreenFont.DefaultFont, Color.White, main.X + 23, main.Y + 168);
                canvas.DrawString("Left: " + (left ? "DOWN" : "UP") +
                    "     Right: " + (right ? "DOWN" : "UP"),
                    PCScreenFont.DefaultFont, Color.LightGreen, main.X + 23, main.Y + 199);
                canvas.DrawString(status, PCScreenFont.DefaultFont,
                    Color.LightGray, main.X + 23, main.Y + 235);
            }

            if (help.Visible && !help.IsMinimized)
            {
                help.Render(canvas);
                canvas.DrawString("This window uses the real ZonderqOS Gen3 widgets.",
                    PCScreenFont.DefaultFont, Color.White, help.X + 18, help.Y + 65);
                canvas.DrawString("USB HID reports are delivered by Cosmos 3.0.94.",
                    PCScreenFont.DefaultFont, Color.LightGray, help.X + 18, help.Y + 95);
                canvas.DrawString("Filesystems, login and services remain disabled.",
                    PCScreenFont.DefaultFont, Color.LightGray, help.X + 18, help.Y + 125);
                canvas.DrawString("ESC: close menu/window, then exit desktop.",
                    PCScreenFont.DefaultFont, Color.LightGray, help.X + 18, help.Y + 155);
            }

            int barY = height - BarHeight;
            canvas.DrawFilledRectangle(Color.FromArgb(22, 28, 36), 0, barY, width, BarHeight);
            canvas.DrawLine(Color.FromArgb(70, 91, 111), 0, barY, width, barY);
            DrawBarItem(canvas, 6, barY + 6, 104, 34, "START", startOpen);
            DrawBarItem(canvas, 118, barY + 6, 132, 34, "DESKTOP", main.Visible && !main.IsMinimized);
            DrawBarItem(canvas, 258, barY + 6, 142, 34, "ABOUT", help.Visible);
            if (width >= 800)
                canvas.DrawString("USB HID  /  READ ONLY  /  3.0.94",
                    PCScreenFont.DefaultFont, Color.LightGray, width - 338, barY + 14);

            if (startOpen)
            {
                int top = barY - MenuHeight;
                canvas.DrawFilledRectangle(Color.FromArgb(31, 42, 55), 0, top, MenuWidth, MenuHeight);
                canvas.DrawRectangle(Color.FromArgb(80, 120, 160), 0, top, MenuWidth, MenuHeight);
                canvas.DrawString("Desktop window", PCScreenFont.DefaultFont, Color.White, 15, top + 18);
                canvas.DrawString("About / help", PCScreenFont.DefaultFont, Color.White, 15, top + 75);
                canvas.DrawString("Exit GUI to console", PCScreenFont.DefaultFont, Color.White, 15, top + 128);
            }

            Cursor.Draw(canvas, mouseX, mouseY);
            canvas.Display();
        }

        private static void DrawBarItem(Canvas canvas, int x, int y,
            int width, int height, string label, bool active)
        {
            canvas.DrawFilledRectangle(active ? Color.FromArgb(51, 94, 135) :
                Color.FromArgb(39, 52, 65), x, y, width, height);
            canvas.DrawRectangle(active ? Color.Cyan : Color.FromArgb(67, 85, 103),
                x, y, width, height);
            canvas.DrawString(label, PCScreenFont.DefaultFont, Color.White, x + 12, y + 9);
        }
    }
}
#endif
