using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public class AboutApp : Application
    {
        private readonly Action closeCallback;
        private readonly Button closeButton;

        public AboutApp(int x, int y, Action onClose) : base("O Systemie")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 520, 300, "O ZonderqOS");
            Window.CloseAction = Close;

            closeButton = new Button(190, 245, 140, 30, "Zamknij", Close);
            closeButton.BackgroundColor = Color.DarkSlateBlue;
            closeButton.TextColor = Color.White;
            Window.AddChild(closeButton);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
                Close();
        }

        public override void Update()
        {
            closeButton.X = Window.X + (Window.Width - closeButton.Width) / 2;
            closeButton.Y = Window.Y + Window.Height - 45;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible) return;

            base.Render(canvas);
            canvas.DrawString("ZonderqOS", PCScreenFont.DefaultFont, Color.MidnightBlue, Window.X + 25, Window.Y + 55);
            canvas.DrawString("Cosmos Kernel - Gen3", PCScreenFont.DefaultFont, Color.DimGray, Window.X + 25, Window.Y + 100);
            canvas.DrawString("GUI desktop / Application Manager", PCScreenFont.DefaultFont, Color.DimGray, Window.X + 25, Window.Y + 135);
            canvas.DrawString("Terminal, Nano i kolejne aplikacje sa uruchamiane", PCScreenFont.DefaultFont, Color.Black, Window.X + 25, Window.Y + 180);
            canvas.DrawString("jako niezalezne okna systemu.", PCScreenFont.DefaultFont, Color.Black, Window.X + 25, Window.Y + 205);
        }
    }
}
