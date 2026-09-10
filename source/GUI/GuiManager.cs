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

                // Settings are read once. Later GUI decisions use only primitive fields,
                // so no configuration-file IO happens in the render loop.
                global::ZonderqOS.SystemSettings.Load();

                // Decode embedded PNG assets exactly once before the first GUI frame.
                // Later renders only reuse persistent raw pixel buffers.
                IconManager.Preload();

                int desktopHeight = (int)canvas.Height - TaskbarHeight;
                Window.ConfigureDesktop((int)canvas.Width, desktopHeight);
                applicationManager = new ApplicationManager();
                LoadWallpaper((int)canvas.Width, desktopHeight);
                InitializeDesktopShortcuts();
                desktopContextMenu = new DesktopContextMenu(
                    () => LaunchTerminal(145, 96),
                    () => LaunchFileManager(120, 78),
                    () => LaunchNotepad(150, 105, null),
                    RefreshDesktop,
                    () => LaunchSettings(145, 92));

                int menuWidth = 480;
                int menuHeight = 560;
                startMenu = new StartMenu(8, (int)canvas.Height - TaskbarHeight - menuHeight - 8, menuWidth, menuHeight);

                startMenu.AddPinned("Terminal", IconType.Terminal, () => LaunchTerminal(125, 90));
                startMenu.AddPinned("File Manager", IconType.Folder, () => LaunchFileManager(105, 75));
                startMenu.AddPinned("Notatnik", IconType.File, () => LaunchNotepad(145, 100, null));
                startMenu.AddPinned("Manager zadan", IconType.Settings, () => LaunchTaskManager(165, 110));
                startMenu.AddPinned("Ustawienia", IconType.Settings, () => LaunchSettings(125, 82));
                startMenu.AddPinned("Zdjecia", IconType.ImageViewer, () => LaunchImageViewer(155, 92, null));

                startMenu.AddTool("Kalkulator", IconType.Calculator, () => LaunchCalculator(185, 96));
                startMenu.AddTool("Kalendarz", IconType.Calendar, () => LaunchCalendar(170, 88));
                startMenu.AddTool("Diagnostyka", IconType.About, () => LaunchDiagnostics(150, 120));
                startMenu.AddTool("O Systemie", IconType.About, () => LaunchAbout(210, 160));
                startMenu.AddTool("Dyski", IconType.DiskManager, () => LaunchDiskManager(135, 78));
                startMenu.AddTool("Siec", IconType.Network, () => LaunchNetworkCenter(150, 84));

                startMenu.SetPowerActions(
                    () => Cosmos.Kernel.System.Power.Reboot(),
                    () => Cosmos.Kernel.System.Power.Shutdown(),
                    () => isRunning = false);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, TaskbarHeight, () =>
                {
                    bool opening = !startMenu.Visible;
                    startMenu.Visible = opening;
                    if (opening)
                        startMenu.ResetSearch();
                    if (desktopContextMenu != null)
                        desktopContextMenu.Visible = false;
                    selectedShortcut = -1;
                }, applicationManager);

                bool previousLeftButtonState = false;
                bool previousRightButtonState = false;
                int previousMouseX = -1;
                int previousMouseY = -1;

                int firstMouseX = (int)MouseManager.X;
                int firstMouseY = (int)MouseManager.Y;
                RenderFrame(firstMouseX, firstMouseY);

                while (isRunning)
                {
                    frameCounter++;
                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;
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

                        if (startMenu.Visible && startMenu.HandleKeyboard(key))
                            continue;

                        applicationManager.HandleKeyboard(key);
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeftButtonState = MouseManager.LeftButton;
                    bool currentRightButtonState = MouseManager.RightButton;

                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeftButtonState != previousLeftButtonState ||
                                         currentRightButtonState != previousRightButtonState;

                    applicationManager.HandleMouse(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);
                    startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                    UpdateDesktopInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                        currentRightButtonState, previousRightButtonState);

                    previousLeftButtonState = currentLeftButtonState;
                    previousRightButtonState = currentRightButtonState;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

                    applicationManager.Update();

                    int heartbeatFrames = applicationManager.HasLiveTelemetryWindow
                        ? global::ZonderqOS.SystemSettings.TelemetryHeartbeatFrames
                        : global::ZonderqOS.SystemSettings.IdleHeartbeatFrames;
                    if (heartbeatFrames < 1)
                        heartbeatFrames = 1;
                    bool heartbeat = frameCounter % heartbeatFrames == 0;

                    if (keyboardActivity || pointerMoved || buttonChanged || heartbeat)
                        RenderFrame(mouseX, mouseY);

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

        private void RenderFrame(int mouseX, int mouseY)
        {
            RenderDesktop();
            RenderDesktopShortcuts();
            applicationManager.Render(canvas);
            taskbar.Render(canvas);
            startMenu.Render(canvas);
            desktopContextMenu?.Render(canvas);
            Cursor.Draw(canvas, mouseX, mouseY);
            canvas.Display();
        }

        private void InitializeDesktopShortcuts()
        {
            desktopShortcuts = new[]
            {
                new DesktopShortcut(20, 22, "File Manager", IconType.Folder, () => LaunchFileManager(110, 72)),
                new DesktopShortcut(20, 116, "Terminal", IconType.Terminal, () => LaunchTerminal(135, 92)),
                new DesktopShortcut(20, 210, "Notatnik", IconType.File, () => LaunchNotepad(150, 105, null)),
                new DesktopShortcut(20, 304, "Ustawienia", IconType.Settings, () => LaunchSettings(130, 82)),
                new DesktopShortcut(20, 398, "Zdjecia", IconType.ImageViewer, () => LaunchImageViewer(155, 92, null)),
                new DesktopShortcut(20, 492, "Kalkulator", IconType.Calculator, () => LaunchCalculator(185, 96)),
                new DesktopShortcut(20, 586, "Siec", IconType.Network, () => LaunchNetworkCenter(150, 84)),
                new DesktopShortcut(20, 680, "Dyski", IconType.DiskManager, () => LaunchDiskManager(135, 78)),
                new DesktopShortcut(20, 774, "About", IconType.About, () => LaunchAbout(180, 138)),
                new DesktopShortcut(120, 22, "Kalendarz", IconType.Calendar, () => LaunchCalendar(170, 88))
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
            var fileManager = new FileManagerApp(x, y,
                path => LaunchFileByType(path));
            applicationManager.Launch(fileManager);
        }

        private void LaunchFileByType(string path)
        {
            if (IsImagePath(path))
            {
                LaunchImageViewer(155, 92, path);
                return;
            }

            LaunchNotepad(145, 95, path);
        }

        private static bool IsImagePath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
        }

        private void LaunchImageViewer(int x, int y, string path)
        {
            applicationManager.Launch(new ImageViewerApp(x, y, path, null));
        }

        private void LaunchCalculator(int x, int y)
        {
            applicationManager.Launch(new CalculatorApp(x, y, null));
        }

        private void LaunchCalendar(int x, int y)
        {
            applicationManager.Launch(new CalendarApp(x, y, null));
        }

        private void LaunchNetworkCenter(int x, int y)
        {
            applicationManager.Launch(new NetworkCenterApp(x, y, applicationManager, null));
        }

        private void LaunchDiskManager(int x, int y)
        {
            applicationManager.Launch(new DiskManagerApp(x, y, null));
        }

        private void LaunchNotepad(int x, int y, string path)
        {
            applicationManager.Launch(new NotepadApp(x, y, path, null));
        }

        private void LaunchTaskManager(int x, int y)
        {
            applicationManager.Launch(new TaskManagerModernApp(x, y, applicationManager, null));
        }

        private void LaunchSettings(int x, int y)
        {
            applicationManager.Launch(new SettingsApp(x, y, applicationManager, null));
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

            if (!global::ZonderqOS.SystemSettings.ShowDesktopIcons)
            {
                selectedShortcut = -1;
                for (int i = 0; i < desktopShortcuts.Length; i++)
                {
                    desktopShortcuts[i].IsHovered = false;
                    desktopShortcuts[i].IsSelected = false;
                }

                if (rightClicked && !wasRightClicked && !IsPointOverWindow(mouseX, mouseY) && !startMenu.Visible &&
                    mouseY >= 0 && mouseY < (int)canvas.Height - TaskbarHeight && desktopContextMenu != null)
                {
                    desktopContextMenu.ShowAt(mouseX, mouseY, (int)canvas.Width,
                        (int)canvas.Height - TaskbarHeight);
                }
                return;
            }

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
            if (desktopShortcuts == null || !global::ZonderqOS.SystemSettings.ShowDesktopIcons)
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
            int mode = global::ZonderqOS.SystemSettings.DesktopBackgroundMode;
            if (mode == 0 && wallpaperCanvas != null)
            {
                canvas.DrawCanvas(wallpaperCanvas, 0, 0);
                return;
            }

            Color background = mode == 1
                ? Color.FromArgb(25, 30, 36)
                : Color.FromArgb(17, 31, 47);
            canvas.Clear(background);
            canvas.DrawFilledRectangle(background, 0, 0, (int)canvas.Width,
                (int)canvas.Height - TaskbarHeight);
        }
    }
}