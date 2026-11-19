using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Compact network diagnostics view kept for Settings compatibility links.
    /// </summary>
    public sealed class NetworkAdvancedApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color TextColor = Color.FromArgb(235, 240, 245);
        private static readonly Color Muted = Color.FromArgb(145, 160, 178);

        public NetworkAdvancedApp(int x, int y) : base("Network Diagnostics")
        {
            Window = new Window(x, y, 540, 220, "Network Diagnostics");
            Window.CloseAction = Close;
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key != null && key.Key == ConsoleKeyEx.Escape)
                Close();
        }

        public override void Render(Canvas canvas)
        {
            if (Window == null || !Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);
            int wx = Window.X;
            int wy = Window.Y + 34;
            canvas.DrawFilledRectangle(Surface, wx + 1, wy, Window.Width - 2, Window.Height - 35);

            SmallTextRenderer.Draw(canvas, "Use Network Center or terminal commands:", wx + 20, wy + 20, TextColor);
            SmallTextRenderer.Draw(canvas, "  net info / net dhcp / ip info / ping <host>", wx + 20, wy + 48, Muted);
            SmallTextRenderer.Draw(canvas, "  dns <domain> / httpget <https://url>", wx + 20, wy + 72, Muted);
            SmallTextRenderer.Draw(canvas, "  zupdate check / zupdate ota-download / zupdate install", wx + 20, wy + 96, Muted);
            SmallTextRenderer.Draw(canvas, Network.IsReady ? "STATUS: ONLINE" : "STATUS: OFFLINE", wx + 20, wy + 132, TextColor);
        }
    }
}
