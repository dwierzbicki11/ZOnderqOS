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

                int menuWidth = 240;
                int menuHeight = 300;
                startMenu = new StartMenu(0, (int)canvas.Height - taskbarHeight - menuHeight, menuWidth, menuHeight);

                startMenu.AddItem("Terminal CLI", () =>
                {
                    int offset = 25;
                    var terminal = new TerminalApp(100 + offset, 80 + offset, null);
                    terminal.SetNanoLauncher(path => applicationManager.Launch(new NanoApp(path, null)));
                    applicationManager.Launch(terminal);
                });

                startMenu.AddItem("File Manager", () =>
                {
                    int offset = 35;
                    var fileManager = new FileManagerApp(70 + offset, 55 + offset,
                        path => applicationManager.Launch(new NanoApp(path, null)));
                    applicationManager.Launch(fileManager);
                });

                startMenu.AddItem("Diagnostyka", () =>
                {
                    applicationManager.Launch(new DiagnosticsApp(140, 110, null));
                });

                startMenu.AddItem("O Systemie", () =>
                {
                    applicationManager.Launch(new AboutApp(170, 130, null));
                });

                startMenu.AddItem("Pomoc", () =>
                {
                    applicationManager.Launch(new AboutApp(200, 150, null));
                });

                startMenu.AddItem("Odśwież pulpit", () => { });
                startMenu.AddItem("Sesja GUI", () => { });
                startMenu.AddItem("Informacje systemowe", () =>
                {
                    applicationManager.Launch(new DiagnosticsApp(180, 120, null));
                });
                startMenu.AddItem("Wyjdź z GUI", () => isRunning = false);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, taskbarHeight, () =>
                {
                    startMenu.Visible = !startMenu.Visible;
                }, applicationManager);

                bool previousLeftButtonState = false;
                bool previousRightButtonState = false;
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
                    bool currentRightButtonState = MouseManager.RightButton;

                    applicationManager.HandleMouse(mouseX, mouseY,
                        currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    previousLeftButtonState = currentLeftButtonState;
                    previousRightButtonState = currentRightButtonState;
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
