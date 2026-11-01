using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.Games.Framework;

namespace ZonderqOS.Games.Pong
{
    /// <summary>Playable Pong vs CPU. Up/Down move the left paddle.</summary>
    public sealed class PongGame : Game
    {
        private const int PaddleWidth = 12;
        private const int PaddleHeight = 74;
        private const int BallSize = 12;

        private float playerY;
        private float computerY;
        private float ballX;
        private float ballY;
        private float velocityX;
        private float velocityY;
        private int playerScore;
        private int computerScore;
        private string scoreText = "0 : 0";

        public PongGame(int x, int y)
            : base("Pong", x, y, 640, 360, GameOrientation.Landscape, 60)
        {
        }

        protected override void Initialize()
        {
            playerScore = 0;
            computerScore = 0;
            scoreText = "0 : 0";
            playerY = (Height - PaddleHeight) / 2f;
            computerY = playerY;
            Serve(1);
        }

        private void Serve(int direction)
        {
            ballX = (Width - BallSize) / 2f;
            ballY = (Height - BallSize) / 2f;
            velocityX = 220f * direction;
            velocityY = 130f;
        }

        protected override void Update(float deltaSeconds)
        {
            // A lightweight deterministic CPU opponent.
            float target = ballY + BallSize / 2f - PaddleHeight / 2f;
            float maxMove = 170f * deltaSeconds;
            computerY += Math.Max(-maxMove, Math.Min(maxMove, target - computerY));
            computerY = Math.Max(0f, Math.Min(Height - PaddleHeight, computerY));

            ballX += velocityX * deltaSeconds;
            ballY += velocityY * deltaSeconds;
            if (ballY <= 0f)
            {
                ballY = 0f;
                velocityY = Math.Abs(velocityY);
            }
            else if (ballY >= Height - BallSize)
            {
                ballY = Height - BallSize;
                velocityY = -Math.Abs(velocityY);
            }

            if (velocityX < 0f && ballX <= 36 && ballX + BallSize >= 24 &&
                ballY + BallSize >= playerY && ballY <= playerY + PaddleHeight)
            {
                ballX = 36;
                velocityX = Math.Min(440f, -velocityX + 15f);
                velocityY = (ballY + BallSize / 2f - playerY - PaddleHeight / 2f) * 5f;
            }
            else if (velocityX > 0f && ballX + BallSize >= Width - 24 &&
                ballX <= Width - 12 && ballY + BallSize >= computerY &&
                ballY <= computerY + PaddleHeight)
            {
                ballX = Width - 24 - BallSize;
                velocityX = Math.Max(-440f, -velocityX - 15f);
                velocityY = (ballY + BallSize / 2f - computerY - PaddleHeight / 2f) * 5f;
            }

            if (ballX < -BallSize)
            {
                computerScore++;
                scoreText = playerScore + " : " + computerScore;
                Serve(1);
            }
            else if (ballX > Width)
            {
                playerScore++;
                scoreText = playerScore + " : " + computerScore;
                Serve(-1);
            }
        }

        protected override void OnKeyDown(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.UpArrow || key.KeyChar == 'w' || key.KeyChar == 'W')
                playerY = Math.Max(0f, playerY - 24f);
            else if (key.Key == ConsoleKeyEx.DownArrow || key.KeyChar == 's' || key.KeyChar == 'S')
                playerY = Math.Min(Height - PaddleHeight, playerY + 24f);
        }

        protected override void Render(GameCanvas graphics)
        {
            graphics.Clear(Color.FromArgb(12, 21, 32));
            for (int y = 16; y < Height; y += 26)
                graphics.FillRect(Color.FromArgb(61, 75, 89), Width / 2 - 2, y, 4, 12);

            graphics.FillRect(Color.FromArgb(83, 191, 243), 24, (int)playerY, PaddleWidth, PaddleHeight);
            graphics.FillRect(Color.FromArgb(239, 179, 86), Width - 24, (int)computerY,
                PaddleWidth, PaddleHeight);
            graphics.FillRect(Color.White, (int)ballX, (int)ballY, BallSize, BallSize);
            graphics.DrawText(scoreText, Width / 2 - 22, 14, Color.White);
            graphics.DrawText("GORA/DOL lub W/S - PALETKA", 15, Height - 31, Color.White);
            graphics.DrawText("SPACJA - PAUZA   R - RESTART   ESC - WYJSCIE", 15, Height - 16, Color.White);
        }
    }
}
