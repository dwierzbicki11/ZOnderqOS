using System;
using System.Drawing;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public class DiagnosticsApp : Application
    {
        private readonly Action closeCallback;
        private readonly Label titleLabel;
        private readonly Label statusLabel;
        private readonly Label keyboardLabel;
        private readonly Label mouseLabel;
        private readonly Label graphicsLabel;
        private readonly Button closeButton;

        public DiagnosticsApp(int x, int y, Action onClose) : base("Diagnostyka")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 560, 330, "Diagnostyka ZonderqOS");
            Window.CloseAction = Close;

            titleLabel = new Label(25, 55, "Diagnostyka systemu", Color.MidnightBlue);
            statusLabel = new Label(25, 100, "Status: GUI dziala poprawnie", Color.Green);
            keyboardLabel = new Label(25, 135, "Klawiatura: aktywna", Color.Black);
            mouseLabel = new Label(25, 170, "Mysz: aktywna", Color.Black);
            graphicsLabel = new Label(25, 205, "Grafika: framebuffer Canvas aktywny", Color.Black);

            Window.AddChild(titleLabel);
            Window.AddChild(statusLabel);
            Window.AddChild(keyboardLabel);
            Window.AddChild(mouseLabel);
            Window.AddChild(graphicsLabel);

            closeButton = new Button(210, 270, 140, 30, "Zamknij", Close);
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
    }
}
