using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.Games.Framework;

namespace ZonderqOS.Games.Snake
{
    /// <summary>
    /// Portrait game template. Fill in the snake body, food and collisions.
    /// Deliberately NOT a completed Snake implementation.
    /// </summary>
    public sealed class SnakeGame : Game
    {
        private int headX;
        private int headY;
        private int directionX;
        private int directionY;

        public SnakeGame(int x, int y)
            : base("Snake - szkielet", x, y, 320, 480, GameOrientation.Portrait, 10)
        {
        }

        protected override void Initialize()
        {
            headX = Width / 2;
            headY = Height / 2;
            directionX = 0;
            directionY = 0;
        }

        protected override void Update(float deltaSeconds)
        {
            // TODO: advance on a grid, store tail segments, add food, detect collisions.
            // The example cursor moves one cell per tick.
            headX = System.Math.Max(0, System.Math.Min(Width - 20, headX + directionX * 20));
            headY = System.Math.Max(0, System.Math.Min(Height - 20, headY + directionY * 20));
        }

        protected override void OnKeyDown(KeyEvent key)
        {
            directionX = 0;
            directionY = 0;
            if (key.Key == ConsoleKeyEx.LeftArrow) directionX = -1;
            if (key.Key == ConsoleKeyEx.RightArrow) directionX = 1;
            if (key.Key == ConsoleKeyEx.UpArrow) directionY = -1;
            if (key.Key == ConsoleKeyEx.DownArrow) directionY = 1;
        }

        protected override void Render(GameCanvas graphics)
        {
            graphics.Clear(Color.FromArgb(16, 28, 23));
            graphics.DrawText("SNAKE - SZKIELET", 12, 12, Color.White);
            graphics.FillRect(Color.LimeGreen, headX, headY, 20, 20);
            graphics.DrawText("STRZALKI - RUCH", 12, Height - 34, Color.White);
            graphics.DrawText("R - RESET / ESC - WYJSCIE", 12, Height - 19, Color.White);
        }
    }
}
