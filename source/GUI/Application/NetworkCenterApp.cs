using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.Security;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Live ZonderqOS Network Center showing discovered Cosmos 3.0.97 adapters
    /// (Intel E1000E / VirtIO-Net), IPv4/DHCP/DNS status, and interactive actions.
    /// Zero allocations on idle Render() frames; no continuous redraw loop.
    /// </summary>
    public sealed class NetworkCenterApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color Panel = Color.FromArgb(28, 35, 45);
        private static readonly Color Border = Color.FromArgb(52, 66, 84);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color Muted = Color.FromArgb(145, 160, 178);
        private static readonly Color Accent = Color.FromArgb(45, 145, 235);
        private static readonly Color Good = Color.FromArgb(55, 215, 105);
        private static readonly Color Warn = Color.FromArgb(245, 195, 45);
        private static readonly Color Danger = Color.FromArgb(235, 75, 75);

        private const int HeaderHeight = 36;
        private readonly Action closeCallback;

        private string adapterName = "NONE";
        private string macText = "--";
        private string linkText = "DOWN";
        private string ipText = "0.0.0.0";
        private string maskText = "0.0.0.0";
        private string gwText = "0.0.0.0";
        private string dnsText = "0.0.0.0";
        private bool isOnline;
        private string statusLine = "Ready";
        private Color statusColor = Muted;

        public NetworkCenterApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Network Center")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 560, 340, "ZonderqOS Network Center");
            Window.CloseAction = Close;
            RefreshSnapshot();
        }

        private void RefreshSnapshot()
        {
            Network.Refresh();
            NetworkDeviceInfo dev = Network.ActiveDevice;
            isOnline = Network.IsReady;
            if (dev == null)
            {
                adapterName = "NONE (Plug Ethernet or Phone USB Tethering)";
                macText = "--";
                linkText = "NO ADAPTER";
                ipText = "0.0.0.0";
                maskText = "0.0.0.0";
                gwText = "0.0.0.0";
                dnsText = "0.0.0.0";
            }
            else
            {
                adapterName = dev.Name ?? "nic0";
                macText = dev.MacAddress ?? "--";
                linkText = dev.Ready && dev.LinkUp ? "UP (READY)" : (dev.LinkUp ? "UP (NOT READY)" : "DOWN");
                ipText = dev.IpAddress ?? "0.0.0.0";
                maskText = dev.SubnetMask ?? "0.0.0.0";
                gwText = dev.DefaultGateway ?? "0.0.0.0";
                dnsText = dev.DnsServer ?? Network.CurrentDnsServer ?? "0.0.0.0";
            }
            statusLine = Network.LastStatusMessage ?? "Ready";
            statusColor = isOnline ? Good : Muted;
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            base.HandleKeyboard(key);
            if (key == null) return;
            if (key.Key == ConsoleKeyEx.Escape)
                Close();
            else if (key.Key == ConsoleKeyEx.F5)
                RefreshSnapshot();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
            if (!Window.Visible || Window.IsMinimized || !leftClicked || leftWasClicked)
                return;

            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int btnY = wy + 242;

            // Button 1: RENEW DHCP
            if (Hit(mouseX, mouseY, wx + 16, btnY, 150, 28))
            {
                if (!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0)
                {
                    statusLine = "Root privileges required to reconfigure DHCP.";
                    statusColor = Danger;
                    return;
                }
                statusLine = "Requesting DHCP lease...";
                statusColor = Accent;
                bool ok = Network.ConfigureDhcp();
                RefreshSnapshot();
                if (!ok)
                {
                    statusLine = Network.LastStatusMessage ?? "DHCP failed.";
                    statusColor = Danger;
                }
                return;
            }

            // Button 2: TEST DNS (github.com)
            if (Hit(mouseX, mouseY, wx + 178, btnY, 180, 28))
            {
                if (!Network.IsReady)
                {
                    statusLine = "Network offline. Click RENEW DHCP first.";
                    statusColor = Warn;
                    return;
                }
                string ip = Network.ResolveDns("github.com");
                if (!string.IsNullOrEmpty(ip))
                {
                    statusLine = "DNS OK: github.com -> " + ip;
                    statusColor = Good;
                }
                else
                {
                    statusLine = "DNS lookup failed for github.com";
                    statusColor = Danger;
                }
                return;
            }

            // Button 3: REFRESH
            if (Hit(mouseX, mouseY, wx + 370, btnY, 174, 28))
            {
                RefreshSnapshot();
                statusLine = "Interface snapshot refreshed.";
                statusColor = Accent;
            }
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Render(Canvas canvas)
        {
            if (Window == null || !Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);
            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - HeaderHeight;

            canvas.DrawFilledRectangle(Surface, wx + 1, wy, ww - 2, wh - 1);

            // Status Banner
            canvas.DrawFilledRectangle(Panel, wx + 16, wy + 12, ww - 32, 42);
            canvas.DrawRectangle(isOnline ? Good : Border, wx + 16, wy + 12, ww - 32, 42);
            SmallTextRenderer.Draw(canvas, isOnline ? "ETHERNET ONLINE (TLS 1.2/1.3 OTA READY)" : "NETWORK OFFLINE",
                wx + 28, wy + 22, isOnline ? Good : Warn);
            SmallTextRenderer.DrawClipped(canvas, adapterName, wx + 28, wy + 38, ww - 56, Muted);

            // Details Card
            canvas.DrawFilledRectangle(Panel, wx + 16, wy + 64, ww - 32, 166);
            canvas.DrawRectangle(Border, wx + 16, wy + 64, ww - 32, 166);

            DrawRow(canvas, wx + 28, wy + 78, "MAC ADDRESS:", macText);
            DrawRow(canvas, wx + 28, wy + 102, "LINK STATE:", linkText);
            DrawRow(canvas, wx + 28, wy + 126, "IPv4 ADDRESS:", ipText);
            DrawRow(canvas, wx + 28, wy + 150, "SUBNET MASK:", maskText);
            DrawRow(canvas, wx + 28, wy + 174, "GATEWAY:", gwText);
            DrawRow(canvas, wx + 28, wy + 198, "DNS SERVER:", dnsText);

            // Action Buttons
            int btnY = wy + 242;
            DrawButton(canvas, wx + 16, btnY, 150, 28, "RENEW DHCP", Accent);
            DrawButton(canvas, wx + 178, btnY, 180, 28, "TEST DNS (GITHUB)", Panel);
            DrawButton(canvas, wx + 370, btnY, 174, 28, "REFRESH", Panel);

            // Status Footer
            SmallTextRenderer.DrawClipped(canvas, statusLine, wx + 16, wy + 280, ww - 32, statusColor);
        }

        private static void DrawRow(Canvas canvas, int x, int y, string label, string value)
        {
            SmallTextRenderer.Draw(canvas, label, x, y, Muted);
            SmallTextRenderer.DrawClipped(canvas, value, x + 150, y, 340, TextColor);
        }

        private static void DrawButton(Canvas canvas, int x, int y, int w, int h, string text, Color bg)
        {
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            SmallTextRenderer.DrawClipped(canvas, text, x + 10, y + 10, w - 20, TextColor);
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }
    }
}
