using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class Taskbar : Widget
    {
        public List<Widget> Children { get; } = new List<Widget>();
        public Color BackgroundColor { get; set; } = UiPalette.Surface;

        private readonly ApplicationManager applicationManager;
        private readonly Action notificationClick;
        private readonly Action clockClick;
        private readonly Button startButton;
        private int hoveredAppIndex = -1;
        private bool overflowHovered;
        private bool clockHovered;
        private bool peekHovered;
        private int firstVisibleAppIndex;
        private Application lastActiveApplication;
        private int lastActiveRunningIndex = -1;
        private int lastPageSlots = -1;
        private bool notificationHovered;
        private int cachedClockKey = -1;
        private int cachedDay = -1;
        private int cachedVolumeCount = -1;
        private long lastStoragePollTicks = -1;
        private int cachedTimeZone = int.MinValue;
        private bool cachedSecondsMode;
        private bool cachedNetworkReady;
        private bool networkStateInitialized;
        private bool volumeStateInitialized;
        private string cachedTime = "--:--";
        private string cachedDate = "--.--";
        private string cachedVolumeLabel = "VOL 0";
        public bool HasPendingUpdate { get; set; }

        private const int StartButtonWidth = 48;
        private const int AppButtonSize = 40;
        private const int AppButtonGap = 5;
        private const int SidePadding = 6;
        private const int TrayWidth = 278;
        private const int CompactTrayWidth = 132;

        // At low resolutions the full NET/VOL tray would cover the Start
        // button and leave no slots for open windows. Keep notifications and
        // the clock, but collapse optional status tiles when space is tight
        // or the user has disabled them in settings.
        private int GetTrayWidth()
        {
            // Below 240px the normal compact tray overlaps the last usable
            // app-switcher slot. Preserve the Start button and a 40px window
            // control first; show a clock only when all three can fit.
            if (Width < 178)
                return 0;
            if (Width < 240)
                return System.Math.Min(CompactTrayWidth,
                    System.Math.Max(0, Width - (StartButtonWidth + SidePadding + 6 + AppButtonSize)));

            return Width < 560 || !global::ZonderqOS.SystemSettings.ShowTrayStatus
                ? CompactTrayWidth : TrayWidth;
        }

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick, ApplicationManager manager)
            : this(screenWidth, screenHeight, height, onStartClick, manager, null, null)
        {
        }

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick,
            ApplicationManager manager, Action onNotificationClick)
            : this(screenWidth, screenHeight, height, onStartClick, manager, onNotificationClick, null)
        {
        }

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick,
            ApplicationManager manager, Action onNotificationClick, Action onClockClick)
            : base(0, screenHeight - height, screenWidth, height)
        {
            applicationManager = manager;
            notificationClick = onNotificationClick;
            clockClick = onClockClick;

            startButton = new Button(4, Y + 4, StartButtonWidth - 8, height - 8, "", onStartClick);
            startButton.BackgroundColor = Color.FromArgb(27, 34, 42);
            startButton.TextColor = Color.White;
            Children.Add(startButton);
        }

        // Share the actual Start button hitbox with the desktop overlay
        // dispatcher. A click on another taskbar control must dismiss Start,
        // but a click on Start itself must still toggle the menu normally.
        public bool IsStartButtonHit(int mouseX, int mouseY)
        {
            return startButton != null && startButton.Contains(mouseX, mouseY);
        }

        private IconType GetApplicationIcon(Application app)
        {
            if (app == null || string.IsNullOrEmpty(app.Name))
                return IconType.File;

            string name = app.Name;
            if (name.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Terminal;
            if (name.IndexOf("zdjec", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("image", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("photo", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.ImageViewer;
            if (name.IndexOf("calculator", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("kalkulator", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Calculator;
            if (name.IndexOf("paint", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Paint;
            if (name.IndexOf("network", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("siec", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Network;
            if (name.IndexOf("disk", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("dysk", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.DiskManager;
            if (name.IndexOf("hex", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.HexViewer;
            if (name.IndexOf("calendar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("kalendar", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Calendar;
            if (name.IndexOf("app center", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("centrum aplik", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.AppCenter;
            if (name.IndexOf("task", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("zadan", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Settings;
            if (name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("manager", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Folder;
            if (name.IndexOf("setting", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("ustaw", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Settings;
            if (name.IndexOf("about", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("diagnostic", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.About;
            return IconType.File;
        }

        // Reserve one taskbar slot for paging when open windows overflow.
        private static int CountRunningApps(List<Application> apps)
        {
            if (apps == null)
                return 0;

            int count = 0;
            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app != null && app.IsRunning && app.Window != null)
                    count++;
            }
            return count;
        }

        private static int RunningIndexOf(List<Application> apps, Application target)
        {
            if (apps == null || target == null)
                return -1;

            int runningIndex = 0;
            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null)
                    continue;
                if (app == target)
                    return runningIndex;
                runningIndex++;
            }
            return -1;
        }

        private int GetPageSlots(int runningCount, out bool overflow)
        {
            int available = Width - GetTrayWidth() - (StartButtonWidth + SidePadding + 6);
            int slots = System.Math.Max(0, (available + AppButtonGap) / (AppButtonSize + AppButtonGap));
            // With a single available slot, reserve it for a window
            // switcher instead of stranding every app except the first.
            // Zero app slots means the arrow itself cycles the MRU ring.
            overflow = runningCount > slots && slots >= 1;
            return overflow ? slots - 1 : slots;
        }

        private void NormalizeAppPage(int runningCount, int pageSlots, bool overflow)
        {
            if (!overflow || pageSlots <= 0)
            {
                firstVisibleAppIndex = 0;
                return;
            }

            if (firstVisibleAppIndex < 0 || firstVisibleAppIndex >= runningCount)
                firstVisibleAppIndex = 0;
            firstVisibleAppIndex = (firstVisibleAppIndex / pageSlots) * pageSlots;
        }

        // Keep render and hit-testing on the same page. Resolution changes
        // can reduce available slots without changing the active window.
        // Manual paging stays put until focus, ordering or slot count changes.
        private void SyncActivePage(List<Application> apps, Application activeApplication,
            int runningCount, int pageSlots, bool overflow)
        {
            NormalizeAppPage(runningCount, pageSlots, overflow);
            int activeRunningIndex = RunningIndexOf(apps, activeApplication);
            if (activeApplication != lastActiveApplication ||
                activeRunningIndex != lastActiveRunningIndex ||
                pageSlots != lastPageSlots)
            {
                lastActiveApplication = activeApplication;
                lastActiveRunningIndex = activeRunningIndex;
                lastPageSlots = pageSlots;
                if (overflow && pageSlots > 0 && activeRunningIndex >= 0)
                    firstVisibleAppIndex = (activeRunningIndex / pageSlots) * pageSlots;
            }
        }

        private int OverflowButtonX(int pageSlots)
        {
            return StartButtonWidth + SidePadding + 6 + pageSlots * (AppButtonSize + AppButtonGap);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateTrayCache();

            canvas.DrawFilledRectangle(Color.FromArgb(13, 17, 22), X, Y, Width, Height);
            canvas.DrawFilledRectangle(BackgroundColor, X, Y + 1, Width, Height - 1);
            canvas.DrawLine(Color.FromArgb(48, 60, 72), X, Y, X + Width, Y);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);

            // Modern Windows & Zorin OS Start Button styling
            UiPainter.RoundedPanel(canvas, startButton.X, startButton.Y, startButton.Width, startButton.Height,
                UiMetrics.ButtonRadius, startButton.IsHovered ? SystemTheme.AccentSoft : Color.FromArgb(25, 32, 40),
                startButton.IsHovered ? SystemTheme.AccentBorder : Color.FromArgb(44, 55, 66));

            // Windows & Zorin OS 4-Tile Modern Emblem
            int startEmblemX = startButton.X + (startButton.Width - 18) / 2;
            int startEmblemY = startButton.Y + (startButton.Height - 18) / 2;
            Color tile1 = startButton.IsHovered ? Color.White : SystemTheme.Accent;
            Color tile2 = startButton.IsHovered ? Color.FromArgb(220, 240, 255) : SystemTheme.AccentBorder;
            canvas.DrawFilledRectangle(tile1, startEmblemX, startEmblemY, 8, 8);
            canvas.DrawFilledRectangle(tile2, startEmblemX + 10, startEmblemY, 8, 8);
            canvas.DrawFilledRectangle(tile2, startEmblemX, startEmblemY + 10, 8, 8);
            canvas.DrawFilledRectangle(tile1, startEmblemX + 10, startEmblemY + 10, 8, 8);

            canvas.DrawLine(Color.FromArgb(48, 59, 70), StartButtonWidth + 2, Y + 8,
                StartButtonWidth + 2, Y + Height - 8);

            int appX = StartButtonWidth + SidePadding + 6;
            List<Application> apps = applicationManager != null ? applicationManager.Applications : null;
            Application activeApplication = applicationManager != null ? applicationManager.ActiveApplication : null;
            int runningCount = CountRunningApps(apps);
            bool overflow;
            int pageSlots = GetPageSlots(runningCount, out overflow);
            SyncActivePage(apps, activeApplication, runningCount, pageSlots, overflow);
            int runningIndex = 0;
            int shown = 0;

            if (apps != null)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    Application app = apps[i];
                    if (app == null || !app.IsRunning || app.Window == null)
                        continue;
                    if (runningIndex++ < firstVisibleAppIndex)
                        continue;
                    if (shown >= pageSlots)
                        break;

                    bool active = activeApplication == app;
                    bool minimized = app.Window.IsMinimized;
                    bool hovered = shown == hoveredAppIndex;

                    Color bg = active
                        ? SystemTheme.AccentSoft
                        : hovered ? Color.FromArgb(35, 45, 55) : Color.FromArgb(25, 31, 38);
                    Color border = active
                        ? SystemTheme.AccentBorder
                        : hovered ? Color.FromArgb(61, 76, 90) : Color.FromArgb(38, 47, 56);

                    UiPainter.RoundedPanel(canvas, appX, Y + 3, AppButtonSize, Height - 6,
                        UiMetrics.TileRadius, bg, border);
                    IconManager.DrawScaled(canvas, GetApplicationIcon(app), appX + 9, Y + 9, 22, 22);

                    if (active && !minimized)
                        canvas.DrawFilledRectangle(SystemTheme.Accent, appX + 10, Y + Height - 4, 20, 3);
                    else if (minimized)
                        canvas.DrawFilledRectangle(Color.FromArgb(98, 107, 116), appX + 14, Y + Height - 4, 12, 2);
                    else
                        canvas.DrawFilledRectangle(Color.FromArgb(58, 67, 76), appX + 17, Y + Height - 3, 6, 1);

                    appX += AppButtonSize + AppButtonGap;
                    shown++;
                }
            }

            if (overflow)
            {
                int arrowX = OverflowButtonX(pageSlots);
                UiPainter.RoundedPanel(canvas, arrowX, Y + 3, AppButtonSize, Height - 6,
                    UiMetrics.TileRadius,
                    overflowHovered ? SystemTheme.AccentSoft : Color.FromArgb(25, 31, 38),
                    overflowHovered ? SystemTheme.AccentBorder : Color.FromArgb(38, 47, 56));
                SmallTextRenderer.DrawCentered(canvas, ">>", arrowX, Y + 17, AppButtonSize,
                    Color.FromArgb(232, 237, 242));
            }

            RenderSystemTray(canvas);
        }

        private void UpdateTrayCache()
        {
            int timeZone = global::ZonderqOS.SystemSettings.TimeZoneOffsetHours;
            bool showSeconds = global::ZonderqOS.SystemSettings.ShowClockSeconds;
            if (timeZone != cachedTimeZone || showSeconds != cachedSecondsMode)
            {
                cachedTimeZone = timeZone;
                cachedSecondsMode = showSeconds;
                cachedClockKey = -1;
                cachedDay = -1;
            }

            DateTime currentTime = DateTime.UtcNow.AddHours(timeZone);
            int clockKey = showSeconds
                ? currentTime.Hour * 3600 + currentTime.Minute * 60 + currentTime.Second
                : currentTime.Hour * 60 + currentTime.Minute;

            if (clockKey != cachedClockKey)
            {
                cachedClockKey = clockKey;
                cachedTime = showSeconds
                    ? currentTime.ToString("HH:mm:ss")
                    : currentTime.ToString("HH:mm");
            }

            if (currentTime.DayOfYear != cachedDay)
            {
                cachedDay = currentTime.DayOfYear;
                cachedDate = currentTime.ToString("dd.MM");
            }

            bool networkReady = global::ZonderqOS.Network.IsReady;
            if (!networkStateInitialized)
            {
                cachedNetworkReady = networkReady;
                networkStateInitialized = true;
            }
            else if (networkReady != cachedNetworkReady)
            {
                cachedNetworkReady = networkReady;
                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Network,
                    "SIEC",
                    networkReady ? "Siec polaczona" : "Siec rozlaczona",
                    networkReady ? "Interfejs sieciowy jest gotowy do pracy." : "Polaczenie sieciowe nie jest obecnie gotowe.");
            }

            // The partition table can involve device/driver work. Do not
            // enumerate it on every rendered frame (typically ~60 Hz).
            // Keep the last valid value on transient storage exceptions,
            // rather than inventing a disconnect and posting a false alert.
            long nowTicks = Stopwatch.GetTimestamp();
            if (!TaskbarTelemetryGate.ShouldPoll(nowTicks, lastStoragePollTicks, Stopwatch.Frequency))
                return;
            lastStoragePollTicks = nowTicks;

            int volumeCount;
            try
            {
                volumeCount = StorageManager.Partitions.Count;
            }
            catch
            {
                // Retry on the next polling interval, not on every frame.
                return;
            }

            if (volumeCount != cachedVolumeCount)
            {
                if (volumeStateInitialized)
                {
                    global::ZonderqOS.NotificationService.Post(
                        global::ZonderqOS.NotificationKind.Storage,
                        "MAGAZYN",
                        "Zmiana woluminow",
                        "System wykryl zmiane listy partycji lub woluminow.");
                }
                cachedVolumeCount = volumeCount;
                cachedVolumeLabel = "VOL " + volumeCount;
                volumeStateInitialized = true;
            }
        }

        private void RenderSystemTray(Canvas canvas)
        {
            // At <178px even the clock would cover the window switcher.
            // The Start button and running applications take precedence.
            if (Width < 178)
                return;

            if (Width >= 240)
            {
                int trayX = Width - GetTrayWidth();
                canvas.DrawLine(Color.FromArgb(48, 59, 70), trayX, Y + 7, trayX, Y + Height - 7);
                DrawNotificationTile(canvas, trayX + 9, 34);

                if (GetTrayWidth() == TrayWidth && global::ZonderqOS.SystemSettings.ShowTrayStatus)
                {
                    bool networkReady = global::ZonderqOS.Network.IsReady;
                    DrawTrayTile(canvas, trayX + 47, 46, IconType.Network, "NET", networkReady);
                    DrawTrayTile(canvas, trayX + 97, 46, IconType.FileManager, cachedVolumeLabel, cachedVolumeCount > 0);
                    DrawBatteryTile(canvas, trayX + 147, 40);
                }
            }

            int clockX = Width - 78;
            if (Width >= 240)
            {
                canvas.DrawLine(Color.FromArgb(48, 59, 70), clockX - 10, Y + 7,
                    clockX - 10, Y + Height - 7);
                UiPainter.RoundedPanel(canvas, clockX - 4, Y + 5, 68, Height - 10,
                    UiMetrics.TileRadius,
                    clockHovered ? Color.FromArgb(36, 47, 58) : Color.FromArgb(22, 28, 35),
                    clockHovered ? SystemTheme.AccentBorder : Color.FromArgb(40, 50, 62));
            }

            if (global::ZonderqOS.SystemSettings.ShowTaskbarDate)
            {
                SmallTextRenderer.DrawCentered(canvas, cachedTime, clockX, Y + 12, 68,
                    Color.FromArgb(232, 237, 242));
                SmallTextRenderer.DrawCentered(canvas, cachedDate, clockX, Y + 27, 68,
                    Color.FromArgb(132, 148, 162));
            }
            else
            {
                SmallTextRenderer.DrawCentered(canvas, cachedTime, clockX, Y + 19, 68,
                    Color.FromArgb(232, 237, 242));
            }

            // Windows / Zorin OS "Show Desktop" (Aero Peek) button at the right screen edge
            int peekX = Width - 8;
            if (Width >= 240)
            {
                canvas.DrawLine(Color.FromArgb(48, 59, 70), peekX - 1, Y + 6, peekX - 1, Y + Height - 6);
                Color peekBg = peekHovered ? SystemTheme.AccentSoft : Color.FromArgb(16, 21, 28);
                canvas.DrawFilledRectangle(peekBg, peekX, Y + 2, 8, Height - 4);
                if (peekHovered)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, peekX + 5, Y + 6, 2, Height - 12);
            }
        }

        private void DrawNotificationTile(Canvas canvas, int x, int width)
        {
            int unread = global::ZonderqOS.NotificationService.UnreadCount;
            bool dnd = global::ZonderqOS.NotificationService.DoNotDisturb;
            Color background = notificationHovered
                ? Color.FromArgb(40, 53, 64)
                : unread > 0 ? SystemTheme.AccentSoft : Color.FromArgb(30, 35, 41);
            Color border = notificationHovered || unread > 0
                ? SystemTheme.AccentBorder
                : Color.FromArgb(49, 58, 67);

            UiPainter.RoundedPanel(canvas, x, Y + 6, width, Height - 12,
                UiMetrics.TileRadius, background, border);

            IconManager.DrawScaled(canvas, HasPendingUpdate ? IconType.Install : IconType.About, x + 8, Y + 13, 16, 16);

            if (unread > 0)
            {
                int badgeX = x + width - 12;
                int badgeY = Y + 7;
                canvas.DrawFilledRectangle(dnd ? Color.FromArgb(176, 132, 62) : SystemTheme.Accent,
                    badgeX, badgeY, 11, 11);
                if (unread < 10)
                    SmallTextRenderer.DrawUInt(canvas, (ulong)unread, badgeX + 3, badgeY + 2, Color.White);
            }
            else if (dnd)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(224, 174, 76), x + width - 7, Y + 10, 3, 3);
            }
        }

        private void DrawTrayTile(Canvas canvas, int x, int width, IconType icon, string label, bool ready)
        {
            Color background = ready ? Color.FromArgb(31, 48, 60) : Color.FromArgb(30, 35, 41);
            Color border = ready ? SystemTheme.AccentBorder : Color.FromArgb(49, 58, 67);
            Color indicator = ready ? Color.FromArgb(72, 173, 118) : Color.FromArgb(111, 119, 127);

            UiPainter.RoundedPanel(canvas, x, Y + 6, width, Height - 12,
                UiMetrics.TileRadius, background, border);
            IconManager.DrawScaled(canvas, icon, x + 6, Y + 13, 14, 14);
            SmallTextRenderer.DrawClipped(canvas, label, x + 25, Y + 18,
                System.Math.Max(8, width - 32), Color.FromArgb(207, 216, 224));
            canvas.DrawFilledRectangle(indicator, x + width - 7, Y + 10, 3, 3);
        }

        private void DrawBatteryTile(Canvas canvas, int x, int width)
        {
            Color background = Color.FromArgb(31, 48, 60);
            Color border = SystemTheme.AccentBorder;
            Color textCol = Color.FromArgb(207, 216, 224);

            UiPainter.RoundedPanel(canvas, x, Y + 6, width, Height - 12,
                UiMetrics.TileRadius, background, border);

            // Battery body (11x7 px)
            int bx = x + 5;
            int by = Y + 17;
            canvas.DrawFilledRectangle(Color.FromArgb(90, 105, 120), bx, by, 11, 7);
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 30), bx + 1, by + 1, 9, 5);
            // Positive terminal (1x3 px)
            canvas.DrawFilledRectangle(Color.FromArgb(90, 105, 120), bx + 11, by + 2, 1, 3);
            // Green charge fill (7x3 px)
            canvas.DrawFilledRectangle(Color.FromArgb(72, 173, 118), bx + 2, by + 2, 7, 3);

            // Text "AC"
            SmallTextRenderer.DrawClipped(canvas, "AC", x + 19, Y + 18,
                System.Math.Max(8, width - 20), textCol);
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] is Button btn)
                {
                    bool isOver = btn.Contains(mouseX, mouseY);
                    btn.IsHovered = isOver;
                    if (isOver && isClicked && !wasClicked)
                        btn.InvokeClick();
                }
            }

            hoveredAppIndex = -1;
            overflowHovered = false;
            notificationHovered = false;
            clockHovered = false;
            peekHovered = false;
            if (mouseY < Y || mouseY >= Y + Height)
                return;

            if (Width >= 240)
            {
                int peekX = Width - 8;
                if (mouseX >= peekX && mouseX < Width && mouseY >= Y && mouseY < Y + Height)
                {
                    peekHovered = true;
                    if (isClicked && !wasClicked)
                    {
                        applicationManager?.ToggleShowDesktop();
                        return;
                    }
                }

                int clockX = Width - 78;
                if (mouseX >= clockX - 4 && mouseX < clockX + 66 && mouseY >= Y + 5 && mouseY < Y + Height - 5)
                {
                    clockHovered = true;
                    if (isClicked && !wasClicked)
                    {
                        clockClick?.Invoke();
                        return;
                    }
                }
            }

            // The notification tile is deliberately hidden on ultra-narrow
            // desktops. Its invisible old rectangle must not steal clicks
            // from the app switcher or the clock.
            if (Width >= 240)
            {
                int trayX = Width - GetTrayWidth();
                int notificationX = trayX + 9;
                // Match the painted notification tile (Y+6..Y+Height-6).
                // Clicks on the tray separator or bottom border must not
                // open a panel that was not under the pointer.
                if (Height > 12 && mouseX >= notificationX && mouseX < notificationX + 34 &&
                    mouseY >= Y + 6 && mouseY < Y + Height - 6)
                {
                    notificationHovered = true;
                    if (isClicked && !wasClicked)
                        notificationClick?.Invoke();
                    return;
                }
            }

            // App tiles and the overflow switcher are painted at Y+3 with
            // Height-6. The taskbar's top/bottom borders are not buttons.
            // Start and notification controls have their own hitboxes above.
            if (mouseY < Y + 3 || mouseY >= Y + Height - 3)
                return;

            if (applicationManager == null)
                return;

            int appX = StartButtonWidth + SidePadding + 6;
            List<Application> apps = applicationManager.Applications;
            int runningCount = CountRunningApps(apps);
            bool overflow;
            int pageSlots = GetPageSlots(runningCount, out overflow);
            SyncActivePage(apps, applicationManager.ActiveApplication, runningCount, pageSlots, overflow);
            int runningIndex = 0;
            int shown = 0;

            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null)
                    continue;
                if (runningIndex++ < firstVisibleAppIndex)
                    continue;
                if (shown >= pageSlots)
                    break;

                if (mouseX >= appX && mouseX < appX + AppButtonSize)
                {
                    hoveredAppIndex = shown;
                    if (isClicked && !wasClicked)
                        applicationManager.ToggleMinimize(app);
                    return;
                }

                appX += AppButtonSize + AppButtonGap;
                shown++;
            }

            if (overflow)
            {
                int arrowX = OverflowButtonX(pageSlots);
                if (mouseX >= arrowX && mouseX < arrowX + AppButtonSize)
                {
                    overflowHovered = true;
                    if (isClicked && !wasClicked)
                    {
                        if (pageSlots == 0)
                        {
                            // At ultra-narrow widths (one total slot), an
                            // overflow arrow is the only usable app control.
                            // Cycle windows instead of advancing by zero.
                            applicationManager.CycleWindow(false);
                        }
                        else
                        {
                            firstVisibleAppIndex += pageSlots;
                            if (firstVisibleAppIndex >= runningCount)
                                firstVisibleAppIndex = 0;
                        }
                    }
                }
            }
        }
    }
}
