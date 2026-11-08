using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public class AboutApp : Application
    {
        private readonly Action closeCallback;

        public AboutApp(int x, int y, Action onClose) : base("O Systemie")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 560, 300, "O ZOnderqOS");
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

            int x = Window.X + 18;
            int y = Window.Y + 54;
            int width = System.Math.Max(1, Window.Width - 36);
            int height = System.Math.Max(1, Window.Height - 72);

            UiPainter.RoundedPanel(canvas, x, y, width, height,
                UiMetrics.WindowRadius, UiPalette.SurfaceRaised, UiPalette.Border);

            // The About panel can be constrained to a 320x196 desktop.
            // Do not paint rows beyond its client rectangle after a resize.
            if (width >= 110 && height >= 64)
            {
                IconManager.DrawScaled(canvas, IconType.Start, x + 18, y + 18, 42, 42);
                UiTypography.Heading(canvas, "ZOnderqOS", x + 78, y + 13,
                    System.Math.Max(1, width - 94), UiPalette.Text);
                UiTypography.Caption(canvas, "COSMOS KERNEL GEN 3  /  SDK 3.0.95",
                    x + 78, y + 43, System.Math.Max(1, width - 94));
            }

            if (height >= 80 && width >= 40)
                canvas.DrawLine(UiPalette.Border, x + 18, y + 78, x + width - 18, y + 78);
            if (height >= 110)
                UiTypography.Caption(canvas, "NATYWNY SYSTEM OPERACYJNY X86_64",
                    x + 18, y + 96, System.Math.Max(1, width - 36));
            if (height >= 140)
                UiTypography.Caption(canvas, "PULPIT  /  TERMINAL  /  APLIKACJE",
                    x + 18, y + 124, System.Math.Max(1, width - 36));
            if (height >= 168)
                UiTypography.Caption(canvas, "WINDOWS, NATIVE GUI, COSMOS RUNTIME",
                    x + 18, y + 152, System.Math.Max(1, width - 36));
        }
    }
}
