using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class GuiManager
    {
        public bool Failed { get; private set; }

        private Canvas canvas;
        private Canvas wallpaperCanvas;
        private CursorDamageRenderer cursorDamageRenderer;
        private bool sceneHasToast;
        private Taskbar taskbar;
        private StartMenu startMenu;
        private NotificationCenter notificationCenter;
        private DesktopContextMenu desktopContextMenu;
        private ApplicationManager applicationManager;
        private AppRegistry appRegistry;
        private CommandPalette commandPalette;
        private DesktopShortcut[] desktopShortcuts;
        private bool isRunning = true;
        private bool lockRequested;
        private long lastActivityTimestamp;
        private int selectedShortcut = -1;
        private int lastShortcutClick = -1;
        private long lastShortcutClickTimestamp;
        private int lastShortcutClickX, lastShortcutClickY;
        private int frameCounter;

        private const int TaskbarHeight = 44;
        // Bound input dispatch per GUI iteration. An autorepeating key or a
        // constantly filled PS/2 queue must not starve pointer updates,
        // window animations, session lock checks or the framebuffer.
        // TryReadKey leaves the remaining events queued for the next frame.
        private const int MaxKeyboardEventsPerFrame = 64;

        private const string WallpaperCacheDirectory = "/root/.zonderq-wallpapers";
        private const string WallpaperCachePath = WallpaperCacheDirectory + "/wallpaper.png";
        private const string WallpaperResourceName = "Wallpapers.wallpaper.png";

        public void Run()
        {
            Failed = false;
            try
            {
                Console.WriteLine("[GUI] Inicjalizacja trybu graficznego...");
                canvas = DisplayModeSelector.OpenNativeOrBest();
                Console.WriteLine($"[GUI] Rzeczywista rozdzielczość Canvas: {canvas.Width}x{canvas.Height}");
                Console.WriteLine("[GUI] Uruchamiam pulpit...");
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                cursorDamageRenderer = new CursorDamageRenderer(canvas);

                // Settings are read once. Later GUI decisions use only primitive fields,
                // so no configuration-file IO happens in the render loop.
                global::ZonderqOS.SystemSettings.Load();

                // Notifications are session-local so messages from one authenticated user
                // never leak into the next login session.
                global::ZonderqOS.NotificationService.ResetForSession();
                // Clipboard paths may point to another user's private home
                // or a removable volume from the previous desktop session.
                // Never inherit a pending cut/copy across logins.
                FileClipboard.Clear();

                // Decode embedded PNG assets exactly once before the first GUI frame.
                // Later renders only reuse persistent raw pixel buffers.
                IconManager.Preload();

                int desktopHeight = (int)canvas.Height - TaskbarHeight;
                Window.ConfigureDesktop((int)canvas.Width, desktopHeight);
                applicationManager = new ApplicationManager();
                InitializeAppRegistry();
                // CommandPalette expects the full framebuffer height and reserves
                // the 44px taskbar itself. Passing desktopHeight deducted it twice,
                // needlessly hiding a row of search results on compact displays.
                commandPalette = new CommandPalette((int)canvas.Width, (int)canvas.Height, appRegistry);
                LoadWallpaper((int)canvas.Width, desktopHeight);
                InitializeDesktopShortcuts();
                desktopContextMenu = new DesktopContextMenu(
                    () => LaunchRegisteredApp(AppIds.Terminal),
                    () => LaunchRegisteredApp(AppIds.FileManager),
                    () => LaunchRegisteredApp(AppIds.Notepad),
                    RefreshDesktop,
                    () => LaunchRegisteredApp(AppIds.Settings));

                // Use the real framebuffer dimensions. On 800x600 the old
                // 560px menu was partly above the screen (-12px).
                int availableDesktopHeight = System.Math.Max(1, (int)canvas.Height - TaskbarHeight);
                int menuWidth = System.Math.Min(480, System.Math.Max(1, (int)canvas.Width - 16));
                int menuHeight = System.Math.Min(560, System.Math.Max(1, availableDesktopHeight - 16));
                int menuX = System.Math.Min(8, System.Math.Max(0, (int)canvas.Width - menuWidth));
                int menuY = System.Math.Max(0, availableDesktopHeight - menuHeight - 8);
                startMenu = new StartMenu(menuX, menuY, menuWidth, menuHeight);
                PopulateStartMenu();

                global::ZonderqOS.SystemCore.SystemPower.OnBeforeShutdown = RenderShutdownScreen;

                startMenu.SetPowerActions(
                    () => global::ZonderqOS.SystemCore.SystemPower.Reboot("gui-reboot"),
                    () => global::ZonderqOS.SystemCore.SystemPower.Shutdown("gui-shutdown"),
                    LogoutSession);
                startMenu.SetLockAction(RequestLockSession);

                notificationCenter = new NotificationCenter((int)canvas.Width, (int)canvas.Height, TaskbarHeight);

                taskbar = new Taskbar((int)canvas.Width, (int)canvas.Height, TaskbarHeight, () =>
                {
                    bool opening = !startMenu.Visible;
                    startMenu.Visible = opening;
                    if (opening)
                    {
                        commandPalette?.Close();
                        startMenu.ResetSearch();
                        notificationCenter?.Close();
                    }
                    if (desktopContextMenu != null)
                        desktopContextMenu.Visible = false;
                    selectedShortcut = -1;
                    lastShortcutClickTimestamp = 0;
                }, applicationManager, ToggleNotificationCenter, () => LaunchTimer(170, 75));

                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Security,
                    "SESJA",
                    "Witaj, " + SecurityContext.CurrentUser,
                    "Bezpieczna sesja ZOnderqOS zostala uruchomiona.");

                string curVer = global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentVersion;
                if (curVer != "2026.11.0" && curVer != "3")
                {
                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Success,
                        "AKTUALIZACJA",
                        "ZOnderqOS " + curVer,
                        "System jest zaktualizowany! Sprawdz nowe aplikacje i raport zmian.");
                }

                // Show Welcome App on desktop startup
                int welcomeX = System.Math.Max(20, ((int)canvas.Width - 620) / 2);
                int welcomeY = System.Math.Max(20, ((int)canvas.Height - 420) / 2);
                LaunchWelcome(welcomeX, welcomeY);

                bool previousLeftButtonState = false;
                bool previousRightButtonState = false;
                // A modal may close on mouse-down. The rest of that physical
                // hold still belongs to the modal, not the newly exposed app.
                bool modalLeftGestureOwned = false;
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
                        FileClipboard.Clear();
                        global::ZonderqOS.NotificationService.ResetForSession();
                        isRunning = false;
                        break;
                    }

                    bool keyboardActivity = false;

                    for (int processedKeys = 0;
                         processedKeys < MaxKeyboardEventsPerFrame &&
                         KeyboardManager.TryReadKey(out KeyEvent? key);
                         processedKeys++)
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;

                        if (IsSecureSessionShortcut(key))
                        {
                            commandPalette?.Close();
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

                        if (IsCommandPaletteShortcut(key))
                        {
                            ToggleCommandPalette();
                            continue;
                        }

                        if (IsScreenshotShortcut(key))
                        {
                            ScreenshotService.CaptureAndSave(canvas);
                            continue;
                        }

                        if (IsShowDesktopShortcut(key))
                        {
                            applicationManager?.ToggleShowDesktop();
                            continue;
                        }

                        if (HandleWindowShortcut(key))
                            continue;

                        if (commandPalette != null && commandPalette.Visible)
                        {
                            commandPalette.HandleKeyboard(key);
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

                        // The desktop context menu owns all keys while open.
                        // Do not let Delete/Enter/letters reach a background app.
                        if (desktopContextMenu != null && desktopContextMenu.Visible)
                        {
                            desktopContextMenu.HandleKeyboard(key);
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
                    if (!currentLeftButtonState)
                        modalLeftGestureOwned = false;

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

                    // Right-click dismisses a modal overlay without passing the same
                    // event to an underlying app (e.g. File Manager context menu).
                    // Right-button chords never activate left-button controls.
                    // Compare the previous left state against the CURRENT right
                    // state: releasing right while left stays held must not
                    // synthesize a new left click on Start, a window or a file.
                    bool leftForUi = currentLeftButtonState && !currentRightButtonState;
                    // If right is released in the same sample that left is
                    // pressed, the previous right-owned gesture still owns
                    // that left hold. Treat it as held, not a new Start/taskbar
                    // or notification click on an exposed control.
                    bool oldLeftForUi = (previousLeftButtonState ||
                        (previousRightButtonState && currentLeftButtonState)) && !currentRightButtonState;
                    bool rightPressed = currentRightButtonState && !previousRightButtonState;
                    // A second button cannot start a desktop action during an
                    // existing physical gesture. This also covers releasing
                    // right and pressing left in the same input sample: the
                    // sanitized left state otherwise looks like a fresh click.
                    bool desktopChordPress = (previousLeftButtonState || previousRightButtonState) &&
                        ((currentLeftButtonState && !previousLeftButtonState) || rightPressed);
                    if (desktopChordPress)
                    {
                        lastShortcutClick = -1;
                        lastShortcutClickTimestamp = 0;
                    }
                    bool paletteCaptured = false;
                    if (commandPalette != null && commandPalette.Visible)
                    {
                        if (rightPressed)
                        {
                            commandPalette.Close();
                            paletteCaptured = true;
                        }
                        else
                        {
                            paletteCaptured = commandPalette.HandleMouse(mouseX, mouseY,
                                leftForUi, oldLeftForUi);
                        }
                    }

                    bool notificationCaptured = false;
                    if (!paletteCaptured && notificationCenter != null)
                    {
                        if (notificationCenter.Visible && rightPressed)
                        {
                            notificationCenter.Close();
                            notificationCaptured = true;
                        }
                        else
                        {
                            notificationCaptured = notificationCenter.UpdateInteractions(mouseX, mouseY,
                                leftForUi, oldLeftForUi);
                        }
                    }

                    if (currentLeftButtonState && (paletteCaptured || notificationCaptured))
                        modalLeftGestureOwned = true;

                    if (!paletteCaptured && !notificationCaptured)
                    {
                        // Overlay menus own clicks inside their bounds. Dispatching
                        // to the application first would also click the obscured
                        // window (including destructive File Manager actions).
                        bool startMenuWasVisible = startMenu.Visible;
                        bool startMenuInside = startMenuWasVisible &&
                            mouseX >= startMenu.X && mouseX < startMenu.X + startMenu.Width &&
                            mouseY >= startMenu.Y && mouseY < startMenu.Y + startMenu.Height;
                        // Clicking outside Start dismisses it, but must not
                        // click a window or desktop shortcut underneath.
                        // Taskbar clicks still work, including Start toggling.
                        bool startMenuOutsideClick = startMenuWasVisible && !startMenuInside &&
                            ((currentLeftButtonState && !previousLeftButtonState && mouseY < taskbar.Y) ||
                             rightPressed);
                        bool startMenuCaptures = startMenuInside || startMenuOutsideClick;
                        // An already-held gesture must not keep moving a window
                        // behind Start. Only the desktop is modal: taskbar
                        // controls still receive their normal click.
                        if (startMenuWasVisible && mouseY < taskbar.Y &&
                            (currentLeftButtonState || currentRightButtonState))
                            startMenuCaptures = true;
                        if (startMenuOutsideClick)
                            startMenu.Visible = false;
                        // Taskbar app and notification buttons should dismiss an
                        // open Start menu, while the same click still reaches the
                        // taskbar. The Start button itself must retain toggle
                        // semantics; otherwise a second click would reopen it.
                        bool startMenuTaskbarDismiss = startMenuWasVisible && !startMenuInside &&
                            currentLeftButtonState && !previousLeftButtonState &&
                            mouseX >= taskbar.X && mouseX < taskbar.X + taskbar.Width &&
                            mouseY >= taskbar.Y && mouseY < taskbar.Y + taskbar.Height &&
                            !taskbar.IsStartButtonHit(mouseX, mouseY);
                        if (startMenuTaskbarDismiss)
                            startMenu.Visible = false;
                        // Handle the desktop menu before applications, regardless of
                        // whether desktop shortcuts are enabled. A click outside an
                        // open menu dismisses it without also activating a hidden
                        // window or triggering a destructive action underneath.
                        bool desktopMenuWasVisible = desktopContextMenu != null && desktopContextMenu.Visible;
                        bool desktopMenuCaptures = desktopMenuWasVisible &&
                            ((mouseX >= desktopContextMenu.X && mouseX < desktopContextMenu.X + desktopContextMenu.Width &&
                              mouseY >= desktopContextMenu.Y && mouseY < desktopContextMenu.Y + desktopContextMenu.Height) ||
                             // Capture an already-held press as well as its first
                             // edge: a menu must not resume an underlying drag.
                             currentLeftButtonState || currentRightButtonState);
                        if (desktopMenuWasVisible)
                        {
                            if (rightPressed)
                                desktopContextMenu.Visible = false;
                            else
                                desktopContextMenu.UpdateInteractions(mouseX, mouseY,
                                    leftForUi, oldLeftForUi);
                        }

                        // Snapshot ownership before a title-bar action closes
                        // or minimizes the window. Otherwise that same click
                        // could hit a desktop shortcut exposed underneath it.
                        bool windowCapturedClick =
                            ((currentLeftButtonState && !previousLeftButtonState) ||
                             (currentRightButtonState && !previousRightButtonState)) &&
                            IsPointOverWindow(mouseX, mouseY);
                        if (windowCapturedClick)
                            lastShortcutClickTimestamp = 0;
                        // An open File Manager menu is modal for the next mouse
                        // press. Route it to its owner before window focus changes,
                        // and do not replay it on the taskbar or desktop underneath.
                        FileManagerApp activeFileManager = applicationManager.ActiveApplication as FileManagerApp;
                        bool fileManagerMenuCaptures = !startMenuCaptures && !desktopMenuCaptures &&
                            // A held gesture still belongs to the open context
                            // menu, not to a window or taskbar behind it.
                            (leftForUi || currentRightButtonState) &&
                            activeFileManager != null && activeFileManager.ContextMenuVisible;
                        if (currentLeftButtonState &&
                            (startMenuCaptures || desktopMenuCaptures || fileManagerMenuCaptures))
                            modalLeftGestureOwned = true;
                        // Keep the original press for the modal itself, but
                        // never replay its remaining held frames on an app.
                        bool appLeftForUi = leftForUi && !modalLeftGestureOwned;
                        bool appOldLeftForUi = oldLeftForUi && !modalLeftGestureOwned;
                        if (fileManagerMenuCaptures)
                        {
                            // The modal menu must receive the same sanitized
                            // left state as ordinary windows. Otherwise a left
                            // press during right-release can execute a hidden
                            // Copy/Cut/Paste item as a new click.
                            activeFileManager.HandleMouse(mouseX, mouseY, leftForUi,
                                oldLeftForUi, currentRightButtonState, previousRightButtonState);
                        }
                        else if (!startMenuCaptures && !desktopMenuCaptures)
                        {
                            // The left state is filtered while right is held to avoid
                            // synthetic clicks. Pass physical previous-button ownership
                            // separately so a second button cannot retarget a drag.
                            applicationManager.HandleMouse(mouseX, mouseY, appLeftForUi, appOldLeftForUi,
                                currentRightButtonState, previousRightButtonState,
                                previousLeftButtonState || previousRightButtonState);
                        }
                        // A simultaneous right press or a File Manager menu
                        // dismissal must never invoke an underlying Start action.
                        if (!rightPressed && !fileManagerMenuCaptures)
                            startMenu.UpdateInteractions(mouseX, mouseY,
                                leftForUi, oldLeftForUi);

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

                        if (!desktopMenuCaptures && !fileManagerMenuCaptures)
                            taskbar.UpdateInteractions(mouseX, mouseY, leftForUi, oldLeftForUi);
                        // A Start-menu action may close the menu in this frame.
                        // Use the pre-dispatch capture flags so that same click
                        // cannot open a desktop shortcut beneath the old overlay.
                        if (!startMenuCaptures && !desktopMenuCaptures && !fileManagerMenuCaptures &&
                            !windowCapturedClick && !desktopChordPress)
                            UpdateDesktopInteractions(mouseX, mouseY, appLeftForUi, appOldLeftForUi,
                                currentRightButtonState, previousRightButtonState);
                    }

                    int priorMouseX = previousMouseX;
                    int priorMouseY = previousMouseY;
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
                    if (heartbeat)
                    {
                        global::ZonderqOS.SystemCore.Updates.UpdateManager.PollBackground();
                        bool pendingUpd = global::ZonderqOS.SystemCore.Updates.UpdateManager.Status == global::ZonderqOS.SystemCore.Updates.UpdateStatus.Available;
                        if (startMenu != null) startMenu.HasPendingUpdate = pendingUpd;
                        if (taskbar != null) taskbar.HasPendingUpdate = pendingUpd;
                    }

                    // A few extra frames after actual UI interaction (roughly
                    // 30 FPS). Idle desktops keep their configurable heartbeat.
                    bool animationFrame = UiAnimator.NeedsRedraw && (frameCounter % 2 == 0);
                    bool appContinuousRedraw = applicationManager.NeedsContinuousRedraw;
                    bool cursorOnly = pointerMoved && !keyboardActivity && !buttonChanged &&
                        !currentLeftButtonState && !currentRightButtonState &&
                        !heartbeat && !UiAnimator.NeedsRedraw && !appContinuousRedraw &&
                        CanMoveCursorWithoutSceneRedraw(priorMouseX, priorMouseY, mouseX, mouseY);

                    // A full frame remains mandatory for UI changes, interactions,
                    // animations, toasts, telemetry and periodic clock updates.
                    if (cursorOnly && cursorDamageRenderer != null &&
                        cursorDamageRenderer.TryMove(mouseX, mouseY))
                    {
                        // Only two 24x28 underlay rectangles were repainted
                        // in software; Canvas.Display still flushes full screen.
                    }
                    else if (keyboardActivity || pointerMoved || buttonChanged || heartbeat || animationFrame || appContinuousRedraw)
                        RenderFrame(mouseX, mouseY);

                    Thread.Sleep(15);
                }

                canvas.Clear(Color.Black);
                canvas.Display();
            }
            catch (Exception ex)
            {
                Failed = true;
                WriteMessage.WriteError($"Błąd w pętli GUI: {ex.Message}", "GUI");
            }
        }

        private void RenderFrame(int mouseX, int mouseY)
        {
            cursorDamageRenderer?.Reset();
            RenderDesktop();
            RenderDesktopShortcuts();
            applicationManager.Render(canvas);
            taskbar.Render(canvas);
            startMenu.Render(canvas);
            desktopContextMenu?.Render(canvas);
            notificationCenter?.Render(canvas);
            commandPalette?.Render(canvas);
            // Save pixels beneath the cursor only AFTER all desktop layers
            // have been drawn. Full-screen redraw invalidates the old snapshot.
            if (cursorDamageRenderer != null)
                cursorDamageRenderer.DrawAfterScene(mouseX, mouseY);
            else
                Cursor.Draw(canvas, mouseX, mouseY);
            canvas.Display();
            UiAnimator.FrameRendered();
            NotificationEntry toast;
            sceneHasToast = global::ZonderqOS.NotificationService.TryGetActiveToast(out toast);
        }

        /// <summary>
        /// Conservative fast path: the pointer must stay away from interactive
        /// hit targets AND their visual halos at both positions. No desktop
        /// widgets are allowed to change state while their paint is skipped.
        /// </summary>
        private bool CanMoveCursorWithoutSceneRedraw(int oldX, int oldY, int x, int y)
        {
            if (oldX < 0 || oldY < 0 || startMenu == null || taskbar == null ||
                applicationManager == null || sceneHasToast)
                return false;

            if (startMenu.Visible || (desktopContextMenu != null && desktopContextMenu.Visible) ||
                (notificationCenter != null && notificationCenter.Visible) ||
                (commandPalette != null && commandPalette.Visible) ||
                applicationManager.HasLiveTelemetryWindow)
                return false;

            NotificationEntry toast;
            if (global::ZonderqOS.NotificationService.TryGetActiveToast(out toast))
                return false;

            return IsQuietCursorPosition(oldX, oldY) && IsQuietCursorPosition(x, y);
        }

        private bool IsQuietCursorPosition(int x, int y)
        {
            // Keep a margin around the panel boundary, caption buttons,
            // shadows, shortcuts and taskbar. A 24x28 cursor damage box must
            // not touch controls with pointer-dependent rendering.
            const int margin = 24;
            if (x < 0 || y < 0 || x >= (int)canvas.Width ||
                y >= (int)canvas.Height - TaskbarHeight - margin)
                return false;

            var apps = applicationManager.Applications;
            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null ||
                    !app.Window.Visible || app.Window.IsMinimized)
                    continue;

                Window window = app.Window;
                if (IsNearRect(x, y, window.X, window.Y, window.Width, window.Height, margin))
                    return false;
            }

            if (desktopShortcuts != null && global::ZonderqOS.SystemSettings.ShowDesktopIcons)
            {
                for (int i = 0; i < desktopShortcuts.Length; i++)
                {
                    DesktopShortcut shortcut = desktopShortcuts[i];
                    if (IsNearRect(x, y, shortcut.X, shortcut.Y,
                        shortcut.Width, shortcut.Height, margin))
                        return false;
                }
            }

            return true;
        }

        private static bool IsNearRect(int x, int y, int left, int top,
            int width, int height, int margin)
        {
            return x >= left - margin && y >= top - margin &&
                (long)x < (long)left + width + margin &&
                (long)y < (long)top + height + margin;
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
                AppIds.ConfigManager, "Konfiguracja /etc", "Konfiguracja", "Konfiguracja",
                "Pliki /etc, nazwa hosta i uslugi w tle",
                AppCategory.System, IconType.Settings, () => LaunchConfigManager(130, 85),
                StartMenuPlacement.None, -1, -1, true));

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

            appRegistry.Register(new AppDescriptor(
                AppIds.Minesweeper, "Saper", "Saper", "Saper",
                "Klasyczna gra logiczna Saper",
                AppCategory.Games, IconType.Play, () => LaunchMinesweeper(160, 90),
                StartMenuPlacement.Tool, 6, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Snake, "Waz", "Waz", "Waz",
                "Klasyczna gra zrecznosciowa Snake",
                AppCategory.Games, IconType.Play, () => LaunchSnake(170, 95),
                StartMenuPlacement.Tool, 7, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Tetris, "Tetris", "Tetris", "Tetris",
                "Klasyczna ukladanka klockow Tetris",
                AppCategory.Games, IconType.Play, () => LaunchTetris(180, 85),
                StartMenuPlacement.Tool, 8, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Paint, "Paint", "Paint", "Paint",
                "Program graficzny do rysowania i tworzenia obrazow",
                AppCategory.Tools, IconType.Paint, () => LaunchPaint(165, 80),
                StartMenuPlacement.Tool, 9, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Screenshot, "Zrzut ekranu", "Zrzut ekranu", "Zrzut ekranu",
                "Narzedzie do przechwytywania ekranu pulpitu",
                AppCategory.Tools, IconType.ImageViewer, () => LaunchScreenshot(175, 90),
                StartMenuPlacement.Tool, 10, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.HexViewer, "Hex Viewer", "Hex Viewer", "Hex Viewer",
                "Podglad plikow w formacie szesnastkowym i ASCII",
                AppCategory.Tools, IconType.HexViewer, () => LaunchHexViewer(185, 85),
                StartMenuPlacement.Tool, 11, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.MusicPlayer, "Muzyka", "Muzyka", "Muzyka",
                "Odtwarzacz muzyki chiptune z wizualizatorem",
                AppCategory.Tools, IconType.Play, () => LaunchMusicPlayer(160, 75),
                StartMenuPlacement.Tool, 12, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Breakout, "Arkanoid", "Arkanoid", "Arkanoid",
                "Klasyczna gra zrecznosciowa Arkanoid / Breakout",
                AppCategory.Games, IconType.Play, () => LaunchBreakout(170, 65),
                StartMenuPlacement.Tool, 13, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Game2048, "2048", "2048", "2048",
                "Kultowa gra logiczna polegajaca na laczeniu liczb",
                AppCategory.Games, IconType.Play, () => LaunchGame2048(180, 60),
                StartMenuPlacement.Tool, 14, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.ThemeStudio, "Studio Motywow", "Studio Motywow", "Motywy",
                "Personalizacja motywow kolorystycznych i tla pulpitu",
                AppCategory.Tools, IconType.Settings, () => LaunchThemeStudio(150, 70),
                StartMenuPlacement.Tool, 15, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Solitaire, "Pasjans", "Pasjans", "Pasjans",
                "Klasyczna gra karciana Pasjans Klondike",
                AppCategory.Games, IconType.Play, () => LaunchSolitaire(140, 50),
                StartMenuPlacement.Tool, 16, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Demo3D, "Demoscena 3D", "Demoscena 3D", "Demoscena 3D",
                "Programowy silnik i demonstracja grafiki trojwymiarowej",
                AppCategory.Tools, IconType.Rotate, () => LaunchDemo3D(160, 55),
                StartMenuPlacement.Tool, 17, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Timer, "Zegar i Czas", "Zegar i Czas", "Zegar",
                "Zegar analogowy, stoper z okrazeniami i minutnik z alarmem",
                AppCategory.Tools, IconType.Calendar, () => LaunchTimer(170, 75),
                StartMenuPlacement.Tool, 18, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.BasicIde, "Zonderq BASIC", "Zonderq BASIC", "BASIC",
                "Zintegrowane srodowisko programistyczne i interpreter BASIC",
                AppCategory.Tools, IconType.Terminal, () => LaunchBasicIde(130, 60),
                StartMenuPlacement.Tool, 19, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.SpaceInvaders, "Space Invaders", "Space Invaders", "Space Invaders",
                "Retro zrecznosciowa gra kosmiczna z bunkrami i dzwiekami",
                AppCategory.Games, IconType.Play, () => LaunchSpaceInvaders(150, 60),
                StartMenuPlacement.Tool, 20, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.PassVault, "Sejf Hasel", "Sejf Hasel", "Sejf Hasel",
                "Menedzer hasel, generator i bezpieczny sejf notatek z kodem PIN",
                AppCategory.Tools, IconType.Settings, () => LaunchPassVault(160, 65),
                StartMenuPlacement.Tool, 21, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.SysBenchmark, "Benchmark Systemu", "Benchmark Systemu", "Benchmark",
                "Kompleksowy test wydajnosci CPU, RAM, grafiki VESA oraz specyfikacja sprzetu",
                AppCategory.Tools, IconType.About, () => LaunchSysBenchmark(140, 55),
                StartMenuPlacement.Tool, 22, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.SpriteStudio, "Studio Sprite'ow", "Studio Sprite'ow", "Sprite Studio",
                "Edytor pixel-art 16x16 z paleta VGA i eksportem do C# oraz BASIC DATA",
                AppCategory.Tools, IconType.Paint, () => LaunchSpriteStudio(150, 60),
                StartMenuPlacement.Tool, 23, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.SecurityCenter, "Centrum Bezpieczenstwa", "Centrum Bezpieczenstwa", "Bezpieczenstwo",
                "Ochrona anty-tamper, integralnosc plikow, zapora sieciowa i audyt",
                AppCategory.System, IconType.Settings, () => LaunchSecurityCenter(135, 50),
                StartMenuPlacement.Tool, 24, -1, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.SystemUpdater, "Aktualizacje Systemu", "Aktualizacje", "Aktualizacje",
                "Menedzer aktualizacji OS, kanaly dystrybucji, weryfikacja SHA-256 i rollback",
                AppCategory.System, IconType.Install, () => LaunchSystemUpdater(130, 45),
                StartMenuPlacement.Tool, 25, 11, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.QuickNotes, "Szybkie Notatki", "Szybkie Notatki", "Notatki",
                "Podreczny notatnik i zolte karteczki na pulpicie z autozapisem",
                AppCategory.Tools, IconType.FileDoc, () => LaunchQuickNotes(160, 90),
                StartMenuPlacement.Tool, 26, 12, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Chiptune, "Chiptune Studio", "Chiptune Studio", "Chiptune",
                "Syntezator audio 8-bit, pianino retro i kompozytor melodii PC Speaker",
                AppCategory.Tools, IconType.Play, () => LaunchChiptune(150, 60),
                StartMenuPlacement.Tool, 27, 13, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Battleship, "Statki", "Statki", "Statki",
                "Klasyczna strategiczna gra w bitwe morska z taktycznym komputerowym AI",
                AppCategory.Games, IconType.Play, () => LaunchBattleship(140, 50),
                StartMenuPlacement.Tool, 28, 14, true));

            appRegistry.Register(new AppDescriptor(
                AppIds.Welcome, "Witamy", "Witamy", "Witamy",
                "Centrum powitalne i przewodnik po wydaniu ZOnderqOS 1.0 Alpha",
                AppCategory.System, IconType.About, () => LaunchWelcome(120, 35),
                StartMenuPlacement.Tool, 29, 15, true));
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

                    // Each shortcut is 92x86. The former fixed nine rows
                    // placed icons below the taskbar at 800x600 and 1366x768.
                    // Derive rows from the actual usable framebuffer height.
                    const int shortcutTop = 22;
                    const int shortcutHeight = 86;
                    const int shortcutRowStride = 94;
                    int usableHeight = System.Math.Max(1, (int)canvas.Height - TaskbarHeight);
                    int rowsPerColumn = System.Math.Max(1,
                        (usableHeight - shortcutTop - shortcutHeight) / shortcutRowStride + 1);
                    int column = target / rowsPerColumn;
                    int row = target % rowsPerColumn;
                    int x = 20 + column * 100;
                    int y = shortcutTop + row * shortcutRowStride;
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

        private void ToggleCommandPalette()
        {
            if (commandPalette == null)
                return;

            if (commandPalette.Visible)
            {
                commandPalette.Close();
                return;
            }

            if (startMenu != null)
                startMenu.Visible = false;
            notificationCenter?.Close();
            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;
            selectedShortcut = -1;
            lastShortcutClickTimestamp = 0;
            commandPalette.Show();
        }

        private void RefreshDesktop()
        {
            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickTimestamp = 0;

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
                commandPalette?.Close();
                if (startMenu != null)
                    startMenu.Visible = false;
                if (desktopContextMenu != null)
                    desktopContextMenu.Visible = false;
                selectedShortcut = -1;
                lastShortcutClickTimestamp = 0;
            }
            notificationCenter.Toggle();
        }

        private void RequestLockSession()
        {
            if (!SecurityContext.IsAuthenticated)
                return;

            commandPalette?.Close();
            if (startMenu != null)
                startMenu.Visible = false;
            notificationCenter?.Close();
            if (desktopContextMenu != null)
                desktopContextMenu.Visible = false;

            selectedShortcut = -1;
            lastShortcutClick = -1;
            lastShortcutClickTimestamp = 0;
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

        private static bool IsCommandPaletteShortcut(KeyEvent key)
        {
            if (key == null || key.Key != ConsoleKeyEx.Spacebar)
                return false;

            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            return alt && !control;
        }

        private static bool IsScreenshotShortcut(KeyEvent key)
        {
            if (key == null)
                return false;

            if (key.Key == ConsoleKeyEx.PrintScreen)
                return true;

            if (key.Key == ConsoleKeyEx.S)
            {
                bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
                bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
                return control && alt;
            }

            return false;
        }

        private static bool IsShowDesktopShortcut(KeyEvent key)
        {
            if (key == null)
                return false;

            if (key.Key == ConsoleKeyEx.F12)
                return true;

            if (key.Key == ConsoleKeyEx.D)
            {
                bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
                bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
                return (control && alt) || (alt && !control);
            }

            return false;
        }

        /// <summary>
        /// Desktop shortcuts run before application key handlers. They must not
        /// leak into a text editor or a terminal, and must respect visible overlays.
        /// Cosmos 3.0.94 delivers modifier state on KeyEvent (key-down).
        /// </summary>
        private bool HandleWindowShortcut(KeyEvent key)
        {
            if (key == null)
                return false;

            bool alt = (key.Modifiers & ConsoleModifiers.Alt) == ConsoleModifiers.Alt;
            bool control = (key.Modifiers & ConsoleModifiers.Control) == ConsoleModifiers.Control;
            if (!alt || control)
                return false;

            if (key.Key == ConsoleKeyEx.Tab)
            {
                // Keyboard focus changes invalidate pending desktop double-clicks.
                lastShortcutClickTimestamp = 0;
                // A window switch always returns to the desktop, including
                // when Start, notifications, or the command palette were open.
                commandPalette?.Close();
                notificationCenter?.Close();
                if (startMenu != null)
                    startMenu.Visible = false;
                if (desktopContextMenu != null)
                    desktopContextMenu.Visible = false;

                bool reverse = (key.Modifiers & ConsoleModifiers.Shift) == ConsoleModifiers.Shift;
                applicationManager?.CycleWindow(reverse);
                return true;
            }

            if (key.Key != ConsoleKeyEx.F4)
                return false;

            lastShortcutClickTimestamp = 0;
            // Alt+F4 first dismisses whichever transient panel currently owns
            // keyboard focus. The underlying app must not close accidentally.
            if (commandPalette != null && commandPalette.Visible)
                commandPalette.Close();
            else if (notificationCenter != null && notificationCenter.Visible)
                notificationCenter.Close();
            else if (startMenu != null && startMenu.Visible)
                startMenu.Visible = false;
            else if (desktopContextMenu != null && desktopContextMenu.Visible)
                desktopContextMenu.Visible = false;
            else
            {
                // The File Manager owns its create-name dialog and context
                // menu. Alt+F4 dismisses that transient UI first; only the
                // next Alt+F4 requests the application's normal close action.
                FileManagerApp fileManager = applicationManager?.ActiveApplication as FileManagerApp;
                if (fileManager == null || !fileManager.DismissTransientUi())
                    applicationManager?.RequestCloseActiveWindow();
            }

            return true;
        }

        private void LogoutSession()
        {
            // Also runs for already-expired sessions. Do not retain a stale
            // cut operation that the next authenticated user could paste.
            FileClipboard.Clear();
            commandPalette?.Close();
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
            lastShortcutClickTimestamp = 0;

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

        private void LaunchConfigManager(int x, int y)
        {
            applicationManager.Launch(new ConfigManagerApp(x, y,
                path => LaunchNotepad(155, 95, path)));
        }

        private void LaunchDiagnostics(int x, int y)
        {
            applicationManager.Launch(new DiagnosticsApp(x, y, null));
        }

        private void LaunchAbout(int x, int y)
        {
            applicationManager.Launch(new AboutApp(x, y, null));
        }

        private void LaunchMinesweeper(int x, int y)
        {
            applicationManager.Launch(new MinesweeperApp(x, y, null));
        }

        private void LaunchSnake(int x, int y)
        {
            applicationManager.Launch(new SnakeApp(x, y, null));
        }

        private void LaunchTetris(int x, int y)
        {
            applicationManager.Launch(new TetrisApp(x, y, null));
        }

        private void LaunchPaint(int x, int y)
        {
            applicationManager.Launch(new PaintApp(x, y, null));
        }

        private void LaunchScreenshot(int x, int y)
        {
            applicationManager.Launch(new ScreenshotApp(x, y, () => canvas, path => LaunchImageViewer(155, 92, path), null));
        }

        private void LaunchHexViewer(int x, int y)
        {
            applicationManager.Launch(new HexViewerApp(x, y, null));
        }

        private void LaunchMusicPlayer(int x, int y)
        {
            applicationManager.Launch(new MusicPlayerApp(x, y, null));
        }

        private void LaunchBreakout(int x, int y)
        {
            applicationManager.Launch(new BreakoutApp(x, y, null));
        }

        private void LaunchGame2048(int x, int y)
        {
            applicationManager.Launch(new Game2048App(x, y, null));
        }

        private void LaunchThemeStudio(int x, int y)
        {
            applicationManager.Launch(new ThemeStudioApp(x, y, null));
        }

        private void LaunchSolitaire(int x, int y)
        {
            applicationManager.Launch(new SolitaireApp(x, y, null));
        }

        private void LaunchDemo3D(int x, int y)
        {
            applicationManager.Launch(new Demo3DApp(x, y, null));
        }

        private void LaunchTimer(int x, int y)
        {
            applicationManager.Launch(new TimerApp(x, y, null));
        }

        private void LaunchBasicIde(int x, int y)
        {
            applicationManager.Launch(new BasicIdeApp(x, y, null));
        }

        private void LaunchSpaceInvaders(int x, int y)
        {
            applicationManager.Launch(new SpaceInvadersApp(x, y, null));
        }

        private void LaunchPassVault(int x, int y)
        {
            applicationManager.Launch(new PassVaultApp(x, y, null));
        }

        private void LaunchSysBenchmark(int x, int y)
        {
            applicationManager.Launch(new SysBenchmarkApp(x, y, null));
        }

        private void LaunchSpriteStudio(int x, int y)
        {
            applicationManager.Launch(new SpriteStudioApp(x, y, null));
        }

        private void LaunchSecurityCenter(int x, int y)
        {
            applicationManager.Launch(new SecurityCenterApp(x, y, null));
        }

        private void LaunchSystemUpdater(int x, int y)
        {
            applicationManager.Launch(new SystemUpdaterApp(x, y, null));
        }

        private void LaunchQuickNotes(int x, int y)
        {
            applicationManager.Launch(new QuickNotesApp(x, y, null));
        }

        private void LaunchChiptune(int x, int y)
        {
            applicationManager.Launch(new ChiptuneApp(x, y, null));
        }

        private void LaunchBattleship(int x, int y)
        {
            applicationManager.Launch(new BattleshipApp(x, y, null));
        }

        private void LaunchWelcome(int x, int y)
        {
            applicationManager.Launch(new WelcomeApp(x, y, null, (appId) => LaunchRegisteredApp(appId)));
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
                    lastShortcutClickTimestamp = 0;
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
            {
                lastShortcutClickTimestamp = 0;
                return;
            }

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
                lastShortcutClickTimestamp = 0;
                return;
            }

            // Frame counts do not measure elapsed time during disk stalls.
            // Two distant clicks on the same shortcut must not launch it.
            long now = Stopwatch.GetTimestamp();
            long elapsed = now - lastShortcutClickTimestamp;
            bool doubleClick = selectedShortcut == hitIndex && lastShortcutClick == hitIndex &&
                lastShortcutClickTimestamp > 0 && elapsed >= 0 &&
                elapsed <= Stopwatch.Frequency / 2 &&
                System.Math.Abs(mouseX - lastShortcutClickX) <= 12 &&
                System.Math.Abs(mouseY - lastShortcutClickY) <= 12;

            selectedShortcut = hitIndex;
            lastShortcutClick = hitIndex;
            lastShortcutClickTimestamp = now;
            lastShortcutClickX = mouseX;
            lastShortcutClickY = mouseY;

            if (doubleClick)
            {
                // Consume the pair before launching (which may perform IO).
                selectedShortcut = -1;
                lastShortcutClick = -1;
                lastShortcutClickTimestamp = 0;
                desktopShortcuts[hitIndex].Open();
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

        private bool IsDesktopOccluded()
        {
            if (applicationManager == null)
                return false;

            var apps = applicationManager.Applications;
            for (int i = apps.Count - 1; i >= 0; i--)
            {
                Application app = apps[i];
                if (app != null && app.IsRunning && app.Window != null &&
                    app.Window.Visible && !app.Window.IsMinimized && app.Window.IsMaximized)
                    return true;
            }
            return false;
        }

        private void RenderDesktopShortcuts()
        {
            if (desktopShortcuts == null || !global::ZonderqOS.SystemSettings.ShowDesktopIcons)
                return;

            if (IsDesktopOccluded())
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
            if (IsDesktopOccluded())
                return;

            int deskW = (int)canvas.Width;
            int deskH = (int)canvas.Height - TaskbarHeight;

            int mode = global::ZonderqOS.SystemSettings.DesktopBackgroundMode;
            if (mode == 0 && wallpaperCanvas != null)
            {
                canvas.DrawCanvas(wallpaperCanvas, 0, 0);
            }
            else
            {
                Color background = mode switch
                {
                    1 => Color.FromArgb(25, 30, 36),     // Grafit
                    2 => Color.FromArgb(17, 31, 47),     // Cosmos Navy
                    3 => Color.FromArgb(8, 10, 14),      // OLED Midnight
                    4 => Color.FromArgb(0, 128, 128),    // Retro 95 Teal
                    5 => Color.FromArgb(10, 26, 18),     // Cyber Matrix
                    6 => Color.FromArgb(22, 36, 48),     // Nordic Glacier
                    _ => Color.FromArgb(17, 31, 47)
                };
                canvas.DrawFilledRectangle(background, 0, 0, deskW, deskH);
            }

            // Desktop Watermark & OS Build Status Badge
            string curVer = global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentVersion;
            string curBuild = global::ZonderqOS.SystemCore.Updates.UpdateManager.CurrentBuild;

            SmallTextRenderer.Draw(canvas, "ZOnderqOS 1.0 Alpha • Build " + curBuild + " [ALFA 1]", deskW - 275, deskH - 34, Color.FromArgb(70, 225, 120));
            SmallTextRenderer.Draw(canvas, "Wersja " + curVer + " • Cosmos 3.0.95 x64 Native", deskW - 275, deskH - 18, Color.FromArgb(170, 195, 220));
        }

        private void RenderShutdownScreen()
        {
            try
            {
                if (canvas != null)
                {
                    int w = (int)canvas.Width;
                    int h = (int)canvas.Height;
                    canvas.Clear(Color.FromArgb(12, 16, 22));

                    int boxW = System.Math.Min(600, w - 40);
                    int boxH = 210;
                    int boxX = (w - boxW) / 2;
                    int boxY = (h - boxH) / 2;

                    UiPainter.RoundedPanel(canvas, boxX, boxY, boxW, boxH, 8,
                        Color.FromArgb(20, 26, 36), Color.FromArgb(45, 145, 235));

                    SmallTextRenderer.DrawCentered(canvas, "SYSTEM ZONDERQOS ZOSTAL WYLACZONY",
                        boxX, boxY + 28, boxW, Color.WhiteSmoke);

                    SmallTextRenderer.DrawCentered(canvas, "Wszystkie dane zostaly bezpiecznie zapisane na nosniku USB.",
                        boxX, boxY + 68, boxW, Color.FromArgb(135, 150, 168));

                    SmallTextRenderer.DrawCentered(canvas, "Twoj komputer moze byc teraz bezpiecznie wylaczony.",
                        boxX, boxY + 92, boxW, Color.FromArgb(135, 150, 168));

                    SmallTextRenderer.DrawCentered(canvas, "Przytrzymaj przycisk ZASILANIA na laptopie, aby odciac prad.",
                        boxX, boxY + 122, boxW, Color.FromArgb(55, 215, 105));

                    SmallTextRenderer.DrawCentered(canvas, "[ R ]  Nacisnij klawisz R, aby zrestartowac komputer",
                        boxX, boxY + 160, boxW, Color.FromArgb(245, 195, 45));

                    canvas.Display();
                }
            }
            catch { }
        }
    }
}
