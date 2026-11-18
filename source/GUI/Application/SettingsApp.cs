using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;
using ZonderqOS.Platform;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Main Settings hub. Rendering is deliberately allocation-free after snapshots are
    /// captured. Expensive filesystem, network and hardware queries only run on open,
    /// page change, F5 or an explicit action.
    /// </summary>
    public sealed class SettingsApp : Application
    {
        private const int PageSystem = 0;
        private const int PagePersonalization = 1;
        private const int PageDisplay = 2;
        private const int PageNetwork = 3;
        private const int PageMemory = 4;
        private const int PageSecurity = 5;
        private const int PagePower = 6;
        private const int PageAbout = 7;

        private const int SidebarWidth = 236;
        private const int SidebarItemHeight = 46;
        private const int SidebarTop = 78;
        private const int RowsTop = 142;
        private const int RowHeight = 68;
        private const int RowGap = 6;
        // Six rows must fit above the status bar on a physical 800x600
        // framebuffer; only their visual height changes, not action indices.
        private int EffectiveRowHeight => Window.Height >= 650 ? RowHeight : 50;

        private static readonly string[] PageNames =
        {
            "SYSTEM",
            "PERSONALIZATION",
            "DISPLAY AND GUI",
            "NETWORK",
            "MEMORY AND DISKS",
            "ACCOUNTS AND SECURITY",
            "POWER",
            "ABOUT"
        };

        private static readonly string[] PageSubtitles =
        {
            "Performance, tools and system devices",
            "Wallpaper, theme, desktop and taskbar options",
            "Framebuffer, time zone and rendering frequency",
            "DHCP, static IPv4, DNS and network interfaces",
            "Physical memory, OrionGC, disks, partitions and VFS",
            "Session, local accounts, audit and system logs",
            "Performance profile, restart and shutdown controls",
            "Versions, CPU, scheduler and advanced kernel panel"
        };

        private static readonly IconType[] PageIcons =
        {
            IconType.Settings,
            IconType.Start,
            IconType.Settings,
            IconType.Settings,
            IconType.Folder,
            IconType.About,
            IconType.Shutdown,
            IconType.About
        };

        private static readonly Color Surface = Color.FromArgb(24, 29, 35);
        private static readonly Color Sidebar = Color.FromArgb(21, 26, 32);
        private static readonly Color Panel = Color.FromArgb(31, 38, 46);
        private static readonly Color PanelHover = Color.FromArgb(38, 50, 61);
        private static readonly Color Border = Color.FromArgb(53, 66, 78);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        private readonly Action closeCallback;
        private readonly ApplicationManager applicationManager;
        private readonly CpuHardwareInfo cpuInfo;

        private int selectedPage;
        private int lastMouseX = -1;
        private int lastMouseY = -1;
        private string statusMessage = "GOTOWE";
        private Color statusColor = Good;

        private ulong totalPages;
        private ulong freePages;
        private ulong physicalRamMb;
        private ulong installedRamMb;
        private ulong gcHeapBytes;
        private ulong gcCommittedBytes;
        private int storageDeviceCount;
        private int partitionCount;
        private int schedulerThreadCount;
        private uint schedulerCpuCount;
        private string schedulerName = "N/A";

        private bool networkReady;
        private int networkDeviceCount;
        private string activeNetworkName = "NONE";
        private string ipAddress = "0.0.0.0";

        private string currentUser = "root";
        private int currentUid;
        private int userCount;
        private long auditLogBytes;
        private bool auditLogPresent;

        private int framebufferWidth = 1920;
        private int framebufferHeight = 1080;

        private int armedPowerAction;
        private long armedPowerTimestamp;

        private Color Accent
        {
            get { return global::ZonderqOS.GUI.SystemTheme.Accent; }
        }

        public SettingsApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Settings")
        {
            closeCallback = onClose;
            applicationManager = manager;
            Window = new Window(x, y, 1080, 720, "System Settings - ZOnderqOS");
            Window.CloseAction = Close;

            global::ZonderqOS.SystemSettings.Load();
            cpuInfo = CpuHardwareInfo.Detect();
            RefreshSnapshot();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshSnapshot();
                SetStatus("SYSTEM INFORMATION REFRESHED", Good);
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                selectedPage--;
                if (selectedPage < 0)
                    selectedPage = PageNames.Length - 1;
                RefreshSnapshot();
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                selectedPage++;
                if (selectedPage >= PageNames.Length)
                    selectedPage = 0;
                RefreshSnapshot();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            lastMouseX = mouseX;
            lastMouseY = mouseY;

            Window?.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            if (!leftClicked || leftWasClicked)
                return;

            int page = GetSidebarPageAt(mouseX, mouseY);
            if (page >= 0)
            {
                selectedPage = page;
                RefreshSnapshot();
                SetStatus("SETTINGS SECTION SELECTED", Accent);
                return;
            }

            HandlePageClick(mouseX, mouseY);
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            framebufferWidth = (int)canvas.Width;
            framebufferHeight = (int)canvas.Height;
            Window.Render(canvas);
            RenderClient(canvas);
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void RenderClient(Canvas canvas)
        {
            int clientTop = Window.Y + 39;
            int clientHeight = System.Math.Max(1, Window.Height - 40);
            int sidebarWidth = System.Math.Min(SidebarWidth, System.Math.Max(180, Window.Width - 520));
            int contentX = Window.X + sidebarWidth;
            int contentWidth = System.Math.Max(1, Window.Width - sidebarWidth);

            canvas.DrawFilledRectangle(Surface, Window.X + 1, clientTop, Window.Width - 2, clientHeight);
            canvas.DrawFilledRectangle(Sidebar, Window.X + 1, clientTop, sidebarWidth - 1, clientHeight);
            canvas.DrawLine(Border, contentX, clientTop, contentX, Window.Y + Window.Height - 2);

            RenderSidebar(canvas, sidebarWidth);
            RenderPageHeader(canvas, contentX, contentWidth);

            switch (selectedPage)
            {
                case PagePersonalization: RenderPersonalization(canvas, contentX, contentWidth); break;
                case PageDisplay: RenderDisplay(canvas, contentX, contentWidth); break;
                case PageNetwork: RenderNetwork(canvas, contentX, contentWidth); break;
                case PageMemory: RenderMemory(canvas, contentX, contentWidth); break;
                case PageSecurity: RenderSecurity(canvas, contentX, contentWidth); break;
                case PagePower: RenderPower(canvas, contentX, contentWidth); break;
                case PageAbout: RenderAbout(canvas, contentX, contentWidth); break;
                default: RenderSystem(canvas, contentX, contentWidth); break;
            }

            RenderStatusBar(canvas, contentX, contentWidth);
        }

        private void RenderSidebar(Canvas canvas, int sidebarWidth)
        {
            int x = Window.X + 1;
            SmallTextRenderer.Draw(canvas, "SETTINGS", x + 16, Window.Y + 55, Text);
            SmallTextRenderer.Draw(canvas, "SYSTEM", x + 16, Window.Y + 68, Muted);

            for (int i = 0; i < PageNames.Length; i++)
            {
                int y = Window.Y + SidebarTop + i * SidebarItemHeight;
                bool selected = i == selectedPage;
                bool hover = Hit(lastMouseX, lastMouseY, x + 8, y, sidebarWidth - 16, SidebarItemHeight - 4);
                Color bg = selected ? global::ZonderqOS.GUI.SystemTheme.AccentSoft : hover ? Color.FromArgb(31, 40, 49) : Sidebar;
                Color edge = selected ? Accent : hover ? Border : Sidebar;

                UiPainter.RoundedPanel(canvas, x + 8, y, sidebarWidth - 16,
                    SidebarItemHeight - 4, UiMetrics.TileRadius, bg, edge);
                if (selected)
                    canvas.DrawFilledRectangle(Accent, x + 8, y + 5, 3, SidebarItemHeight - 14);

                IconManager.DrawScaled(canvas, PageIcons[i], x + 18, y + 10, 20, 20);
                SmallTextRenderer.DrawClipped(canvas, PageNames[i], x + 48, y + 16,
                    System.Math.Max(50, sidebarWidth - 64), selected ? Text : Muted);
            }

            int cardY = Window.Y + Window.Height - 100;
            if (cardY > Window.Y + SidebarTop + PageNames.Length * SidebarItemHeight + 8)
            {
                UiPainter.RoundedPanel(canvas, x + 10, cardY, sidebarWidth - 20, 66,
                    UiMetrics.ButtonRadius, UiPalette.SurfaceRaised, Border);
                SmallTextRenderer.Draw(canvas, "GUI PROFILE", x + 22, cardY + 14, Muted);
                SmallTextRenderer.DrawClipped(canvas, global::ZonderqOS.SystemSettings.PerformanceProfileName,
                    x + 22, cardY + 34, System.Math.Max(40, sidebarWidth - 44), Text);
            }
        }

        private void RenderPageHeader(Canvas canvas, int contentX, int contentWidth)
        {
            int x = contentX + 24;
            int maxWidth = System.Math.Max(120, contentWidth - 48);
            UiTypography.Heading(canvas, PageNames[selectedPage], x, Window.Y + 51,
                maxWidth, UiPalette.Text);
            SmallTextRenderer.DrawClipped(canvas, PageSubtitles[selectedPage], x, Window.Y + 88, maxWidth, Muted);
            canvas.DrawLine(Border, x, Window.Y + 112, Window.X + Window.Width - 24, Window.Y + 112);
            SmallTextRenderer.Draw(canvas, "F5 REFRESH  |  ESC CLOSE  |  ARROWS: SECTIONS",
                x, Window.Y + 121, Color.FromArgb(102, 122, 139));
        }

        private void RenderSystem(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "PERFORMANCE PROFILE", "Controls GUI and telemetry refresh",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "SCHEDULER THREADS", "Active threads reported by Cosmos Gen3",
                (ulong)System.Math.Max(0, schedulerThreadCount), "");
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "DEVICE MANAGER", "Network cards, disks, active interface and partition rescan",
                "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "TASK MANAGER", "Processes, CPU, memory, scheduler and telemetry", "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.About,
                "DIAGNOSTICS", "Quick GUI, keyboard, mouse and graphics test", "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "RESTORE DEFAULTS", "Reset GUI, theme and network profile preferences", "RESET", Warning);
        }

        private void RenderPersonalization(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.About,
                "SYSTEM LANGUAGE", "English (en-US) only in Alpha 1",
                global::ZonderqOS.Localization.LocalizationManager.CurrentLanguageDisplayName, Accent);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.Start,
                "DESKTOP BACKGROUND", "PNG wallpaper, graphite or dark Cosmos navy",
                global::ZonderqOS.SystemSettings.DesktopBackgroundName, Accent);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ACCENT THEME", "Colors of active windows, Start and system shell",
                global::ZonderqOS.SystemSettings.AccentThemeName, Accent);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Folder,
                "DESKTOP ICONS", "Show application shortcuts on the desktop",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowDesktopIcons),
                global::ZonderqOS.SystemSettings.ShowDesktopIcons ? Good : Muted);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "TASKBAR DATE", "Show day and month below the clock",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowTaskbarDate),
                global::ZonderqOS.SystemSettings.ShowTaskbarDate ? Good : Muted);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "NETWORK / VOLUME STATUS", "Show network state and volume count in the tray",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowTrayStatus),
                global::ZonderqOS.SystemSettings.ShowTrayStatus ? Good : Muted);
        }

        private void RenderDisplay(Canvas canvas, int contentX, int contentWidth)
        {
            DrawResolutionRow(canvas, contentX, contentWidth, 0);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "FRAMEBUFFER", "Full Cosmos Canvas framebuffer", "32-BIT", Good);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Refresh,
                "RENDER MODE", "Normal windows update on demand", "EVENT-DRIVEN", Good);
            DrawTimeZoneRow(canvas, contentX, contentWidth, 3);
            DrawNumericRow(canvas, contentX, contentWidth, 4, IconType.Refresh,
                "IDLE HEARTBEAT", "Fallback desktop refresh interval",
                (ulong)global::ZonderqOS.SystemSettings.IdleHeartbeatFrames * 15UL, " MS");
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "GUI PROFILE", "Power saving / balanced / responsive",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
        }

        private void RenderNetwork(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "NETWORK STATUS", "ZOnderqOS TCP/IP stack status",
                networkReady ? "GOTOWA" : "OFFLINE", networkReady ? Good : Danger);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "IPv4 MODE", "DHCP or saved static profile",
                global::ZonderqOS.SystemSettings.NetworkModeName, Accent);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ACTIVE INTERFACE", "Network card used by the network stack", activeNetworkName, Text);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "CURRENT IPv4 ADDRESS", "Address of the active interface", ipAddress, Text);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "STATIC IPv4 AND DNS", "IP, subnet mask, gateway and DNS editor", "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "NETWORK INTERFACES", "Choose an active adapter in Device Manager",
                networkDeviceCount > 0 ? "OPEN" : "NONE", networkDeviceCount > 0 ? Good : Muted);
        }

        private void RenderMemory(Canvas canvas, int contentX, int contentWidth)
        {
            DrawNumericRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "DETECTED RAM", "RAM from Limine map (installed total unknown; no SMBIOS)", physicalRamMb > 0 ? physicalRamMb : PagesToMb(totalPages), " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "FREE RAM", "Currently available physical pages", PagesToMb(freePages), " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "KERNEL HEAP", "Heap range allocated for PageAllocator", PagesToMb(totalPages), " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "GC COMMITTED", "Committed managed-heap memory",
                gcCommittedBytes / 1024UL / 1024UL, " MB");
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Folder,
                "DISKS / PARTITIONS / VFS", "Block devices, partitions and mount point details",
                "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "REFRESH METRICS", "Refresh RAM, GC and storage metrics", "REFRESH", Good);

            int x = GetRowX(contentX);
            int y = GetRowY(4) + 42;
            SmallTextRenderer.Draw(canvas, "DEV/PART:", x + 52, y, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)System.Math.Max(0, storageDeviceCount), x + 108, y, Text);
            SmallTextRenderer.Draw(canvas, "/", x + 121, y, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)System.Math.Max(0, partitionCount), x + 129, y, Text);
        }

        private void RenderSecurity(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.About,
                "CURRENT SESSION", "User assigned to SecurityContext", currentUser, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.About,
                "UID", "Current user identifier", (ulong)System.Math.Max(0, currentUid), "");
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "LOCAL ACCOUNTS", "List accounts, create accounts and switch sessions safely",
                userCount > 0 ? "OPEN" : "NONE", userCount > 0 ? Good : Warning);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.File,
                "SYSTEM LOGS", "Scrollable auth.log and sysmon.log with controlled clearing",
                "OPEN", Good);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "SECURITY LOGGER", "Rotated authentication and permission event log",
                auditLogPresent ? "ACTIVE" : "NO LOG", auditLogPresent ? Good : Warning);
            DrawNumericRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "AUDIT LOG SIZE", "Click to refresh security information",
                auditLogBytes > 0 ? (ulong)auditLogBytes / 1024UL : 0UL, " KB");
        }

        private void RenderPower(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "PERFORMANCE PROFILE", "GUI profile affects refresh frequency",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Refresh,
                "GUI TELEMETRY", "Current dynamic-data refresh interval",
                (ulong)global::ZonderqOS.SystemSettings.TelemetryHeartbeatFrames * 15UL, " MS");

            bool rebootArmed = IsPowerArmed(1);
            bool shutdownArmed = IsPowerArmed(2);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Reboot,
                "RESTART SYSTEM", "First click arms the action; second click restarts",
                rebootArmed ? "CONFIRM" : "REBOOT", rebootArmed ? Warning : Accent);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Shutdown,
                "SHUT DOWN SYSTEM", "First click arms the action; second click shuts down",
                shutdownArmed ? "CONFIRM" : "SHUT DOWN", shutdownArmed ? Danger : Warning);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Close,
                "CLOSING THE GUI", "X closes this window; Start > GUI ends the desktop session",
                "SAFE", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "COSMOS POWER API", "Restart and shutdown are handled by platform HAL",
                "ACTIVE", Good);
        }

        private void RenderAbout(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Start,
                "SYSTEM", "System name and generation", "ZONDERQOS GEN3", Accent);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.About,
                "COSMOS SDK", "Version of kernel and kernel.system packages", "3.0.97", Good);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ARCHITECTURE", "Architecture detected by CPUID", cpuInfo.Architecture, Text);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "PROCESSOR", "CPU or virtual processor name", cpuInfo.Brand, Text);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "SCHEDULER", "Active Cosmos Gen3 scheduler", schedulerName, Text);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "ADVANCED KERNEL PANEL", "Scheduler, CPU time, RAM, OrionGC and uptime",
                "OPEN", Warning);
        }

        private void DrawRow(Canvas canvas, int contentX, int contentWidth, int index, IconType icon,
            string title, string description, string value, Color valueColor)
        {
            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            bool hover = Hit(lastMouseX, lastMouseY, x, y, width, RowHeight);

            int rowHeight = EffectiveRowHeight;
            UiPainter.RoundedPanel(canvas, x, y, width, rowHeight, UiMetrics.TileRadius,
                hover ? PanelHover : Panel, hover ? Accent : Border);
            if (hover)
                canvas.DrawFilledRectangle(Accent, x + 9, y + 2, width - 18, 2);

            IconManager.DrawScaled(canvas, icon, x + 14, y + 20, 24, 24);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int textWidth = System.Math.Max(80, width - valueWidth - 76);
            SmallTextRenderer.DrawClipped(canvas, title, x + 52, y + 16, textWidth, Text);
            SmallTextRenderer.DrawClipped(canvas, description, x + 52, y + 37, textWidth, Muted);

            int chipX = x + width - valueWidth - 14;
            int chipY = y + 16;
            UiPainter.RoundedPanel(canvas, chipX, chipY, valueWidth, 34,
                UiMetrics.TileRadius, UiPalette.Surface, Border);
            SmallTextRenderer.DrawCentered(canvas, value ?? string.Empty, chipX + 8, chipY + 14,
                System.Math.Max(20, valueWidth - 16), valueColor);
        }

        private void DrawNumericRow(Canvas canvas, int contentX, int contentWidth, int index, IconType icon,
            string title, string description, ulong value, string suffix)
        {
            DrawRow(canvas, contentX, contentWidth, index, icon, title, description, string.Empty, Text);
            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int chipX = x + width - valueWidth - 14;
            int numberWidth = SmallTextRenderer.WidthUInt(value);
            int suffixWidth = string.IsNullOrEmpty(suffix) ? 0 : SmallTextRenderer.Width(suffix) + 6;
            int totalWidth = numberWidth + suffixWidth;
            int drawX = chipX + System.Math.Max(8, (valueWidth - totalWidth) / 2);
            SmallTextRenderer.DrawUInt(canvas, value, drawX, y + 30, Text);
            if (!string.IsNullOrEmpty(suffix))
                SmallTextRenderer.Draw(canvas, suffix, drawX + numberWidth + 6, y + 30, Muted);
        }

        private void DrawTimeZoneRow(Canvas canvas, int contentX, int contentWidth, int index)
        {
            DrawRow(canvas, contentX, contentWidth, index, IconType.Settings,
                "TIME ZONE", "Click to shift UTC by +1h; range -12 to +14",
                string.Empty, Text);

            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int chipX = x + width - valueWidth - 14;
            int tz = global::ZonderqOS.SystemSettings.TimeZoneOffsetHours;
            int drawX = chipX + 22;
            SmallTextRenderer.Draw(canvas, "UTC", drawX, y + 30, Muted);
            drawX += 24;
            if (tz >= 0)
            {
                SmallTextRenderer.Draw(canvas, "+", drawX, y + 30, Text);
                SmallTextRenderer.DrawUInt(canvas, (ulong)tz, drawX + 6, y + 30, Text);
            }
            else
            {
                SmallTextRenderer.DrawInt(canvas, tz, drawX, y + 30, Text);
            }
        }

        private void DrawResolutionRow(Canvas canvas, int contentX, int contentWidth, int index)
        {
            DrawRow(canvas, contentX, contentWidth, index, IconType.Settings,
                "RESOLUTION", "Actual active Canvas/framebuffer dimensions",
                string.Empty, Text);

            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int chipX = x + width - valueWidth - 14;
            int w1 = SmallTextRenderer.WidthUInt((ulong)System.Math.Max(0, framebufferWidth));
            int w2 = SmallTextRenderer.WidthUInt((ulong)System.Math.Max(0, framebufferHeight));
            int total = w1 + 12 + w2;
            int drawX = chipX + System.Math.Max(8, (valueWidth - total) / 2);
            SmallTextRenderer.DrawUInt(canvas, (ulong)System.Math.Max(0, framebufferWidth), drawX, y + 30, Text);
            SmallTextRenderer.Draw(canvas, " X ", drawX + w1 + 2, y + 30, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)System.Math.Max(0, framebufferHeight), drawX + w1 + 14, y + 30, Text);
        }

        private void RenderStatusBar(Canvas canvas, int contentX, int contentWidth)
        {
            int x = contentX + 18;
            int y = Window.Y + Window.Height - 42;
            int width = System.Math.Max(100, contentWidth - 36);
            UiPainter.RoundedPanel(canvas, x, y, width, 28,
                UiMetrics.TileRadius, UiPalette.Surface, Border);
            canvas.DrawFilledRectangle(statusColor, x + 9, y + 11, 5, 5);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, x + 24, y + 11,
                System.Math.Max(40, width - 34), statusColor);
        }

        private void HandlePageClick(int mouseX, int mouseY)
        {
            int row = GetRowAt(mouseX, mouseY);
            if (row < 0)
                return;

            switch (selectedPage)
            {
                case PageSystem:
                    if (row == 0) CycleProfile();
                    else if (row == 2) LaunchSubApp(new DeviceManagerApp(Window.X + 70, Window.Y + 40));
                    else if (row == 3) LaunchTaskManager();
                    else if (row == 4) LaunchSubApp(new DiagnosticsApp(Window.X + 100, Window.Y + 70, null));
                    else if (row == 5)
                    {
                        global::ZonderqOS.SystemSettings.RestoreDefaults();
                        SetStatus("DEFAULT SETTINGS RESTORED", Warning);
                    }
                    break;

                case PagePersonalization:
                    if (row == 0)
                    {
                        // Alpha 1 is English-only. Language switching returns in Beta 1.
                        global::ZonderqOS.Localization.LocalizationManager.SetLanguage(global::ZonderqOS.Localization.Language.English, false);
                        SetStatus("ENGLISH (en-US) ONLY IN ALPHA 1", Good);
                    }
                    else if (row == 1) { global::ZonderqOS.SystemSettings.CycleDesktopBackground(); SetStatus("DESKTOP BACKGROUND CHANGED", Accent); }
                    else if (row == 2) { global::ZonderqOS.SystemSettings.CycleAccentTheme(); SetStatus("ACCENT THEME CHANGED", Accent); }
                    else if (row == 3) { global::ZonderqOS.SystemSettings.ToggleDesktopIcons(); SetStatus("DESKTOP ICONS TOGGLED", Accent); }
                    else if (row == 4) { global::ZonderqOS.SystemSettings.ToggleTaskbarDate(); SetStatus("TASKBAR DATE TOGGLED", Accent); }
                    else if (row == 5) { global::ZonderqOS.SystemSettings.ToggleTrayStatus(); SetStatus("TRAY STATUS TOGGLED", Accent); }
                    break;

                case PageDisplay:
                    if (row == 3)
                    {
                        global::ZonderqOS.SystemSettings.ShiftTimeZone(1);
                        SetStatus("TIME ZONE CHANGED", Good);
                    }
                    else if (row == 5)
                        CycleProfile();
                    break;

                case PageNetwork:
                    if (row == 1 || row == 4)
                        LaunchSubApp(new NetworkAdvancedApp(Window.X + 70, Window.Y + 40));
                    else if (row == 5)
                        LaunchSubApp(new DeviceManagerApp(Window.X + 70, Window.Y + 40));
                    break;

                case PageMemory:
                    if (row == 4)
                        LaunchSubApp(new StorageInfoApp(Window.X + 70, Window.Y + 40));
                    else if (row == 5)
                    {
                        RefreshSnapshot();
                        SetStatus("MEMORY AND DISK METRICS REFRESHED", Good);
                    }
                    break;

                case PageSecurity:
                    if (row == 2)
                        LaunchSubApp(new UserAccountsApp(Window.X + 80, Window.Y + 50));
                    else if (row == 3)
                        LaunchSubApp(new SystemLogApp(Window.X + 55, Window.Y + 25));
                    else if (row == 5)
                    {
                        RefreshSnapshot();
                        SetStatus("SECURITY INFORMATION REFRESHED", Good);
                    }
                    break;

                case PagePower:
                    if (row == 0) CycleProfile();
                    else if (row == 2) RequestPowerAction(1);
                    else if (row == 3) RequestPowerAction(2);
                    break;

                case PageAbout:
                    if (row == 5)
                        LaunchSubApp(new KernelAdvancedApp(Window.X + 60, Window.Y + 35));
                    break;
            }
        }

        private void LaunchSubApp(Application app)
        {
            if (applicationManager == null || app == null)
            {
                SetStatus("APPLICATION MANAGER UNAVAILABLE", Danger);
                return;
            }
            applicationManager.Launch(app);
            SetStatus("ADVANCED PANEL OPENED", Good);
        }

        private void LaunchTaskManager()
        {
            if (applicationManager == null)
                return;

            for (int i = 0; i < applicationManager.Applications.Count; i++)
            {
                if (applicationManager.Applications[i] is TaskManagerModernApp existing && existing.IsRunning)
                {
                    applicationManager.Activate(existing);
                    SetStatus("TASK MANAGER OPENED", Good);
                    return;
                }
            }

            applicationManager.Launch(new TaskManagerModernApp(Window.X + 40, Window.Y + 30, applicationManager, null));
            SetStatus("TASK MANAGER OPENED", Good);
        }

        private void CycleProfile()
        {
            global::ZonderqOS.SystemSettings.CyclePerformanceProfile();
            SetStatus("PERFORMANCE PROFILE CHANGED", Accent);
        }

        private void RequestPowerAction(int action)
        {
            if (IsPowerArmed(action))
            {
                if (action == 1)
                    global::ZonderqOS.SystemCore.SystemPower.Reboot("gui-reboot");
                else
                    global::ZonderqOS.SystemCore.SystemPower.Shutdown("gui-shutdown");
                return;
            }

            armedPowerAction = action;
            armedPowerTimestamp = Stopwatch.GetTimestamp();
            SetStatus(action == 1
                ? "RESTART ARMED - CLICK AGAIN TO CONFIRM"
                : "SHUTDOWN ARMED - CLICK AGAIN TO CONFIRM",
                action == 1 ? Warning : Danger);
        }

        private bool IsPowerArmed(int action)
        {
            if (armedPowerAction != action || armedPowerTimestamp <= 0 || Stopwatch.Frequency <= 0)
                return false;

            long now = Stopwatch.GetTimestamp();
            long elapsed = now - armedPowerTimestamp;
            if (elapsed < 0 || elapsed > Stopwatch.Frequency * 6L)
            {
                armedPowerAction = 0;
                armedPowerTimestamp = 0;
                return false;
            }
            return true;
        }

        private void RefreshSnapshot()
        {
            try
            {
                totalPages = PageAllocator.TotalPageCount;
                freePages = PageAllocator.FreePageCount;
                ulong physBytes = ZonderqOS.Hardware.PhysicalMemoryDetector.TotalPhysicalBytes;
                physicalRamMb = physBytes > 0 ? physBytes / (1024UL * 1024UL) : PagesToMb(totalPages);
                ulong instBytes = ZonderqOS.Hardware.PhysicalMemoryDetector.InstalledBytes;
                installedRamMb = instBytes / (1024UL * 1024UL); // 0 == unknown (no SMBIOS)
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
                physicalRamMb = 0;
                installedRamMb = 0;
            }

            try
            {
                if (CosmosGc.IsEnabled)
                {
                    gcHeapBytes = CosmosGc.GetHeapSizeBytes();
                    gcCommittedBytes = CosmosGc.GetTotalCommittedBytes();
                }
                else
                {
                    gcHeapBytes = 0;
                    gcCommittedBytes = 0;
                }
            }
            catch
            {
                gcHeapBytes = 0;
                gcCommittedBytes = 0;
            }

            try
            {
                storageDeviceCount = StorageManager.DeviceCount;
                partitionCount = StorageManager.Partitions.Count;
            }
            catch
            {
                storageDeviceCount = 0;
                partitionCount = 0;
            }

            try
            {
                schedulerThreadCount = SchedulerManager.ThreadCount;
                schedulerCpuCount = SchedulerManager.CpuCount;
                schedulerName = SchedulerManager.Current != null && !string.IsNullOrEmpty(SchedulerManager.Current.Name)
                    ? SchedulerManager.Current.Name : "N/A";
            }
            catch
            {
                schedulerThreadCount = 0;
                schedulerCpuCount = 0;
                schedulerName = "N/A";
            }

            try
            {
                networkReady = global::ZonderqOS.Network.IsReady;
                networkDeviceCount = global::ZonderqOS.Network.Devices.Count;
                activeNetworkName = global::ZonderqOS.Network.ActiveDevice != null &&
                                    !string.IsNullOrEmpty(global::ZonderqOS.Network.ActiveDevice.Name)
                    ? global::ZonderqOS.Network.ActiveDevice.Name : "NONE";
                var address = NetworkConfigManager.CurrentAddress;
                ipAddress = address != null ? address.ToString() : "0.0.0.0";
            }
            catch
            {
                networkReady = false;
                networkDeviceCount = 0;
                activeNetworkName = "NONE";
                ipAddress = "0.0.0.0";
            }

            currentUser = global::ZonderqOS.SecurityContext.CurrentUser ?? "unknown";
            currentUid = global::ZonderqOS.SecurityContext.CurrentUid;
            userCount = CountLocalUsers();
            ReadAuditStatus();
        }

        private static int CountLocalUsers()
        {
            try
            {
                const string path = "/etc/passwd";
                if (!File.Exists(path))
                    return 0;
                string[] lines = File.ReadAllLines(path);
                int count = 0;
                for (int i = 0; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        count++;
                }
                return count;
            }
            catch
            {
                return 0;
            }
        }

        private void ReadAuditStatus()
        {
            try
            {
                SecurityLogger.Flush();
                const string path = "/var/log/auth.log";
                auditLogPresent = File.Exists(path);
                auditLogBytes = auditLogPresent ? new FileInfo(path).Length : 0;
            }
            catch
            {
                auditLogPresent = false;
                auditLogBytes = 0;
            }
        }

        private int GetSidebarPageAt(int mouseX, int mouseY)
        {
            int sidebarWidth = System.Math.Min(SidebarWidth, System.Math.Max(180, Window.Width - 520));
            int x = Window.X + 9;
            int width = sidebarWidth - 16;
            for (int i = 0; i < PageNames.Length; i++)
            {
                int y = Window.Y + SidebarTop + i * SidebarItemHeight;
                if (Hit(mouseX, mouseY, x, y, width, SidebarItemHeight - 4))
                    return i;
            }
            return -1;
        }

        private int GetRowAt(int mouseX, int mouseY)
        {
            int sidebarWidth = System.Math.Min(SidebarWidth, System.Math.Max(180, Window.Width - 520));
            int contentX = Window.X + sidebarWidth;
            int contentWidth = System.Math.Max(1, Window.Width - sidebarWidth);
            int x = GetRowX(contentX);
            int width = GetRowWidth(contentX, contentWidth);
            for (int i = 0; i < 6; i++)
            {
                int y = GetRowY(i);
                if (Hit(mouseX, mouseY, x, y, width, EffectiveRowHeight))
                    return i;
            }
            return -1;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }

        private static string BoolLabel(bool value)
        {
            return value ? "WLACZONE" : "WYLACZONE";
        }

        private static ulong PagesToMb(ulong pages)
        {
            return pages * PageAllocator.PageSize / 1024UL / 1024UL;
        }

        private int GetRowX(int contentX)
        {
            return contentX + 18;
        }

        private int GetRowY(int index)
        {
            return Window.Y + RowsTop + index * (EffectiveRowHeight + RowGap);
        }

        private int GetRowWidth(int contentX, int contentWidth)
        {
            return System.Math.Max(220, contentWidth - 36);
        }

        private void SetStatus(string message, Color color)
        {
            statusMessage = string.IsNullOrEmpty(message) ? "GOTOWE" : message;
            statusColor = color;
        }
    }
}
