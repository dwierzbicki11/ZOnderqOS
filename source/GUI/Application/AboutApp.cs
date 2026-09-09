using System;
using System.Drawing;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public class AboutApp : Application
    {
        private readonly Action closeCallback;
        private readonly Label titleLabel;
        private readonly Label versionLabel;
        private readonly Label managerLabel;
        private readonly Label textLabel1;
        private readonly Label textLabel2;
        private readonly Button closeButton;

        public AboutApp(int x, int y, Action onClose) : base("O Systemie")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 520, 300, "O ZonderqOS");
            Window.CloseAction = Close;

            titleLabel = new Label(25, 55, "ZonderqOS", Color.MidnightBlue);
            versionLabel = new Label(25, 100, "Cosmos Kernel - Gen3", Color.DimGray);
            managerLabel = new Label(25, 135, "GUI desktop / Application Manager", Color.DimGray);
            textLabel1 = new Label(25, 180, "Terminal, Nano i kolejne aplikacje sa uruchamiane", Color.Black);
            textLabel2 = new Label(25, 205, "jako niezalezne okna systemu.", Color.Black);

            Window.AddChild(titleLabel);
            Window.AddChild(versionLabel);
            Window.AddChild(managerLabel);
            Window.AddChild(textLabel1);
            Window.AddChild(textLabel2);

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
    }
}
