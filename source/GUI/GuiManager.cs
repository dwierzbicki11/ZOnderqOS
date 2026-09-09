using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using ZonderqOS.GUI.Apps;

namespace ZonderqOS.GUI
{
    public class GuiManager
    {
        private Canvas canvas;
        private Taskbar taskbar;
        private StartMenu startMenu;
        private ApplicationManager applicationManager;
        private bool isRunning = true;

        public void Run()
        {
            try
            {
                Console.WriteLine("[GUI] Inicjalizacja trybu graficznego...");

                canvas = Canvas.GetFullScreen();

                Console.WriteLine($"[GUI] Rzeczywista rozdzielczość Canvas: {canvas.Width}x{canvas.Height}");
                Console.WriteLine("[GUI] Uruchamiam pulpit...");

                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                int taskbarHeight = 30;

                Window.ConfigureDesktop((int)canvas.Width, (int)canvas.Height - taskbarHeight);
                applicationManager = new ApplicationManager();

                int menuWidth = 200;
                int menuHeight = 160;
                startMenu = new StartMenu(0, (int)canvas.Height - taskbarHeight - menuHeight, menuWidth, menuHeight);

                startMenu.AddItem("Terminal CLI", () =>
                {
                    int offset = 25;
                    var terminal = new TerminalApp(100 + offset, 80 + offset, null);
                    applicationManager.Launch(terminal);
                });

                startMenu.AddItem("Diagnostyka", () => Console.WriteLine("[ZonderqOS] Diagnostyka sprzętu..."));
                startMenu.AddItem("O Systemie", () => Console.WriteLine("[ZonderqOS] v0.3 Cosmos Gen3"));
                startMenu.AddItem("Wyjdź z GUI", () => isRunning = false);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, taskbarHeight, () =>
                {
                    startMenu.Visible = !startMenu.Visible;
                });

                bool previousLeftButtonState = false;

                while (isRunning)
                {
                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key != null)
                        {
                            if (key.Key == ConsoleKeyEx.Escape)
                                isRunning = false;
                            else
                                applicationManager.HandleKeyboard(key);
                        }
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeftButtonState = MouseManager.LeftButton;

                    applicationManager.HandleMouse(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);

                    previousLeftButtonState = currentLeftButtonState;
                    applicationManager.Update();

                    canvas.Clear(Color.MidnightBlue);
                    applicationManager.Render(canvas);
                    taskbar.Render(canvas);
                    startMenu.Render(canvas);
                    Cursor.Draw(canvas, mouseX, mouseY);

                    canvas.Display();
                    Thread.Sleep(15);
                }

                canvas.Clear(Color.Black);
                canvas.Display();
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError($"Błąd w pętli GUI: {ex.Message}", "GUI");
            }
        }
    }
}