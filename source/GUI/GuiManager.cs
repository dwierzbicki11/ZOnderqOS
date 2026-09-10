using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class GuiManager
    {
        private Canvas canvas;
        private Canvas wallpaperCanvas;
        private Taskbar taskbar;
        private StartMenu startMenu;
        private DesktopContextMenu desktopContextMenu;
        private ApplicationManager applicationManager;
        private DesktopShortcut[] desktopShortcuts;
        private bool isRunning = true;
        private int selectedShortcut = -1;
        private int lastShortcutClick = -1;
        private int lastShortcutClickFrame = -1000;
        private int frameCounter;

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
                InitializeDesktopShortcuts();
                desktopContextMenu = new DesktopContextMenu(
                    () => LaunchTerminal(145, 96),
                    () => LaunchFileManager(120, 78),
                    RefreshDesktop,
                    () => LaunchDiagnostics(170, 120));

                int menuWidth = 340;
                int menuHeight = 430;
                startMenu = new StartMenu(8, (int)canvas.Height - TaskbarHeight - menuHeight - 8, menuWidth, menuHeight);

                startMenu.AddItem("Terminal CLI", IconType.Terminal, () => LaunchTerminal(125, 90));
                startMenu.AddItem("File Manager", IconType.Folder, () => LaunchFileManager(105, 75));
                startMenu.AddItem("Diagnostyka", IconType.Settings, () => LaunchDiagnostics(150, 120));
                startMenu.AddItem("O Systemie", IconType.About, () => LaunchAbout(180, 140));
                startMenu.AddItem("Pomoc", IconType.About, () => LaunchAbout(210, 160));
                startMenu.AddItem("Odśwież pulpit", IconType.Refresh, RefreshDesktop);
                startMenu.AddItem("Sesja GUI", IconType.Start, () => { });
                startMenu.AddItem("Informacje systemowe", IconType.Settings, () => LaunchDiagnostics(200, 130));
                startMenu.AddItem("Wyjdź z GUI", IconType.Close, () => isRunning = false);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, TaskbarHeight, () =>
                {
                    startMenu.Visible = !startMenu.Visible;
                    if (desktopContextMenu != null)
                        desktopContextMenu.Visible = false;
                    selectedShortcut = -1;
                }, applicationManager);

                bool previousLeftButtonState = false;
                bool previousRightButtonState = false;
                while (isRunning)
                {
                    frameCounter++;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        if (key.Key == ConsoleKeyEx.Escape && startMenu.Visible)
                        {
                            startMenu.Visible = false;
                            continue;
                        }

                        if (key.Key == ConsoleKeyEx.Escape && desktopContextMenu != null && desktopContextMenu.Visible)
                        {
                            desktopContextMenu.Visible = false;
                            continue;
                        }

                        applicationManager.HandleKeyboard(key);
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeftButtonState = MouseManager.LeftButton;
                    bool currentRightButtonState = MouseManager.RightButton;

                    applicationManager.HandleMouse(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    UpdateDesktopInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);

                    previousLeftButtonState = currentLeftButtonState;
                    previousRightButtonState = currentRightButtonState;
                    applicationManager.Update();

                    RenderDesktop();
                    RenderDesktopShortcuts();
                    applicationManager.Render(canvas);
                    taskbar.Render(canvas);
                    startMenu.Render(canvas);
                    desktopContextMenu?.Render(canvas);
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

        private void InitializeDesktopShortcuts()
        {
            desktopShortcuts = new[]
            {
                new DesktopShortcut(20, 22, "File Manager", IconType.Folder, () => LaunchFileManager(110, 72)),
                new DesktopShortcut(20, 116, "Terminal", IconType.Terminal, () => LaunchTerminal(135, 92)),
                new DesktopShortcut(20, 210, "System", IconType.Settings, () => LaunchDiagnostics(155, 116)),
                new DesktopShortcut(20, 304, "About", IconType.About, () => LaunchAbout(180, 138))
            };
        }

        private void RefreshDesktop()
        {
            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickFrame = -1000;

            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;
        }

        private void LaunchTerminal(int x, int y)
        {
            var terminal = new TerminalApp(x, y, null);
            terminal.SetNanoLauncher(path => applicationManager.Launch(new NanoApp(path, null)));
            applicationManager.Launch(terminal);
        }

        private void LaunchFileManager(int x, int y)
        {
            var fileManager = new FileManagerApp(x, y, path => applicationManager.Launch(new NanoApp(path, null)));
            applicationManager.Launch(fileManager);
        }

        private void LaunchDiagnostics(int x, int y)
        {
            applicationManager.Launch(new DiagnosticsApp(x, y, null));
        }

        private void LaunchAbout(int x, int y)
        {
            applicationManager.Launch(new AboutApp(x, y, null));
        }

        private void UpdateDesktopInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked,
            bool rightClicked, bool wasRightClicked)
        {
            if (desktopShortcuts == null)
                return;

            if (desktopContextMenu != null && desktopContextMenu.Visible)
            {
                if (desktopContextMenu.UpdateInteractions(mouseX, mouseY, isClicked, wasClicked))
                    return;
            }

            bool desktopAvailable = mouseY >= 0 && mouseY < (int)canvas.Height - TaskbarHeight &&
                                    !IsPointOverWindow(mouseX, mouseY) && !startMenu.Visible;

            for (int i = 0; i < desktopShortcuts.Length; i++)
            {
                DesktopShortcut shortcut = desktopShortcuts[i];
                shortcut.IsHovered = desktopAvailable && shortcut.Contains(mouseX, mouseY);
                shortcut.IsSelected = selectedShortcut == i;
            }

            if (rightClicked && !wasRightClicked)
            {
                if (desktopAvailable && desktopContextMenu != null)
                {
                    selectedShortcut = -1;
                    lastShortcutClick = -1;
                    desktopContextMenu.ShowAt(mouseX, mouseY, (int)canvas.Width,
                        (int)canvas.Height - TaskbarHeight);
                }
                return;
            }

            if (!isClicked || wasClicked)
                return;

            if (startMenu.Visible)
            {
                bool insideMenu = mouseX >= startMenu.X && mouseX < startMenu.X + startMenu.Width &&
                                  mouseY >= startMenu.Y && mouseY < startMenu.Y + startMenu.Height;
                if (!insideMenu && mouseY < taskbar.Y)
                    startMenu.Visible = false;
                return;
            }

            if (!desktopAvailable)
                return;

            int hitIndex = -1;
            for (int i = 0; i < desktopShortcuts.Length; i++)
            {
                if (desktopShortcuts[i].Contains(mouseX, mouseY))
                {
                    hitIndex = i;
                    break;
                }
            }

            if (hitIndex < 0)
            {
                selectedShortcut = -1;
                lastShortcutClick = -1;
                return;
            }

            bool doubleClick = selectedShortcut == hitIndex && lastShortcutClick == hitIndex &&
                               frameCounter - lastShortcutClickFrame <= 28;

            selectedShortcut = hitIndex;
            lastShortcutClick = hitIndex;
            lastShortcutClickFrame = frameCounter;

            if (doubleClick)
            {
                desktopShortcuts[hitIndex].Open();
                selectedShortcut = -1;
                lastShortcutClick = -1;
            }
        }

        private bool IsPointOverWindow(int mouseX, int mouseY)
        {
            if (applicationManager == null)
                return false;

            var applications = applicationManager.Applications;
            for (int i = applications.Count - 1; i >= 0; i--)
            {
                Application app = applications[i];
                if (app != null && app.IsRunning && app.Window != null && app.Window.ContainsPoint(mouseX, mouseY))
                    return true;
            }
            return false;
        }

        private void RenderDesktopShortcuts()
        {
            if (desktopShortcuts == null)
                return;

            for (int i = 0; i < desktopShortcuts.Length; i++)
            {
                desktopShortcuts[i].IsSelected = selectedShortcut == i;
                desktopShortcuts[i].Render(canvas);
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
