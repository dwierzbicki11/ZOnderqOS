using System;
using System.Drawing;
using System.IO;
using System.Reflection;
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
        private Canvas wallpaperCanvas;
        private Taskbar taskbar;
        private StartMenu startMenu;
        private ApplicationManager applicationManager;
        private bool isRunning = true;

        private const int TaskbarHeight = 44;
        private const string WallpaperCacheDirectory = "/root/.zonderq-wallpapers";
        private const string WallpaperCachePath = WallpaperCacheDirectory + "/wallpaper.png";
        private const string WallpaperResourceName = "Wallpapers.wallpaper.png";

        public void Run()
        {
            try
            {
                Console.WriteLine("[GUI] Inicjalizacja trybu graficznego...");
                canvas = Canvas.GetFullScreen();
                Console.WriteLine($"[GUI] Rzeczywista rozdzielczość Canvas: {canvas.Width}x{canvas.Height}");
                Console.WriteLine("[GUI] Uruchamiam pulpit...");
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);

                int desktopHeight = (int)canvas.Height - TaskbarHeight;
                Window.ConfigureDesktop((int)canvas.Width, desktopHeight);
                applicationManager = new ApplicationManager();
                LoadWallpaper((int)canvas.Width, desktopHeight);

                int menuWidth = 280;
                int menuHeight = 360;
                startMenu = new StartMenu(0, (int)canvas.Height - TaskbarHeight - menuHeight, menuWidth, menuHeight);

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

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, TaskbarHeight, () =>
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

        private void LoadWallpaper(int width, int height)
        {
            try
            {
                Assembly assembly = typeof(GuiManager).Assembly;
                using (Stream stream = assembly.GetManifestResourceStream(WallpaperResourceName))
                {
                    if (stream == null)
                    {
                        Console.WriteLine("[GUI] Nie znaleziono zasobu tapety PNG.");
                        return;
                    }

                    Directory.CreateDirectory(WallpaperCacheDirectory);
                    if (!File.Exists(WallpaperCachePath))
                    {
                        using (FileStream output = File.Create(WallpaperCachePath))
                            stream.CopyTo(output);
                    }
                }

                wallpaperCanvas = new Canvas(width, height);
                wallpaperCanvas.Clear(Color.FromArgb(12, 18, 27));

                Png wallpaper = new Png(WallpaperCachePath);
                wallpaperCanvas.DrawImage(wallpaper, 0, 0, width, height);
            }
            catch (Exception ex)
            {
                wallpaperCanvas = null;
                Console.WriteLine($"[GUI] Nie udało się przygotować tapety: {ex.Message}");
            }
        }

        private void RenderDesktop()
        {
            if (wallpaperCanvas != null)
            {
                canvas.DrawCanvas(wallpaperCanvas, 0, 0);
                return;
            }

            canvas.Clear(Color.FromArgb(12, 18, 27));
            canvas.DrawFilledRectangle(Color.FromArgb(18, 34, 52), 0, 0, (int)canvas.Width, (int)canvas.Height - TaskbarHeight);
        }
    }
}
