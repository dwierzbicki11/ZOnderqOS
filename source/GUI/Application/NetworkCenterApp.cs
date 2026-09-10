using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Network.Config;
using Cosmos.Kernel.System.Network.IPv4;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Standalone network control center for ZOnderqOS. It intentionally refreshes
    /// hardware/configuration snapshots only on open, F5 or explicit user actions.
    /// Stable rendering therefore reuses cached strings and fixed arrays instead of
    /// allocating telemetry objects on every GUI frame.
    /// </summary>
    public sealed class NetworkCenterApp : Application
    {
        private const int MaxDevices = 8;
        private const int DeviceRowHeight = 58;
        private const int DeviceGap = 7;
        private const int ActionCount = 4;
        private const string TestDomain = "example.com";

        private static readonly string[] ActionLabels =
        {
            "ODSWIEZ",
            "ODNOW DHCP",
            "TEST DNS",
            "KONFIGURACJA"
        };

        private static readonly IconType[] ActionIcons =
        {
            IconType.Refresh,
            IconType.Ethernet,
            IconType.Network,
            IconType.Settings
        };

        private static readonly Color Surface = Color.FromArgb(19, 24, 30);
        private static readonly Color Header = Color.FromArgb(25, 32, 39);
        private static readonly Color Panel = Color.FromArgb(29, 36, 44);
        private static readonly Color PanelHover = Color.FromArgb(38, 50, 61);
        private static readonly Color Border = Color.FromArgb(55, 68, 80);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        private readonly ApplicationManager applicationManager;
        private readonly Action closeCallback;

        private readonly string[] deviceNames = new string[MaxDevices];
        private readonly string[] deviceMacs = new string[MaxDevices];
        private readonly bool[] deviceLinkUp = new bool[MaxDevices];
        private readonly bool[] deviceReady = new bool[MaxDevices];

        private int deviceCount;
        private int selectedDeviceIndex = -1;
        private int activeDeviceIndex = -1;
        private int hoveredDeviceIndex = -1;
        private int hoveredActionIndex = -1;
        private int lastMouseX = -1;
        private int lastMouseY = -1;

        private string activeName = "BRAK";
        private string activeMac = "--";
        private string activeIp = "0.0.0.0";
        private string subnetMask = "0.0.0.0";
        private string defaultGateway = "0.0.0.0";
        private string dnsServer = "--";
        private string networkMode = "--";
        private string dnsTestResult = "NIE TESTOWANO";
        private string statusMessage = "GOTOWE";
        private bool activeLinkUp;
        private bool activeReady;
        private bool stackReady;
        private bool dnsTestOk;

        public NetworkCenterApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Centrum sieci")
        {
            applicationManager = manager;
            closeCallback = onClose;
            Window = new Window(x, y, 980, 690, "Centrum sieci - ZOnderqOS");
            Window.CloseAction = Close;
            RefreshSnapshot();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
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
                SetStatus("ODSWIEZONO STAN SIECI");
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                SelectDevice(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                SelectDevice(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                ActivateSelectedDevice();
                return;
            }

            char ch = key.KeyChar;
            if (ch == 'd' || ch == 'D')
            {
                RenewDhcp();
                return;
            }
            if (ch == 't' || ch == 'T')
            {
                TestDns();
                return;
            }
            if (ch == 'c' || ch == 'C')
                OpenAdvancedConfiguration();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            lastMouseX = mouseX;
            lastMouseY = mouseY;
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);

            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredDeviceIndex = -1;
                hoveredActionIndex = -1;
                return;
            }

            hoveredDeviceIndex = HitDevice(mouseX, mouseY);
            hoveredActionIndex = HitAction(mouseX, mouseY);

            if (!leftClicked || leftWasClicked)
                return;

            if (hoveredDeviceIndex >= 0)
            {
                selectedDeviceIndex = hoveredDeviceIndex;
                ActivateSelectedDevice();
                return;
            }

            if (hoveredActionIndex >= 0)
                InvokeAction(hoveredActionIndex);
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 12;
            int contentY = Window.Y + 43;
            int contentWidth = System.Math.Max(520, Window.Width - 24);
            int contentHeight = System.Math.Max(420, Window.Height - 55);

            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);
            RenderHeader(canvas, contentX, contentY, contentWidth);

            int leftWidth = System.Math.Min(300, System.Math.Max(230, contentWidth / 3));
            int leftX = contentX + 8;
            int panelTop = contentY + 68;
            int footerY = contentY + contentHeight - 32;
            int actionTop = footerY - 70;
            int leftHeight = System.Math.Max(240, actionTop - panelTop - 8);

            RenderDevicePanel(canvas, leftX, panelTop, leftWidth, leftHeight);

            int rightX = leftX + leftWidth + 12;
            int rightWidth = System.Math.Max(240, contentX + contentWidth - 8 - rightX);
            RenderConnectionPanel(canvas, rightX, panelTop, rightWidth, 104);
            RenderConfigurationPanel(canvas, rightX, panelTop + 116, rightWidth,
                System.Math.Max(180, actionTop - (panelTop + 116) - 10));
            RenderActions(canvas, rightX, actionTop, rightWidth, 58);
            RenderStatus(canvas, contentX + 8, footerY, contentWidth - 16);
        }

        private void RenderHeader(Canvas canvas, int x, int y, int width)
        {
            canvas.DrawFilledRectangle(Header, x + 1, y + 1, width - 2, 56);
            canvas.DrawFilledRectangle(SystemTheme.Accent, x + 1, y + 1, 4, 56);
            IconManager.DrawScaled(canvas, IconType.Network, x + 16, y + 13, 30, 30);
            SmallTextRenderer.Draw(canvas, "CENTRUM SIECI", x + 58, y + 16, Text);
            SmallTextRenderer.Draw(canvas, "F5 ODSWIEZ  |  D DHCP  |  T DNS  |  C KONFIGURACJA",
                x + 58, y + 34, Muted);
        }

        private void RenderDevicePanel(Canvas canvas, int x, int y, int width, int height)
        {
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "INTERFEJSY", x + 12, y + 14, Text);
            SmallTextRenderer.DrawUInt(canvas, (ulong)deviceCount, x + width - 28, y + 14, Muted);

            int rowY = y + 38;
            if (deviceCount == 0)
            {
                IconManager.DrawScaled(canvas, IconType.Network, x + 16, rowY + 16, 28, 28);
                SmallTextRenderer.Draw(canvas, "BRAK URZADZEN SIECIOWYCH", x + 54, rowY + 23, Danger);
                return;
            }

            int maxVisible = System.Math.Min(deviceCount,
                System.Math.Max(1, (height - 48) / (DeviceRowHeight + DeviceGap)));

            for (int i = 0; i < maxVisible; i++)
            {
                bool active = i == activeDeviceIndex;
                bool selected = i == selectedDeviceIndex;
                bool hover = i == hoveredDeviceIndex;
                Color background = active
                    ? SystemTheme.AccentSoft
                    : hover ? PanelHover : Color.FromArgb(24, 30, 36);
                Color border = selected || active ? SystemTheme.AccentBorder : Border;

                canvas.DrawFilledRectangle(background, x + 8, rowY, width - 16, DeviceRowHeight);
                canvas.DrawRectangle(border, x + 8, rowY, width - 16, DeviceRowHeight);
                if (active)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x + 8, rowY, 3, DeviceRowHeight);

                IconType icon = deviceLinkUp[i] ? IconType.Ethernet : IconType.Network;
                IconManager.DrawScaled(canvas, icon, x + 18, rowY + 16, 24, 24);
                SmallTextRenderer.DrawClipped(canvas, deviceNames[i] ?? "INTERFEJS", x + 52, rowY + 12,
                    System.Math.Max(40, width - 74), Text);
                SmallTextRenderer.DrawClipped(canvas, deviceMacs[i] ?? "--", x + 52, rowY + 31,
                    System.Math.Max(40, width - 112), Muted);

                Color indicator = deviceLinkUp[i] && deviceReady[i] ? Good :
                    deviceLinkUp[i] ? Warning : Danger;
                canvas.DrawFilledRectangle(indicator, x + width - 30, rowY + 27, 7, 7);

                rowY += DeviceRowHeight + DeviceGap;
            }
        }

        private void RenderConnectionPanel(Canvas canvas, int x, int y, int width, int height)
        {
            bool healthy = stackReady && activeReady && activeLinkUp;
            Color stateColor = healthy ? Good : activeLinkUp ? Warning : Danger;

            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(healthy ? SystemTheme.AccentBorder : Border, x, y, width, height);
            IconManager.DrawScaled(canvas, healthy ? IconType.Ethernet : IconType.Network,
                x + 16, y + 20, 38, 38);

            SmallTextRenderer.Draw(canvas, "AKTYWNE POLACZENIE", x + 68, y + 17, Muted);
            SmallTextRenderer.DrawClipped(canvas, activeName, x + 68, y + 39,
                System.Math.Max(80, width - 210), Text);
            SmallTextRenderer.DrawClipped(canvas, activeIp, x + 68, y + 61,
                System.Math.Max(80, width - 210), stateColor);

            int badgeWidth = 112;
            int badgeX = x + width - badgeWidth - 14;
            canvas.DrawFilledRectangle(healthy ? Color.FromArgb(29, 60, 46) : Color.FromArgb(58, 38, 42),
                badgeX, y + 30, badgeWidth, 38);
            canvas.DrawRectangle(stateColor, badgeX, y + 30, badgeWidth, 38);
            SmallTextRenderer.DrawCentered(canvas, healthy ? "ONLINE" : "OFFLINE",
                badgeX + 5, y + 46, badgeWidth - 10, stateColor);
        }

        private void RenderConfigurationPanel(Canvas canvas, int x, int y, int width, int height)
        {
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "KONFIGURACJA IPv4", x + 14, y + 14, Text);
            SmallTextRenderer.Draw(canvas, "Profil zapisany w /etc/zonderq/settings.conf", x + 14, y + 31, Muted);

            int lineY = y + 57;
            DrawConfigLine(canvas, x, width, lineY, "TRYB", networkMode, SystemTheme.Accent);
            lineY += 27;
            DrawConfigLine(canvas, x, width, lineY, "IP", activeIp, Text);
            lineY += 27;
            DrawConfigLine(canvas, x, width, lineY, "MASKA", subnetMask, Text);
            lineY += 27;
            DrawConfigLine(canvas, x, width, lineY, "BRAMA", defaultGateway, Text);
            lineY += 27;
            DrawConfigLine(canvas, x, width, lineY, "DNS", dnsServer, Text);
            lineY += 27;
            DrawConfigLine(canvas, x, width, lineY, "MAC", activeMac, Muted);

            if (height >= 238)
            {
                int testY = y + height - 47;
                canvas.DrawLine(Border, x + 14, testY - 9, x + width - 14, testY - 9);
                IconManager.DrawScaled(canvas, IconType.Network, x + 14, testY, 20, 20);
                SmallTextRenderer.Draw(canvas, "DNS TEST: example.com", x + 44, testY + 6, Muted);
                SmallTextRenderer.DrawClipped(canvas, dnsTestResult, x + width / 2, testY + 6,
                    System.Math.Max(60, width / 2 - 18), dnsTestOk ? Good : Warning);
            }
        }

        private static void DrawConfigLine(Canvas canvas, int x, int width, int y,
            string label, string value, Color valueColor)
        {
            SmallTextRenderer.Draw(canvas, label, x + 16, y, Muted);
            SmallTextRenderer.DrawClipped(canvas, value ?? "--", x + 116, y,
                System.Math.Max(60, width - 132), valueColor);
        }

        private void RenderActions(Canvas canvas, int x, int y, int width, int height)
        {
            int gap = 8;
            int buttonWidth = System.Math.Max(76, (width - gap * (ActionCount - 1)) / ActionCount);

            for (int i = 0; i < ActionCount; i++)
            {
                int bx = x + i * (buttonWidth + gap);
                bool hover = i == hoveredActionIndex;
                Color background = hover ? PanelHover : Color.FromArgb(27, 34, 41);
                Color border = hover ? SystemTheme.AccentBorder : Border;

                canvas.DrawFilledRectangle(background, bx, y, buttonWidth, height);
                canvas.DrawRectangle(border, bx, y, buttonWidth, height);
                if (hover)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, bx, y, buttonWidth, 2);

                IconManager.DrawScaled(canvas, ActionIcons[i], bx + 9, y + 17, 22, 22);
                SmallTextRenderer.DrawClipped(canvas, ActionLabels[i], bx + 39, y + 25,
                    System.Math.Max(24, buttonWidth - 47), Text);
            }
        }

        private void RenderStatus(Canvas canvas, int x, int y, int width)
        {
            Color statusColor = stackReady ? Good : Danger;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), x, y, width, 25);
            canvas.DrawRectangle(Border, x, y, width, 25);
            canvas.DrawFilledRectangle(statusColor, x + 9, y + 9, 6, 6);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, x + 26, y + 10,
                System.Math.Max(50, width - 36), Text);
        }

        private int HitDevice(int mouseX, int mouseY)
        {
            int contentX = Window.X + 12;
            int contentY = Window.Y + 43;
            int contentWidth = System.Math.Max(520, Window.Width - 24);
            int contentHeight = System.Math.Max(420, Window.Height - 55);
            int leftWidth = System.Math.Min(300, System.Math.Max(230, contentWidth / 3));
            int x = contentX + 8;
            int y = contentY + 68;
            int footerY = contentY + contentHeight - 32;
            int actionTop = footerY - 70;
            int height = System.Math.Max(240, actionTop - y - 8);
            int rowY = y + 38;
            int maxVisible = System.Math.Min(deviceCount,
                System.Math.Max(1, (height - 48) / (DeviceRowHeight + DeviceGap)));

            for (int i = 0; i < maxVisible; i++)
            {
                if (Hit(mouseX, mouseY, x + 8, rowY, leftWidth - 16, DeviceRowHeight))
                    return i;
                rowY += DeviceRowHeight + DeviceGap;
            }
            return -1;
        }

        private int HitAction(int mouseX, int mouseY)
        {
            int contentX = Window.X + 12;
            int contentY = Window.Y + 43;
            int contentWidth = System.Math.Max(520, Window.Width - 24);
            int contentHeight = System.Math.Max(420, Window.Height - 55);
            int leftWidth = System.Math.Min(300, System.Math.Max(230, contentWidth / 3));
            int rightX = contentX + 8 + leftWidth + 12;
            int rightWidth = System.Math.Max(240, contentX + contentWidth - 8 - rightX);
            int footerY = contentY + contentHeight - 32;
            int actionTop = footerY - 70;
            int gap = 8;
            int buttonWidth = System.Math.Max(76, (rightWidth - gap * (ActionCount - 1)) / ActionCount);

            for (int i = 0; i < ActionCount; i++)
            {
                int x = rightX + i * (buttonWidth + gap);
                if (Hit(mouseX, mouseY, x, actionTop, buttonWidth, 58))
                    return i;
            }
            return -1;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }

        private void InvokeAction(int index)
        {
            switch (index)
            {
                case 0:
                    RefreshSnapshot();
                    SetStatus("ODSWIEZONO STAN SIECI");
                    break;
                case 1:
                    RenewDhcp();
                    break;
                case 2:
                    TestDns();
                    break;
                case 3:
                    OpenAdvancedConfiguration();
                    break;
            }
        }

        private void SelectDevice(int delta)
        {
            if (deviceCount <= 0)
                return;

            if (selectedDeviceIndex < 0)
                selectedDeviceIndex = delta >= 0 ? 0 : deviceCount - 1;
            else
                selectedDeviceIndex = System.Math.Max(0,
                    System.Math.Min(deviceCount - 1, selectedDeviceIndex + delta));
        }

        private void ActivateSelectedDevice()
        {
            if (selectedDeviceIndex < 0 || selectedDeviceIndex >= deviceCount)
                return;

            try
            {
                SetStatus("KONFIGUROWANIE WYBRANEGO INTERFEJSU...");
                bool ok = global::ZonderqOS.Network.SetActiveDevice(selectedDeviceIndex);
                RefreshSnapshot();
                SetStatus(ok ? "AKTYWNY INTERFEJS ZMIENIONY" : "NIE UDALO SIE SKONFIGUROWAC INTERFEJSU");
            }
            catch
            {
                RefreshSnapshot();
                SetStatus("BLAD PRZELACZANIA INTERFEJSU");
            }
        }

        private void RenewDhcp()
        {
            if (global::ZonderqOS.Network.ActiveDevice == null)
            {
                SetStatus("BRAK AKTYWNEGO INTERFEJSU");
                return;
            }

            try
            {
                SetStatus("ODNAWIANIE DZIERZAWY DHCP...");
                global::ZonderqOS.SystemSettings.SetNetworkModeDhcp(true);
                bool ok = global::ZonderqOS.Network.ConfigureDhcp();
                RefreshSnapshot();
                SetStatus(ok ? "DHCP ODNOWIONE" : "DHCP NIE UZYSKAL KONFIGURACJI");
            }
            catch
            {
                RefreshSnapshot();
                SetStatus("BLAD DHCP");
            }
        }

        private void TestDns()
        {
            if (!global::ZonderqOS.Network.IsReady)
            {
                dnsTestOk = false;
                dnsTestResult = "SIEC NIEGOTOWA";
                SetStatus("NAJPIERW SKONFIGURUJ POLACZENIE");
                return;
            }

            SetStatus("TESTOWANIE DNS: example.com...");
            string resolved = global::ZonderqOS.Network.ResolveDns(TestDomain);
            dnsTestOk = !string.IsNullOrEmpty(resolved);
            dnsTestResult = dnsTestOk ? resolved : "BRAK ODPOWIEDZI";
            SetStatus(dnsTestOk ? "DNS DZIALA" : "TEST DNS NIEUDANY");
        }

        private void OpenAdvancedConfiguration()
        {
            if (applicationManager == null)
            {
                SetStatus("BRAK APPLICATION MANAGERA");
                return;
            }

            applicationManager.Launch(new NetworkAdvancedApp(Window.X + 34, Window.Y + 28));
            SetStatus("OTWARTO KONFIGURACJE ZAAWANSOWANA");
        }

        private void RefreshSnapshot()
        {
            global::ZonderqOS.SystemSettings.Load();

            deviceCount = 0;
            activeDeviceIndex = -1;
            for (int i = 0; i < MaxDevices; i++)
            {
                deviceNames[i] = string.Empty;
                deviceMacs[i] = string.Empty;
                deviceLinkUp[i] = false;
                deviceReady[i] = false;
            }

            try
            {
                int available = global::ZonderqOS.Network.Devices.Count;
                deviceCount = System.Math.Min(MaxDevices, available);
                for (int i = 0; i < deviceCount; i++)
                {
                    var device = global::ZonderqOS.Network.Devices[i];
                    if (device == null)
                        continue;

                    deviceNames[i] = string.IsNullOrEmpty(device.Name) ? "INTERFEJS " + i : device.Name;
                    deviceMacs[i] = "" + device.MacAddress;
                    deviceLinkUp[i] = device.LinkUp;
                    deviceReady[i] = device.Ready;
                    if (device == global::ZonderqOS.Network.ActiveDevice)
                        activeDeviceIndex = i;
                }
            }
            catch
            {
                deviceCount = 0;
                activeDeviceIndex = -1;
            }

            if (activeDeviceIndex >= 0)
                selectedDeviceIndex = activeDeviceIndex;
            else if (deviceCount > 0 && (selectedDeviceIndex < 0 || selectedDeviceIndex >= deviceCount))
                selectedDeviceIndex = 0;
            else if (deviceCount == 0)
                selectedDeviceIndex = -1;

            activeName = "BRAK";
            activeMac = "--";
            activeIp = "0.0.0.0";
            subnetMask = "0.0.0.0";
            defaultGateway = "0.0.0.0";
            dnsServer = global::ZonderqOS.SystemSettings.DnsServer;
            networkMode = global::ZonderqOS.SystemSettings.NetworkModeName;
            activeLinkUp = false;
            activeReady = false;
            stackReady = global::ZonderqOS.Network.IsReady;

            try
            {
                var active = global::ZonderqOS.Network.ActiveDevice;
                if (active != null)
                {
                    activeName = string.IsNullOrEmpty(active.Name) ? "INTERFEJS" : active.Name;
                    activeMac = "" + active.MacAddress;
                    activeLinkUp = active.LinkUp;
                    activeReady = active.Ready;

                    IPConfig config = NetworkConfigManager.Get(active);
                    if (config != null)
                    {
                        if (config.IPAddress != null) activeIp = config.IPAddress.ToString();
                        if (config.SubnetMask != null) subnetMask = config.SubnetMask.ToString();
                        if (config.DefaultGateway != null) defaultGateway = config.DefaultGateway.ToString();
                    }
                    else
                    {
                        Address current = NetworkConfigManager.CurrentAddress;
                        if (current != null)
                            activeIp = current.ToString();
                    }
                }
            }
            catch
            {
                activeName = "BRAK";
                activeMac = "--";
                activeIp = "0.0.0.0";
                subnetMask = "0.0.0.0";
                defaultGateway = "0.0.0.0";
                activeLinkUp = false;
                activeReady = false;
            }
        }

        private void SetStatus(string message)
        {
            statusMessage = string.IsNullOrEmpty(message) ? "GOTOWE" : message;
        }
    }
}
