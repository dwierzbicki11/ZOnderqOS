using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Apps;

namespace ZonderqOS.Games.Framework
{
    public enum GameOrientation
    {
        Landscape,
        Portrait
    }

    /// <summary>
    /// Base class for built-in ZonderqOS games. Games override Initialize(),
    /// Update(float), Render(GameCanvas) and optional input callbacks.
    /// The GUI owns the main loop; a game must not run its own blocking loop.
    /// </summary>
    public abstract class Game : Application
    {
        private readonly int logicalWidth;
        private readonly int logicalHeight;
        private readonly double fixedStep;
        private readonly GameCanvas gameCanvas;
        private long lastTimestamp;
        private double accumulatedSeconds;
        private bool initialized;

        public bool IsPaused { get; private set; }
        public GameOrientation Orientation { get; }

        protected int Width { get { return logicalWidth; } }
        protected int Height { get { return logicalHeight; } }

        protected Game(string title, int x, int y, int logicalWidth, int logicalHeight,
            GameOrientation orientation = GameOrientation.Landscape, int updatesPerSecond = 60)
            : base(title)
        {
            if (logicalWidth <= 0 || logicalHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(logicalWidth), "Game dimensions must be positive.");
            if (updatesPerSecond < 1 || updatesPerSecond > 240)
                throw new ArgumentOutOfRangeException(nameof(updatesPerSecond));
            if (orientation == GameOrientation.Portrait && logicalWidth > logicalHeight)
                throw new ArgumentException("Portrait games must have height >= width.");
            if (orientation == GameOrientation.Landscape && logicalHeight > logicalWidth)
                throw new ArgumentException("Landscape games must have width >= height.");

            this.logicalWidth = logicalWidth;
            this.logicalHeight = logicalHeight;
            Orientation = orientation;
            fixedStep = 1.0 / updatesPerSecond;
            gameCanvas = new GameCanvas(logicalWidth, logicalHeight);

            int windowWidth = orientation == GameOrientation.Portrait ? 460 : 780;
            int windowHeight = orientation == GameOrientation.Portrait ? 690 : 500;
            Window = new Window(x, y, windowWidth, windowHeight, title);
            Window.CloseAction = Close;
        }

        // Called lazily, after the subclass constructor has completed.
        protected virtual void Initialize() { }

        // Fixed timestep: independent of QEMU/host rendering speed.
        protected abstract void Update(float deltaSeconds);

        // Draw using logical coordinates, independent of actual window resolution.
        protected abstract void Render(GameCanvas graphics);

        protected virtual void OnKeyDown(KeyEvent key) { }
        protected virtual void OnMouseDown(int gameX, int gameY) { }
        protected virtual void Reset() { Initialize(); }

        private void EnsureInitialized()
        {
            if (initialized)
                return;
            initialized = true;
            Initialize();
            lastTimestamp = 0;
            accumulatedSeconds = 0;
        }

        public sealed override void Update()
        {
            if (!IsRunning || !Window.Visible || Window.IsMinimized || !Window.IsActive || IsPaused)
            {
                lastTimestamp = 0;
                accumulatedSeconds = 0;
                return;
            }

            EnsureInitialized();
            long now = Stopwatch.GetTimestamp();
            long frequency = Stopwatch.Frequency;
            if (frequency <= 0 || lastTimestamp == 0 || now < lastTimestamp)
            {
                lastTimestamp = now;
                return;
            }

            double elapsedSeconds = (now - lastTimestamp) / (double)frequency;
            lastTimestamp = now;
            if (elapsedSeconds > 0.1)
                elapsedSeconds = 0.1; // no spiral of death after a stalled GUI frame

            accumulatedSeconds += elapsedSeconds;
            int steps = 0;
            while (accumulatedSeconds >= fixedStep && steps < 5)
            {
                Update((float)fixedStep);
                accumulatedSeconds -= fixedStep;
                steps++;
            }
            if (steps == 5)
                accumulatedSeconds = 0;
        }

        public sealed override void HandleKeyboard(KeyEvent key)
        {
            if (!IsRunning || key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            EnsureInitialized();
            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                IsPaused = !IsPaused;
                lastTimestamp = 0;
                accumulatedSeconds = 0;
                return;
            }

            if (key.KeyChar == 'r' || key.KeyChar == 'R')
            {
                IsPaused = false;
                Reset();
                lastTimestamp = 0;
                accumulatedSeconds = 0;
                return;
            }

            if (!IsPaused)
                OnKeyDown(key);
        }

        public sealed override void HandleMouse(int mouseX, int mouseY, bool leftClicked,
            bool leftWasClicked, bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!IsRunning || !Window.Visible || Window.IsMinimized || !leftClicked || leftWasClicked)
                return;

            int gameX, gameY;
            if (gameCanvas.TryToLogical(mouseX, mouseY, out gameX, out gameY))
            {
                EnsureInitialized();
                if (!IsPaused)
                    OnMouseDown(gameX, gameY);
            }
        }

        public sealed override void Render(Canvas canvas)
        {
            if (!IsRunning || !Window.Visible || Window.IsMinimized)
                return;

            EnsureInitialized();
            base.Render(canvas);

            // Render *inside* the live content area, not behind the title bar.
            int x = Window.X + 8;
            int y = Window.Y + 46;
            int width = Math.Max(1, Window.Width - 16);
            int height = Math.Max(1, Window.Height - 54);
            canvas.DrawFilledRectangle(Color.Black, x, y, width, height);

            gameCanvas.BeginFrame(canvas, x, y, width, height);
            Render(gameCanvas);

            if (IsPaused)
                gameCanvas.DrawText("PAUZA - SPACJA: WZNOW", 12, 12, Color.White);
        }

        public override void Close()
        {
            base.Close();
            lastTimestamp = 0;
            accumulatedSeconds = 0;
        }
    }
}
