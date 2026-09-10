using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public abstract class Application
    {
        public string Name { get; protected set; }
        public Window Window { get; protected set; }
        public bool IsRunning { get; private set; } = true;

        protected Application(string name)
        {
            Name = name;
        }

        public virtual void Update() { }

        public virtual void HandleKeyboard(KeyEvent key) { }

        public virtual void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            Window?.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
        }

        public virtual void Render(Canvas canvas)
        {
            if (Window != null && Window.Visible && IsRunning)
                Window.Render(canvas);
        }

        public virtual void Close()
        {
            if (!IsRunning)
                return;

            IsRunning = false;
            if (Window != null)
                Window.Visible = false;
        }
    }
}