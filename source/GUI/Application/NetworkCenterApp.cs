using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Informational placeholder. The network stack and network tools are not
    /// included in current ZonderqOS builds.
    /// </summary>
    public sealed class NetworkCenterApp : Application
    {
        private readonly Action closeCallback;

        public NetworkCenterApp(int x, int y, ApplicationManager manager, Action onClose)
            : base("Siec niedostepna")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 640, 280, "Siec - funkcja usunieta");
            Window.CloseAction = Close;
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key != null && key.Key == ConsoleKeyEx.Escape)
                Close();
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
            canvas.DrawString("Networking is not included in this ZonderqOS build.", PCScreenFont.DefaultFont,
                Color.Black, Window.X + 28, Window.Y + 72);
            canvas.DrawString("DHCP, DNS, ping and network adapters are disabled.", PCScreenFont.DefaultFont,
                Color.DimGray, Window.X + 28, Window.Y + 112);
            canvas.DrawString("Support will return after the hardware path is complete.", PCScreenFont.DefaultFont,
                Color.DimGray, Window.X + 28, Window.Y + 145);
        }
    }
}
