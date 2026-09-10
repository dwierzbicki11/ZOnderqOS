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

        private static readonly Color DesktopFallback = Color.FromArgb(12, 18, 27);
        private static readonly Color DesktopGlow = Color.FromArgb(18, 34, 52);

        public void Run()
        {
            try
            {
                Console.WriteLine("[GUI] Inicjalizacja trybu graficznego...");
                canvas = Canvas.GetFullScreen();
                Console.WriteLine($"[GUI] Rzeczywista rozdzielczość Canvas: {canvas.Width}x{canvas.Height}");
                Console.WriteLine("[GUI] Uruchamiam pulpit...");
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);

                int taskbarHeight = 44;
                Window.ConfigureDesktop((int)canvas.Width, (int)canvas.Height - taskbarHeight);
                applicationManager = new ApplicationManager();

                int menuWidth = 280;
                int menuHeight = 360;
                startMenu = new StartMenu(0, (int)canvas.Height - taskbarHeight - menuHeight, menuWidth, menuHeight);

                startMenu.AddItem("Terminal CLI", () =>
                {
                    var terminal = new TerminalApp(125, 90, null);
                    terminal.SetNanoLauncher(path => applicationManager.Launch(new NanoApp(path, null)));
                    applicationManager.Launch(terminal);
                });
                startMenu.AddItem("File Manager", () =>
                {
                    var fileManager = new FileManagerApp(105, 75, path => applicationManager.Launch(new NanoApp(path, null)));
                    applicationManager.Launch(fileManager);
                });
                startMenu.AddItem("Diagnostyka", () => applicationManager.Launch(new DiagnosticsApp(150, 120, null)));
                startMenu.AddItem("O Systemie", () => applicationManager.Launch(new AboutApp(180, 140, null)));
                startMenu.AddItem("Pomoc", () => applicationManager.Launch(new AboutApp(210, 160, null)));
                startMenu.AddItem("Odśwież pulpit", () => { });
                startMenu.AddItem("Sesja GUI", () => { });
                startMenu.AddItem("Informacje systemowe", () => applicationManager.Launch(new DiagnosticsApp(200, 130, null)));
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

                    applicationManager.HandleMouse(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    previousLeftButtonState = currentLeftButtonState;
                    previousRightButtonState = currentRightButtonState;
                    applicationManager.Update();

                    RenderDesktop();
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

        private void RenderDesktop()
        {
            // Warstwa przygotowana pod tapetę PNG/JPG. Do czasu dodania pliku zachowujemy
            // spokojne tło zastępcze, bez zmiany API Canvas ani tworzenia bitmap co klatkę.
            canvas.Clear(DesktopFallback);
            canvas.DrawFilledRectangle(DesktopGlow, 0, 0, (int)canvas.Width, (int)canvas.Height - 44);
        }
    }
}
