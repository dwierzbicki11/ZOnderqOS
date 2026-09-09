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

        public virtual void HandleKeyboard(Cosmos.Kernel.System.Keyboard.KeyEvent key) { }

        public virtual void Close()
        {
            IsRunning = false;
            if (Window != null)
                Window.Visible = false;
        }
    }
}