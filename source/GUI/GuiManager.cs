using System;
using System.Diagnostics;
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
        private NotificationCenter notificationCenter;
        private DesktopContextMenu desktopContextMenu;
        private ApplicationManager applicationManager;
        private AppRegistry appRegistry;
        private DesktopShortcut[] desktopShortcuts;
        private bool isRunning = true;
        private bool lockRequested;
        private long lastActivityTimestamp;
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

                // Notifications are session-local so messages from one authenticated user
                // never leak into the next login session.
                global::ZonderqOS.NotificationService.ResetForSession();

                // Decode embedded PNG assets exactly once before the first GUI frame.
                // Later renders only reuse persistent raw pixel buffers.
                IconManager.Preload();

                int desktopHeight = (int)canvas.Height - TaskbarHeight;
                Window.ConfigureDesktop((int)canvas.Width, desktopHeight);
                applicationManager = new ApplicationManager();
                InitializeAppRegistry();
                LoadWallpaper((int)canvas.Width, desktopHeight);
                InitializeDesktopShortcuts();
                desktopContextMenu = new DesktopContextMenu(
                    () => LaunchRegisteredApp(AppIds.Terminal),
                    () => LaunchRegisteredApp(AppIds.FileManager),
                    () => LaunchRegisteredApp(AppIds.Notepad),
                    RefreshDesktop,
                    () => LaunchRegisteredApp(AppIds.Settings));

                int menuWidth = 480;
                int menuHeight = 560;
                startMenu = new StartMenu(8, (int)canvas.Height - TaskbarHeight - menuHeight - 8, menuWidth, menuHeight);
                PopulateStartMenu();

                startMenu.SetPowerActions(
                    () => Cosmos.Kernel.System.Power.Reboot(),
                    () => Cosmos.Kernel.System.Power.Shutdown(),
                    LogoutSession);
                startMenu.SetLockAction(RequestLockSession);

                notificationCenter = new NotificationCenter((int)canvas.Width, (int)canvas.Height, TaskbarHeight);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, TaskbarHeight, () =>
                {
                    bool opening = !startMenu.Visible;
                    startMenu.Visible = opening;
                    if (opening)
                    {
                        startMenu.ResetSearch();
                        notificationCenter?.Close();
                    }
                    if (desktopContextMenu != null)
                        desktopContextMenu.Visible = false;
                    selectedShortcut = -1;
                }, applicationManager, ToggleNotificationCenter);

                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Security,
                    "SESJA",
                    "Witaj, " + SecurityContext.CurrentUser,
                    "Bezpieczna sesja ZOnderqOS zostala uruchomiona.");

                bool previousLeftButtonState = false;
                bool previousRightButtonState = false;
                int previousMouseX = -1;
                int previousMouseY = -1;

                int firstMouseX = (int)MouseManager.X;
                int firstMouseY = (int)MouseManager.Y;
                ResetActivityTimer();
                RenderFrame(firstMouseX, firstMouseY);

                while (isRunning)
                {
                    frameCounter++;

                    // A terminal command may end the session too. Never leave the old
                    // desktop alive after SecurityContext switches back to login state.
                    if (!SecurityContext.IsAuthenticated)
                    {
                        applicationManager.CloseAll();
                        global::ZonderqOS.NotificationService.ResetForSession();
                        isRunning = false;
                        break;
                    }

                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;

                        if (IsSecureSessionShortcut(key))
                        {
                            if (startMenu != null)
                                startMenu.Visible = false;
                            notificationCenter?.Close();
                            if (desktopContextMenu != null)
                                desktopContextMenu.Visible = false;

                            SecureSessionAction action = SecureSessionScreenManager.Run(canvas);
                            if (action == SecureSessionAction.Lock)
                            {
                                RequestLockSession();
                            }
                            else if (action == SecureSessionAction.Logout)
                            {
                                LogoutSession();
                            }

                            previousLeftButtonState = MouseManager.LeftButton;
                            previousRightButtonState = MouseManager.RightButton;
                            previousMouseX = (int)MouseManager.X;
                            previousMouseY = (int)MouseManager.Y;
                            ResetActivityTimer();

                            if (isRunning && !lockRequested)
                                RenderFrame(previousMouseX, previousMouseY);
                            break;
                        }

                        if (IsLockShortcut(key))
                        {
                            RequestLockSession();
                            break;
                        }

                        if (IsNotificationShortcut(key))
                        {
                            ToggleNotificationCenter();
                            continue;
                        }

                        if (notificationCenter != null && notificationCenter.Visible)
                        {
                            notificationCenter.HandleKeyboard(key);
                            continue;
                        }

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

                    if (!isRunning)
                        break;

                    if (lockRequested)
                    {
                        lockRequested = false;
                        if (!RunLockScreen())
                        {
                            LogoutSession();
                            break;
                        }

                        previousLeftButtonState = MouseManager.LeftButton;
                        previousRightButtonState = MouseManager.RightButton;
                        previousMouseX = (int)MouseManager.X;
                        previousMouseY = (int)MouseManager.Y;
                        ResetActivityTimer();
                        RenderFrame(previousMouseX, previousMouseY);
                        continue;
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeftButtonState = MouseManager.LeftButton;
                    bool currentRightButtonState = MouseManager.RightButton;

                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeftButtonState != previousLeftButtonState ||
                                         currentRightButtonState != previousRightButtonState;

                    if (keyboardActivity || pointerMoved || buttonChanged)
                    {
                        ResetActivityTimer();
                    }
                    else if (ShouldAutoLock())
                    {
                        SecurityLogger.LogEvent("INFO", "Automatic idle lock triggered for user " +
                            SecurityContext.CurrentUser + ".");
                        RequestLockSession();
                        lockRequested = false;
                        if (!RunLockScreen())
                        {
                            LogoutSession();
                            break;
                        }

                        previousLeftButtonState = MouseManager.LeftButton;
                        previousRightButtonState = MouseManager.RightButton;
                        previousMouseX = (int)MouseManager.X;
                        previousMouseY = (int)MouseManager.Y;
                        ResetActivityTimer();
                        RenderFrame(previousMouseX, previousMouseY);
                        continue;
                    }

                    bool notificationCaptured = notificationCenter != null &&
                        notificationCenter.UpdateInteractions(mouseX, mouseY,
                            currentLeftButtonState, previousLeftButtonState);

                    if (!notificationCaptured)
                    {
                        applicationManager.HandleMouse(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                            currentRightButtonState, previousRightButtonState);
                        startMenu.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);

                        // A lock requested from Start must take over before the taskbar or desktop
                        // can process the same click. Open applications remain alive and untouched.
                        if (lockRequested)
                        {
                            lockRequested = false;
                            if (!RunLockScreen())
                            {
                                LogoutSession();
                                break;
                            }

                            previousLeftButtonState = MouseManager.LeftButton;
                            previousRightButtonState = MouseManager.RightButton;
                            previousMouseX = (int)MouseManager.X;
                            previousMouseY = (int)MouseManager.Y;
                            ResetActivityTimer();
                            RenderFrame(previousMouseX, previousMouseY);
                            continue;
                        }

                        taskbar.UpdateInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState);
                        UpdateDesktopInteractions(mouseX, mouseY, currentLeftButtonState, previousLeftButtonState,
                            currentRightButtonState, previousRightButtonState);
                    }

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
            notificationCenter?.Render(canvas);
            Cursor.Draw(canvas, mouseX, mouseY);
            canvas.Display();
        }

        private void InitializeAppRegistry()
        {
            appRegistry = new AppRegistry();

            appRegistry.Register(new AppDescriptor(
                AppIds.Terminal, "Terminal", "Terminal", "Terminal",
                "Powłoka i narzedzia wiersza polecen",
                AppCategory.Tools, IconType.Terminal, () => LaunchTerminal(145, 96),
                StartMenuPlacement.Pinned, 0, 1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.FileManager, "File Manager", "File Manager", "File Manager",
                "Pliki, katalogi, schowek i zamontowane woluminy",
                AppCategory.Files, IconType.Folder, () => LaunchFileManager(120, 78),
                StartMenuPlacement.Pinned, 1, 0, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Notepad, "Notatnik", "Notatnik", "Notatnik",
                "Lekki edytor tekstu i plikow konfiguracyjnych",
                AppCategory.Files, IconType.File, () => LaunchNotepad(150, 105, null),
                StartMenuPlacement.Pinned, 2, 2, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.ImageViewer, "Zdjecia", "Zdjecia", "Zdjecia",
                "Przegladarka obrazow PNG i BMP",
                AppCategory.Files, IconType.ImageViewer, () => LaunchImageViewer(155, 92, null),
                StartMenuPlacement.Pinned, 5, 4, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Calculator, "Kalkulator", "Kalkulator", "Kalkulator",
                "Kalkulator standardowy z pamiecia i historia",
                AppCategory.Tools, IconType.Calculator, () => LaunchCalculator(185, 96),
                StartMenuPlacement.Tool, 0, 5, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Calendar, "Kalendarz", "Kalendarz", "Kalendarz",
                "Miesieczny kalendarz systemowy",
                AppCategory.Tools, IconType.Calendar, () => LaunchCalendar(170, 88),
                StartMenuPlacement.Tool, 1, 9, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.NetworkCenter, "Centrum sieci", "Siec", "Siec",
                "Interfejsy, DHCP, IPv4, DNS i test polaczenia",
                AppCategory.System, IconType.Network, () => LaunchNetworkCenter(150, 84),
                StartMenuPlacement.Tool, 5, 6, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.DiskManager, "Menedzer dyskow", "Dyski", "Dyski",
                "Dyski, partycje i punkty montowania VFS",
                AppCategory.System, IconType.DiskManager, () => LaunchDiskManager(135, 78),
                StartMenuPlacement.Tool, 4, 7, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.TaskManager, "Manager zadan", "Manager zadan", "Manager zadan",
                "Procesy, CPU, pamiec i telemetria kernela",
                AppCategory.System, IconType.Settings, () => LaunchTaskManager(165, 110),
                StartMenuPlacement.Pinned, 3, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Settings, "Ustawienia", "Ustawienia", "Ustawienia",
                "Konfiguracja systemu, GUI, sieci i kont",
                AppCategory.System, IconType.Settings, () => LaunchSettings(125, 82),
                StartMenuPlacement.Pinned, 4, 3, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Diagnostics, "Diagnostyka", "Diagnostyka", "Diagnostyka",
                "Szybki podglad stanu komponentow systemowych",
                AppCategory.System, IconType.About, () => LaunchDiagnostics(150, 120),
                StartMenuPlacement.Tool, 2, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.About, "O Systemie", "O Systemie", "About",
                "Informacje o ZOnderqOS i platformie Cosmos Gen3",
                AppCategory.System, IconType.About, () => LaunchAbout(210, 160),
                StartMenuPlacement.None, -1, 8, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.AppCenter, "App Center", "App Center", "App Center",
                "Katalog wbudowanych aplikacji ZOnderqOS",
                AppCategory.System, IconType.AppCenter, () => LaunchAppCenter(145, 76),
                StartMenuPlacement.Tool, 3, 10, false));
        }

        private void PopulateStartMenu()
        {
            AddStartMenuApps(StartMenuPlacement.Pinned);
            AddStartMenuApps(StartMenuPlacement.Tool);
        }

        private void AddStartMenuApps(StartMenuPlacement placement)
        {
            if (startMenu == null || appRegistry == null)
                return;

            for (int order = 0; order < appRegistry.Count; order++)
            {
                for (int i = 0; i < appRegistry.Count; i++)
                {
                    AppDescriptor app = appRegistry.GetAt(i);
                    if (app == null || app.StartPlacement != placement || app.StartOrder != order)
                        continue;

                    Action launch = app.Launcher;
                    if (placement == StartMenuPlacement.Pinned)
                        startMenu.AddPinned(app.MenuName, app.Icon, launch);
                    else
                        startMenu.AddTool(app.MenuName, app.Icon, launch);
                }
            }
        }

        private void InitializeDesktopShortcuts()
        {
            if (appRegistry == null)
            {
                desktopShortcuts = new DesktopShortcut[0];
                return;
            }

            int shortcutCount = 0;
            for (int i = 0; i < appRegistry.Count; i++)
            {
                AppDescriptor app = appRegistry.GetAt(i);
                if (app != null && app.DesktopOrder >= 0)
                    shortcutCount++;
            }

            desktopShortcuts = new DesktopShortcut[shortcutCount];
            int target = 0;
            for (int order = 0; order < appRegistry.Count && target < shortcutCount; order++)
            {
                for (int i = 0; i < appRegistry.Count; i++)
                {
                    AppDescriptor app = appRegistry.GetAt(i);
                    if (app == null || app.DesktopOrder != order)
                        continue;

                    const int rowsPerColumn = 9;
                    int column = target / rowsPerColumn;
                    int row = target % rowsPerColumn;
                    int x = 20 + column * 100;
                    int y = 22 + row * 94;
                    desktopShortcuts[target++] = new DesktopShortcut(
                        x, y, app.DesktopName, app.Icon, app.Launcher);
                }
            }
        }

        private void LaunchRegisteredApp(string id)
        {
            if (appRegistry != null)
                appRegistry.Launch(id);
        }

        private void RefreshDesktop()
        {
            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickFrame = -1000;

            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;
        }

        private void ToggleNotificationCenter()
        {
            if (notificationCenter == null)
                return;

            bool opening = !notificationCenter.Visible;
            if (opening)
            {
                if (startMenu != null)
                    startMenu.Visible = false;
                if (desktopContextMenu != null)
                    desktopContextMenu.Visible = false;
                selectedShortcut = -1;
            }
            notificationCenter.Toggle();
        }

        private void RequestLockSession()
        {
            if (!SecurityContext.IsAuthenticated)
                return;

            if (startMenu != null)
                startMenu.Visible = false;
            notificationCenter?.Close();
            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;

            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickFrame = -1000;
            lockRequested = true;
        }

        private bool RunLockScreen()
        {
            if (!SecurityContext.IsAuthenticated || canvas == null)
                return false;

            SecurityLogger.LogEvent("INFO", "Graphical session locked for user " +
                SecurityContext.CurrentUser + ".");
            bool unlocked = LockScreenManager.Run(canvas);
            if (unlocked)
            {
                SessionManager.MarkUnlock();
                SecurityLogger.LogEvent("INFO", "Graphical session unlocked for user " +
                    SecurityContext.CurrentUser + ".");
                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Security,
                    "BEZPIECZENSTWO",
                    "Sesja odblokowana",
                    "Dostep do pulpitu zostal przywrocony po uwierzytelnieniu.");
            }
            return unlocked;
        }

        private void ResetActivityTimer()
        {
            lastActivityTimestamp = Stopwatch.GetTimestamp();
        }

        private bool ShouldAutoLock()
        {
            int minutes = global::ZonderqOS.SystemSettings.AutoLockMinutes;
            if (minutes <= 0 || lastActivityTimestamp <= 0 || Stopwatch.Frequency <= 0)
                return false;

            long now = Stopwatch.GetTimestamp();
            long elapsed = now - lastActivityTimestamp;
            if (elapsed <= 0)
                return false;

            long threshold = Stopwatch.Frequency * 60L * minutes;
            return elapsed >= threshold;
        }

        private static bool IsSecureSessionShortcut(KeyEvent key)
        {
            if (key == null || key.Key != ConsoleKeyEx.Delete)
                return false;

            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            return control && alt;
        }

        private static bool IsLockShortcut(KeyEvent key)
        {
            if (key == null || key.Key != ConsoleKeyEx.L)
                return false;

            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            return control && alt;
        }

        private static bool IsNotificationShortcut(KeyEvent key)
        {
            if (key == null || key.Key != ConsoleKeyEx.N)
                return false;

            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            return control && alt;
        }

        private void LogoutSession()
        {
            if (!SecurityContext.IsAuthenticated)
            {
                global::ZonderqOS.NotificationService.ResetForSession();
                isRunning = false;
                return;
            }

            if (startMenu != null)
                startMenu.Visible = false;
            notificationCenter?.Close();
            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;

            lockRequested = false;
            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickFrame = -1000;

            if (applicationManager != null)
                applicationManager.CloseAll();

            UserManager.EndSession();
            global::ZonderqOS.NotificationService.ResetForSession();
            isRunning = false;
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

        private void LaunchAppCenter(int x, int y)
        {
            if (appRegistry == null)
                return;

            applicationManager.Launch(new AppCenterApp(x, y, appRegistry, null));
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
