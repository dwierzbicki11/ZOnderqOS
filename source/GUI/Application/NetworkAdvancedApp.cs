using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Informational placeholder retained so old Settings links remain safe.
    /// Networking itself is not included in the OS build.
    /// </summary>
    public sealed class NetworkAdvancedApp : Application
    {
        public NetworkAdvancedApp(int x, int y) : base("Network niedostepna")
        {
            Window = new Window(x, y, 620, 260, "Network - funkcja usunieta");
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
            canvas.DrawString("Network support has been removed from ZonderqOS.", PCScreenFont.DefaultFont,
                Color.Black, Window.X + 28, Window.Y + 70);
            canvas.DrawString("Cosmos Gen3 does not yet provide full support", PCScreenFont.DefaultFont,
                Color.DimGray, Window.X + 28, Window.Y + 110);
            canvas.DrawString("for the target system hardware.", PCScreenFont.DefaultFont,
                Color.DimGray, Window.X + 28, Window.Y + 140);
        }
    }
}
