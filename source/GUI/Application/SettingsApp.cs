using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.Core.Memory;
using Cosmos.Kernel.Core.Scheduler;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using CosmosGc = Cosmos.Kernel.Core.Memory.GarbageCollector.GarbageCollector;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Central system-settings application for ZOnderqOS Gen3. The view is rendered
    /// directly into the window client area, using fixed strings and cached snapshots.
    /// Expensive filesystem/network queries only run on open, F5 or explicit actions.
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

        private static readonly string[] PageNames =
        {
            "SYSTEM",
            "PERSONALIZACJA",
            "EKRAN I GUI",
            "SIEC",
            "PAMIEC I DYSKI",
            "KONTA I OCHRONA",
            "ZASILANIE",
            "INFORMACJE"
        };

        private static readonly string[] PageSubtitles =
        {
            "Wydajnosc, scheduler i narzedzia systemowe",
            "Pulpit, zegar, data i elementy paska zadan",
            "Framebuffer, renderowanie i czestotliwosc odswiezania",
            "Interfejsy, IPv4 oraz konfiguracja DHCP",
            "Pamiec fizyczna, OrionGC i urzadzenia magazynowe",
            "Sesja uzytkownika, konta i dziennik bezpieczenstwa",
            "Profil pracy oraz kontrola restartu i wylaczenia",
            "Wersje, procesor, architektura i srodowisko uruchomieniowe"
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
        private static readonly Color Accent = Color.FromArgb(64, 143, 204);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        private readonly Action closeCallback;
        private readonly Action openTaskManager;
        private readonly Action openDiagnostics;
        private readonly CpuHardwareInfo cpuInfo;

        private int selectedPage;
        private int lastMouseX = -1;
        private int lastMouseY = -1;
        private string statusMessage = "GOTOWE";
        private Color statusColor = Good;

        private ulong totalPages;
        private ulong freePages;
        private ulong gcHeapBytes;
        private ulong gcCommittedBytes;
        private int storageDeviceCount;
        private int partitionCount;
        private int schedulerThreadCount;
        private uint schedulerCpuCount;
        private string schedulerName = "N/A";

        private bool networkReady;
        private int networkDeviceCount;
        private string activeNetworkName = "BRAK";
        private string ipAddress = "0.0.0.0";

        private string currentUser = "root";
        private int currentUid;
        private int userCount;
        private long auditLogBytes;
        private bool auditLogPresent;

        private int framebufferWidth = 1920;
        private int framebufferHeight = 1080;

        // 0 = nothing armed, 1 = reboot, 2 = shutdown.
        private int armedPowerAction;
        private long armedPowerTimestamp;

        public SettingsApp(int x, int y, Action taskManagerAction, Action diagnosticsAction, Action onClose)
            : base("Ustawienia")
        {
            closeCallback = onClose;
            openTaskManager = taskManagerAction;
            openDiagnostics = diagnosticsAction;
            Window = new Window(x, y, 1080, 720, "Ustawienia systemowe - ZOnderqOS");
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
                SetStatus("ODSWIEZONO DANE SYSTEMOWE", Good);
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
                SetStatus("WYBRANO SEKCJE USTAWIEN", Accent);
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
            int contentWidth = System.Math.Max(260, Window.Width - sidebarWidth);

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
            SmallTextRenderer.Draw(canvas, "USTAWIENIA", x + 16, Window.Y + 55, Text);
            SmallTextRenderer.Draw(canvas, "SYSTEMOWE", x + 16, Window.Y + 68, Muted);

            for (int i = 0; i < PageNames.Length; i++)
            {
                int y = Window.Y + SidebarTop + i * SidebarItemHeight;
                bool selected = i == selectedPage;
                bool hover = Hit(lastMouseX, lastMouseY, x + 8, y, sidebarWidth - 16, SidebarItemHeight - 4);
                Color bg = selected ? Color.FromArgb(37, 54, 68) : hover ? Color.FromArgb(31, 40, 49) : Sidebar;
                Color edge = selected ? Accent : hover ? Border : Sidebar;

                canvas.DrawFilledRectangle(bg, x + 8, y, sidebarWidth - 16, SidebarItemHeight - 4);
                if (selected || hover)
                    canvas.DrawRectangle(edge, x + 8, y, sidebarWidth - 16, SidebarItemHeight - 4);
                if (selected)
                    canvas.DrawFilledRectangle(Accent, x + 8, y + 5, 3, SidebarItemHeight - 14);

                IconManager.DrawScaled(canvas, PageIcons[i], x + 18, y + 10, 20, 20);
                SmallTextRenderer.DrawClipped(canvas, PageNames[i], x + 48, y + 16,
                    System.Math.Max(50, sidebarWidth - 64), selected ? Text : Muted);
            }

            int cardY = Window.Y + Window.Height - 100;
            if (cardY > Window.Y + SidebarTop + PageNames.Length * SidebarItemHeight + 8)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(27, 34, 41), x + 10, cardY, sidebarWidth - 20, 66);
                canvas.DrawRectangle(Border, x + 10, cardY, sidebarWidth - 20, 66);
                SmallTextRenderer.Draw(canvas, "PROFIL GUI", x + 22, cardY + 14, Muted);
                SmallTextRenderer.DrawClipped(canvas, global::ZonderqOS.SystemSettings.PerformanceProfileName,
                    x + 22, cardY + 34, System.Math.Max(40, sidebarWidth - 44), Text);
            }
        }

        private void RenderPageHeader(Canvas canvas, int contentX, int contentWidth)
        {
            int x = contentX + 24;
            int maxWidth = System.Math.Max(120, contentWidth - 48);
            canvas.DrawString(PageNames[selectedPage], PCScreenFont.DefaultFont, Text, x, Window.Y + 51);
            SmallTextRenderer.DrawClipped(canvas, PageSubtitles[selectedPage], x, Window.Y + 88, maxWidth, Muted);
            canvas.DrawLine(Border, x, Window.Y + 112, Window.X + Window.Width - 24, Window.Y + 112);
            SmallTextRenderer.Draw(canvas, "F5 ODSWIEZ  |  ESC ZAMKNIJ  |  STRZALKI: SEKCJE",
                x, Window.Y + 121, Color.FromArgb(102, 122, 139));
        }

        private void RenderSystem(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "PROFIL WYDAJNOSCI", "Steruje odswiezaniem GUI i telemetrii",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "WATKI SCHEDULERA", "Aktywne watki raportowane przez Cosmos Gen3", (ulong)System.Math.Max(0, schedulerThreadCount), "");
            DrawNumericRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ONLINE CPU", "Liczba CPU aktywnie obslugiwanych przez scheduler", schedulerCpuCount, "");
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "MANAGER ZADAN", "Procesy, CPU, pamiec, scheduler i telemetria", "OTWORZ", Good);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.About,
                "DIAGNOSTYKA", "Szybki test GUI, klawiatury, myszy i grafiki", "OTWORZ", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "PRZYWROC DOMYSLNE", "Reset ustawien pulpitu i profilu wydajnosci", "RESET", Warning);
        }

        private void RenderPersonalization(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Folder,
                "IKONY PULPITU", "Pokazuj skroty aplikacji na pulpicie",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowDesktopIcons),
                global::ZonderqOS.SystemSettings.ShowDesktopIcons ? Good : Muted);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "SEKUNDY NA ZEGARZE", "Dokladny zegar wymusza odswiezanie pulpitu co okolo 1 s",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowClockSeconds),
                global::ZonderqOS.SystemSettings.ShowClockSeconds ? Warning : Muted);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "DATA NA PASKU", "Pokazuj dzien i miesiac pod zegarem",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowTaskbarDate),
                global::ZonderqOS.SystemSettings.ShowTaskbarDate ? Good : Muted);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "STATUS NET / VOL", "Pokazuj stan sieci i liczbe wolumenow w zasobniku",
                BoolLabel(global::ZonderqOS.SystemSettings.ShowTrayStatus),
                global::ZonderqOS.SystemSettings.ShowTrayStatus ? Good : Muted);
            DrawTimeZoneRow(canvas, contentX, contentWidth, 4);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "PROFIL GUI", "Kliknij aby przelaczyc: oszczedny / zrownowazony / responsywny",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
        }

        private void RenderDisplay(Canvas canvas, int contentX, int contentWidth)
        {
            DrawResolutionRow(canvas, contentX, contentWidth, 0);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "FRAMEBUFFER", "Pelny bufor ekranu Cosmos Canvas", "32-BIT", Good);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Refresh,
                "TRYB RENDEROWANIA", "Normalne okna sa odswiezane zdarzeniowo", "EVENT-DRIVEN", Good);
            DrawNumericRow(canvas, contentX, contentWidth, 3, IconType.Refresh,
                "IDLE HEARTBEAT", "Okres awaryjnego odswiezenia pulpitu w milisekundach",
                (ulong)global::ZonderqOS.SystemSettings.IdleHeartbeatFrames * 15UL, " MS");
            DrawNumericRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "TELEMETRIA", "Okres odswiezania okien z aktywna telemetria",
                (ulong)global::ZonderqOS.SystemSettings.TelemetryHeartbeatFrames * 15UL, " MS");
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "PROFIL WYDAJNOSCI", "Kliknij aby zmienic czestotliwosc renderowania",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
        }

        private void RenderNetwork(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "STAN SIECI", "Stan stosu TCP/IP ZOnderqOS",
                networkReady ? "GOTOWA" : "OFFLINE", networkReady ? Good : Danger);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "INTERFEJSY", "Wykryte urzadzenia sieciowe", (ulong)System.Math.Max(0, networkDeviceCount), "");
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "AKTYWNY INTERFEJS", "Karta uzywana przez stos sieciowy", activeNetworkName, Accent);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "ADRES IPv4", "Aktualny adres otrzymany z konfiguracji sieci", ipAddress, Text);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Refresh,
                "ODNOW DHCP", "Wyslij nowy DHCP DISCOVER na aktywnym interfejsie", "URUCHOM", Warning);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "NASTEPNY INTERFEJS", "Przelacz aktywna karte i ponow konfiguracje DHCP",
                networkDeviceCount > 1 ? "PRZELACZ" : "BRAK DRUGIEGO", networkDeviceCount > 1 ? Good : Muted);
        }

        private void RenderMemory(Canvas canvas, int contentX, int contentWidth)
        {
            ulong totalMb = PagesToMb(totalPages);
            ulong freeMb = PagesToMb(freePages);
            DrawNumericRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "RAM FIZYCZNY", "Pamiec widoczna dla PageAllocator", totalMb, " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Settings,
                "RAM WOLNY", "Aktualnie wolne strony fizyczne", freeMb, " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ORIONGC HEAP", "Rozmiar zarzadzanego sterty raportowany przez GC", gcHeapBytes / 1024UL / 1024UL, " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "GC COMMITTED", "Pamiec zatwierdzona dla zarzadzanego sterty", gcCommittedBytes / 1024UL / 1024UL, " MB");
            DrawNumericRow(canvas, contentX, contentWidth, 4, IconType.Folder,
                "URZADZENIA / PARTYCJE", "Magazyn wykryty przez Cosmos StorageManager",
                (ulong)System.Math.Max(0, storageDeviceCount), " DEV");
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "ODSWIEZ POMIARY", "Ponownie pobierz RAM, GC i dane magazynowe", "ODSWIEZ", Good);

            int x = GetRowX(contentX);
            int y = GetRowY(4) + 42;
            SmallTextRenderer.Draw(canvas, "PARTYCJE:", x + 52, y, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)System.Math.Max(0, partitionCount), x + 114, y, Text);
        }

        private void RenderSecurity(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.About,
                "BIEZACA SESJA", "Uzytkownik przypisany do SecurityContext", currentUser, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.About,
                "UID", "Identyfikator biezacego uzytkownika", (ulong)System.Math.Max(0, currentUid), "");
            DrawNumericRow(canvas, contentX, contentWidth, 2, IconType.Folder,
                "KONTA LOKALNE", "Liczba wpisow w /etc/passwd", (ulong)System.Math.Max(0, userCount), "");
            DrawNumericRow(canvas, contentX, contentWidth, 3, IconType.File,
                "DZIENNIK AUDYTU", "Rozmiar /var/log/auth.log", auditLogBytes > 0 ? (ulong)auditLogBytes / 1024UL : 0UL, " KB");
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "SECURITY LOGGER", "Rotowany dziennik zdarzen uwierzytelniania i uprawnien",
                auditLogPresent ? "AKTYWNY" : "BRAK LOGU", auditLogPresent ? Good : Warning);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Refresh,
                "ODSWIEZ OCHRONE", "Ponownie odczytaj sesje, konta i stan dziennika", "ODSWIEZ", Good);
        }

        private void RenderPower(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Settings,
                "PROFIL PRACY", "Profil GUI ma wplyw na czestotliwosc odswiezania",
                global::ZonderqOS.SystemSettings.PerformanceProfileName, Accent);
            DrawNumericRow(canvas, contentX, contentWidth, 1, IconType.Refresh,
                "TELEMETRIA GUI", "Biezacy okres odswiezania danych dynamicznych",
                (ulong)global::ZonderqOS.SystemSettings.TelemetryHeartbeatFrames * 15UL, " MS");

            bool rebootArmed = IsPowerArmed(1);
            bool shutdownArmed = IsPowerArmed(2);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Reboot,
                "RESTART SYSTEMU", "Pierwsze klikniecie uzbraja akcje, drugie wykonuje restart",
                rebootArmed ? "POTWIERDZ" : "REBOOT", rebootArmed ? Warning : Accent);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Shutdown,
                "WYLACZ SYSTEM", "Pierwsze klikniecie uzbraja akcje, drugie wykonuje shutdown",
                shutdownArmed ? "POTWIERDZ" : "WYLACZ", shutdownArmed ? Danger : Warning);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Close,
                "ZAMKNIECIE GUI", "X zamyka tylko okno; Start > GUI konczy cala sesje graficzna",
                "BEZPIECZNE", Good);
            DrawRow(canvas, contentX, contentWidth, 5, IconType.Settings,
                "COSMOS POWER API", "Reboot i shutdown sa kierowane przez platform HAL",
                "AKTYWNE", Good);
        }

        private void RenderAbout(Canvas canvas, int contentX, int contentWidth)
        {
            DrawRow(canvas, contentX, contentWidth, 0, IconType.Start,
                "SYSTEM", "Nazwa i generacja srodowiska", "ZONDERQOS GEN3", Accent);
            DrawRow(canvas, contentX, contentWidth, 1, IconType.About,
                "COSMOS SDK", "Wersja pakietow kernel i kernel.system", "3.0.82", Good);
            DrawRow(canvas, contentX, contentWidth, 2, IconType.Settings,
                "ARCHITEKTURA", "Architektura wykryta przez CPUID", cpuInfo.Architecture, Text);
            DrawRow(canvas, contentX, contentWidth, 3, IconType.Settings,
                "PROCESOR", "Nazwa procesora lub procesora wirtualnego", cpuInfo.Brand, Text);
            DrawRow(canvas, contentX, contentWidth, 4, IconType.Settings,
                "SCHEDULER", "Aktywny scheduler Cosmos Gen3", schedulerName, Text);
            DrawUptimeRow(canvas, contentX, contentWidth, 5);
        }

        private void DrawRow(Canvas canvas, int contentX, int contentWidth, int index, IconType icon,
            string title, string description, string value, Color valueColor)
        {
            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            bool hover = Hit(lastMouseX, lastMouseY, x, y, width, RowHeight);

            canvas.DrawFilledRectangle(hover ? PanelHover : Panel, x, y, width, RowHeight);
            canvas.DrawRectangle(hover ? Color.FromArgb(68, 96, 120) : Border, x, y, width, RowHeight);
            if (hover)
                canvas.DrawFilledRectangle(Accent, x, y + 5, 3, RowHeight - 10);

            IconManager.DrawScaled(canvas, icon, x + 14, y + 20, 24, 24);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int textWidth = System.Math.Max(80, width - valueWidth - 76);
            SmallTextRenderer.DrawClipped(canvas, title, x + 52, y + 16, textWidth, Text);
            SmallTextRenderer.DrawClipped(canvas, description, x + 52, y + 37, textWidth, Muted);

            int chipX = x + width - valueWidth - 14;
            int chipY = y + 16;
            int chipHeight = 34;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 30, 36), chipX, chipY, valueWidth, chipHeight);
            canvas.DrawRectangle(Color.FromArgb(56, 71, 84), chipX, chipY, valueWidth, chipHeight);
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
                "STREFA CZASOWA", "Kliknij aby przesunac UTC o +1h; zakres od -12 do +14",
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
                drawX += 6;
                SmallTextRenderer.DrawUInt(canvas, (ulong)tz, drawX, y + 30, Text);
            }
            else
            {
                SmallTextRenderer.DrawInt(canvas, tz, drawX, y + 30, Text);
            }
        }

        private void DrawResolutionRow(Canvas canvas, int contentX, int contentWidth, int index)
        {
            DrawRow(canvas, contentX, contentWidth, index, IconType.Settings,
                "ROZDZIELCZOSC", "Rzeczywisty rozmiar aktywnego Canvas/framebuffer",
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

        private void DrawUptimeRow(Canvas canvas, int contentX, int contentWidth, int index)
        {
            DrawRow(canvas, contentX, contentWidth, index, IconType.Settings,
                "UPTIME", "Czas od uruchomienia monotonicznego zegara systemowego",
                string.Empty, Text);

            ulong seconds = 0;
            long frequency = Stopwatch.Frequency;
            long timestamp = Stopwatch.GetTimestamp();
            if (frequency > 0 && timestamp > 0)
                seconds = (ulong)timestamp / (ulong)frequency;

            int x = GetRowX(contentX);
            int y = GetRowY(index);
            int width = GetRowWidth(contentX, contentWidth);
            int valueWidth = System.Math.Min(188, System.Math.Max(92, width / 4));
            int chipX = x + width - valueWidth - 14;
            int numberWidth = SmallTextRenderer.WidthUInt(seconds);
            int total = numberWidth + SmallTextRenderer.Width(" S") + 6;
            int drawX = chipX + System.Math.Max(8, (valueWidth - total) / 2);
            SmallTextRenderer.DrawUInt(canvas, seconds, drawX, y + 30, Text);
            SmallTextRenderer.Draw(canvas, " S", drawX + numberWidth + 6, y + 30, Muted);
        }

        private void RenderStatusBar(Canvas canvas, int contentX, int contentWidth)
        {
            int x = contentX + 18;
            int y = Window.Y + Window.Height - 42;
            int width = System.Math.Max(100, contentWidth - 36);
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), x, y, width, 28);
            canvas.DrawRectangle(Border, x, y, width, 28);
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
                    else if (row == 3) openTaskManager?.Invoke();
                    else if (row == 4) openDiagnostics?.Invoke();
                    else if (row == 5)
                    {
                        global::ZonderqOS.SystemSettings.RestoreDefaults();
                        SetStatus("PRZYWROCONO USTAWIENIA DOMYSLNE", Warning);
                    }
                    break;

                case PagePersonalization:
                    if (row == 0)
                    {
                        global::ZonderqOS.SystemSettings.ToggleDesktopIcons();
                        SetStatus("ZMIENIONO WIDOCZNOSC IKON PULPITU", Good);
                    }
                    else if (row == 1)
                    {
                        global::ZonderqOS.SystemSettings.ToggleClockSeconds();
                        SetStatus("ZMIENIONO DOKLADNOSC ZEGARA", Warning);
                    }
                    else if (row == 2)
                    {
                        global::ZonderqOS.SystemSettings.ToggleTaskbarDate();
                        SetStatus("ZMIENIONO WIDOCZNOSC DATY", Good);
                    }
                    else if (row == 3)
                    {
                        global::ZonderqOS.SystemSettings.ToggleTrayStatus();
                        SetStatus("ZMIENIONO ZASOBNIK SYSTEMOWY", Good);
                    }
                    else if (row == 4)
                    {
                        global::ZonderqOS.SystemSettings.ShiftTimeZone(1);
                        SetStatus("ZMIENIONO STREFE CZASOWA", Good);
                    }
                    else if (row == 5)
                    {
                        CycleProfile();
                    }
                    break;

                case PageDisplay:
                    if (row == 5)
                        CycleProfile();
                    break;

                case PageNetwork:
                    if (row == 4)
                        RenewDhcp();
                    else if (row == 5)
                        SelectNextNetworkDevice();
                    break;

                case PageMemory:
                    if (row == 5)
                    {
                        RefreshSnapshot();
                        SetStatus("ODSWIEZONO POMIARY PAMIECI I DYSKOW", Good);
                    }
                    break;

                case PageSecurity:
                    if (row == 5)
                    {
                        RefreshSnapshot();
                        SetStatus("ODSWIEZONO DANE OCHRONY", Good);
                    }
                    break;

                case PagePower:
                    if (row == 0)
                        CycleProfile();
                    else if (row == 2)
                        RequestPowerAction(1);
                    else if (row == 3)
                        RequestPowerAction(2);
                    break;
            }
        }

        private void CycleProfile()
        {
            global::ZonderqOS.SystemSettings.CyclePerformanceProfile();
            SetStatus("ZMIENIONO PROFIL WYDAJNOSCI", Accent);
        }

        private void RenewDhcp()
        {
            SetStatus("DHCP: TRWA KONFIGURACJA...", Warning);
            bool ok = false;
            try
            {
                ok = global::ZonderqOS.Network.ConfigureDhcp();
            }
            catch
            {
                ok = false;
            }

            RefreshSnapshot();
            SetStatus(ok ? "DHCP ODNOWIONE" : "DHCP: BRAK POPRAWNEJ ODPOWIEDZI", ok ? Good : Danger);
        }

        private void SelectNextNetworkDevice()
        {
            try
            {
                int count = global::ZonderqOS.Network.Devices.Count;
                if (count <= 1)
                {
                    SetStatus("BRAK DRUGIEGO INTERFEJSU SIECIOWEGO", Muted);
                    return;
                }

                int current = 0;
                for (int i = 0; i < count; i++)
                {
                    if (global::ZonderqOS.Network.Devices[i] == global::ZonderqOS.Network.ActiveDevice)
                    {
                        current = i;
                        break;
                    }
                }

                int next = current + 1;
                if (next >= count)
                    next = 0;

                bool ok = global::ZonderqOS.Network.SetActiveDevice(next);
                RefreshSnapshot();
                SetStatus(ok ? "PRZELACZONO INTERFEJS SIECIOWY" : "NIE UDALO SIE SKONFIGUROWAC INTERFEJSU",
                    ok ? Good : Danger);
            }
            catch
            {
                SetStatus("BLAD PRZELACZANIA INTERFEJSU", Danger);
            }
        }

        private void RequestPowerAction(int action)
        {
            if (IsPowerArmed(action))
            {
                if (action == 1)
                    Cosmos.Kernel.System.Power.Reboot();
                else
                    Cosmos.Kernel.System.Power.Shutdown();
                return;
            }

            armedPowerAction = action;
            armedPowerTimestamp = Stopwatch.GetTimestamp();
            SetStatus(action == 1
                ? "REBOOT UZBROJONY - KLIKNIJ PONOWNIE ABY POTWIERDZIC"
                : "WYLACZENIE UZBROJONE - KLIKNIJ PONOWNIE ABY POTWIERDZIC",
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
            }
            catch
            {
                totalPages = 0;
                freePages = 0;
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
                    ? SchedulerManager.Current.Name
                    : "N/A";
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
                    ? global::ZonderqOS.Network.ActiveDevice.Name
                    : "BRAK";

                var address = NetworkConfigManager.CurrentAddress;
                ipAddress = address != null ? address.ToString() : "0.0.0.0";
            }
            catch
            {
                networkReady = false;
                networkDeviceCount = 0;
                activeNetworkName = "BRAK";
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
            int contentWidth = System.Math.Max(260, Window.Width - sidebarWidth);
            int x = GetRowX(contentX);
            int width = GetRowWidth(contentX, contentWidth);
            for (int i = 0; i < 6; i++)
            {
                int y = GetRowY(i);
                if (Hit(mouseX, mouseY, x, y, width, RowHeight))
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
            return Window.Y + RowsTop + index * (RowHeight + RowGap);
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
