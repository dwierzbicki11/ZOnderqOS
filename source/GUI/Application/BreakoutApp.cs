using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class BreakoutApp : Application
    {
        private const int BrickCols = 8;
        private const int BrickRows = 5;
        private const int BrickWidth = 40;
        private const int BrickHeight = 14;
        private const int BrickGap = 4;

        private const int PaddleWidth = 66;
        private const int PaddleHeight = 10;
        private const int BallSize = 7;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color HeaderPanel = Color.FromArgb(27, 34, 41);
        private static readonly Color FieldBg = Color.FromArgb(12, 16, 22);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(73, 163, 224);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color GreenColor = Color.FromArgb(60, 200, 100);
        private static readonly Color YellowColor = Color.FromArgb(245, 210, 50);

        private static readonly Color[] RowColors =
        {
            Color.FromArgb(235, 65, 65),   // Row 0: Red
            Color.FromArgb(245, 135, 35),  // Row 1: Orange
            Color.FromArgb(245, 205, 45),  // Row 2: Yellow
            Color.FromArgb(50, 200, 95),   // Row 3: Green
            Color.FromArgb(55, 175, 235)   // Row 4: Cyan
        };

        private static readonly int[] RowPoints = { 50, 40, 30, 20, 10 };

        private enum GameStatus
        {
            Ready,
            Playing,
            Paused,
            GameOver,
            Victory
        }

        private readonly bool[,] bricks = new bool[BrickCols, BrickRows];
        private int bricksRemaining;
        private GameStatus status = GameStatus.Ready;

        private uint score;
        private uint highScore;
        private int lives = 3;

        private float paddleX;
        private float ballX;
        private float ballY;
        private float ballVx;
        private float ballVy;
        private float ballSpeed = 4.2f;

        private long lastTick;
        private long toneEndTicks;
        private int updateFrames;
        private readonly Action closeCallback;

        public BreakoutApp(int x, int y, Action onClose) : base("Arkanoid")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 400, 470, "Arkanoid");
            Window.CloseAction = Close;
            ResetGame();
        }

        public override bool NeedsContinuousRedraw => status == GameStatus.Playing;

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        private void ResetGame()
        {
            score = 0;
            lives = 3;
            ballSpeed = 4.2f;
            InitLevel();
        }

        private void InitLevel()
        {
            bricksRemaining = BrickCols * BrickRows;
            for (int r = 0; r < BrickRows; r++)
            {
                for (int c = 0; c < BrickCols; c++)
                {
                    bricks[c, r] = true;
                }
            }

            paddleX = (360 - PaddleWidth) / 2.0f;
            ResetBall();
            status = GameStatus.Ready;
        }

        private void ResetBall()
        {
            ballX = paddleX + (PaddleWidth - BallSize) / 2.0f;
            ballY = 320.0f; // Directly above paddle
            ballVx = 0.0f;
            ballVy = 0.0f;
        }

        private void LaunchBall()
        {
            if (status != GameStatus.Ready)
                return;

            ballSpeed = 4.2f + (BrickCols * BrickRows - bricksRemaining) * 0.05f;
            ballVx = 1.8f;
            ballVy = -ballSpeed;
            status = GameStatus.Playing;
            lastTick = Stopwatch.GetTimestamp();

            PlaySound(600, 30);
        }

        private void PlaySound(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long freqClock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (freqClock * durationMs) / 1000;
        }

        public override void Update()
        {
            long now = Stopwatch.GetTimestamp();

            // Sound timer shutoff
            if (toneEndTicks > 0 && now >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }

            if (status != GameStatus.Playing)
                return;

            updateFrames++;
            long intervalTicks = Stopwatch.Frequency > 0 ? (Stopwatch.Frequency * 16) / 1000 : 16000;
            bool timeTriggered = (lastTick > 0 && now - lastTick >= intervalTicks);
            bool frameTriggered = (updateFrames >= 1);

            if (timeTriggered || frameTriggered)
            {
                lastTick = now;
                updateFrames = 0;
                StepPhysics();
            }
        }

        private void StepPhysics()
        {
            float fieldW = 360.0f;
            float fieldH = 345.0f;
            float paddleY = fieldH - 18.0f;

            ballX += ballVx;
            ballY += ballVy;

            // Bounce off left / right walls
            if (ballX <= 0)
            {
                ballX = 0;
                ballVx = Abs(ballVx);
                PlaySound(420, 25);
            }
            else if (ballX + BallSize >= fieldW)
            {
                ballX = fieldW - BallSize;
                ballVx = -Abs(ballVx);
                PlaySound(420, 25);
            }

            // Bounce off top wall
            if (ballY <= 0)
            {
                ballY = 0;
                ballVy = Abs(ballVy);
                PlaySound(420, 25);
            }

            // Ball drops below bottom: lose life
            if (ballY > fieldH)
            {
                lives--;
                PlaySound(180, 120);

                if (lives <= 0)
                {
                    status = GameStatus.GameOver;
                    if (score > highScore)
                        highScore = score;
                }
                else
                {
                    status = GameStatus.Ready;
                    ResetBall();
                }
                return;
            }

            // Paddle collision
            if (ballVy > 0 &&
                ballY + BallSize >= paddleY &&
                ballY <= paddleY + PaddleHeight &&
                ballX + BallSize >= paddleX &&
                ballX <= paddleX + PaddleWidth)
            {
                ballY = paddleY - BallSize;

                // Angle based on where ball hit paddle: -1.0 (left edge) to +1.0 (right edge)
                float hitOffset = (ballX + BallSize * 0.5f) - (paddleX + PaddleWidth * 0.5f);
                float normalized = hitOffset / (PaddleWidth * 0.5f);
                if (normalized < -0.92f) normalized = -0.92f;
                if (normalized > 0.92f) normalized = 0.92f;

                // Adjust speed slightly with progress
                ballSpeed = 4.2f + (BrickCols * BrickRows - bricksRemaining) * 0.05f;
                if (ballSpeed > 6.8f) ballSpeed = 6.8f;

                float maxVx = ballSpeed * 0.88f;
                ballVx = normalized * maxVx;
                ballVy = -ballSpeed;

                PlaySound(550, 30);
            }

            // Brick collisions
            int gridLeft = (360 - (BrickCols * BrickWidth + (BrickCols - 1) * BrickGap)) / 2;
            int gridTop = 18;

            for (int r = 0; r < BrickRows; r++)
            {
                for (int c = 0; c < BrickCols; c++)
                {
                    if (!bricks[c, r])
                        continue;

                    int bx = gridLeft + c * (BrickWidth + BrickGap);
                    int by = gridTop + r * (BrickHeight + BrickGap);

                    // AABB check
                    if (ballX + BallSize >= bx && ballX <= bx + BrickWidth &&
                        ballY + BallSize >= by && ballY <= by + BrickHeight)
                    {
                        // Destroy brick
                        bricks[c, r] = false;
                        bricksRemaining--;
                        score += (uint)RowPoints[r];
                        if (score > highScore)
                            highScore = score;

                        // Deflect ball: check previous position to determine hit axis
                        float prevX = ballX - ballVx;
                        float prevY = ballY - ballVy;

                        if (prevX + BallSize <= bx || prevX >= bx + BrickWidth)
                        {
                            ballVx = -ballVx;
                        }
                        else
                        {
                            ballVy = -ballVy;
                        }

                        PlaySound((ushort)(680 + (BrickRows - 1 - r) * 90), 35);

                        // Check victory
                        if (bricksRemaining <= 0)
                        {
                            status = GameStatus.Victory;
                            PlaySound(1150, 200);
                        }
                        return;
                    }
                }
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.R)
            {
                ResetGame();
                return;
            }

            if (key.Key == ConsoleKeyEx.P)
            {
                if (status == GameStatus.Playing)
                    status = GameStatus.Paused;
                else if (status == GameStatus.Paused)
                    status = GameStatus.Playing;
                return;
            }

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (status == GameStatus.Ready)
                {
                    LaunchBall();
                }
                else if (status == GameStatus.Playing)
                {
                    status = GameStatus.Paused;
                }
                else if (status == GameStatus.Paused)
                {
                    status = GameStatus.Playing;
                }
                else if (status == GameStatus.GameOver || status == GameStatus.Victory)
                {
                    ResetGame();
                }
                return;
            }

            // Left / Right keys for paddle steering
            if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                paddleX -= 18.0f;
                if (paddleX < 0) paddleX = 0;
                if (status == GameStatus.Ready) ballX = paddleX + (PaddleWidth - BallSize) / 2.0f;
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                paddleX += 18.0f;
                if (paddleX > 360 - PaddleWidth) paddleX = 360 - PaddleWidth;
                if (status == GameStatus.Ready) ballX = paddleX + (PaddleWidth - BallSize) / 2.0f;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int fieldX = contentX + 12;
            int fieldY = contentY + 44;

            // Header button hit test [NOWA GRA]
            int btnW = 80;
            int btnH = 24;
            int btnX = contentX + 288;
            int btnY = contentY + 8;

            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= btnX && mouseX < btnX + btnW &&
                    mouseY >= btnY && mouseY < btnY + btnH)
                {
                    ResetGame();
                    return;
                }
            }

            // Mouse steering inside field
            if (mouseX >= fieldX && mouseX <= fieldX + 360)
            {
                float targetX = (mouseX - fieldX) - PaddleWidth / 2.0f;
                if (targetX < 0) targetX = 0;
                if (targetX > 360 - PaddleWidth) targetX = 360 - PaddleWidth;

                paddleX = targetX;

                if (status == GameStatus.Ready)
                {
                    ballX = paddleX + (PaddleWidth - BallSize) / 2.0f;
                }
            }

            // Click inside playing area to launch ball or restart
            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= fieldX && mouseX < fieldX + 360 &&
                    mouseY >= fieldY && mouseY < fieldY + 345)
                {
                    if (status == GameStatus.Ready)
                    {
                        LaunchBall();
                    }
                    else if (status == GameStatus.GameOver || status == GameStatus.Victory)
                    {
                        ResetGame();
                    }
                    else if (status == GameStatus.Paused)
                    {
                        status = GameStatus.Playing;
                    }
                }
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header panel
            canvas.DrawFilledRectangle(HeaderPanel, contentX + 8, contentY + 6, contentWidth - 16, 32);
            canvas.DrawRectangle(Border, contentX + 8, contentY + 6, contentWidth - 16, 32);

            // Header Stats
            SmallTextRenderer.Draw(canvas, "WYNIK", contentX + 16, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, score, contentX + 16, contentY + 23, AccentColor);

            SmallTextRenderer.Draw(canvas, "REKORD", contentX + 80, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, highScore, contentX + 80, contentY + 23, TextColor);

            SmallTextRenderer.Draw(canvas, "ZYCIA", contentX + 155, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, (ulong)Math.Max(0, lives), contentX + 155, contentY + 23, RedColor);

            SmallTextRenderer.Draw(canvas, "CEGLY", contentX + 215, contentY + 11, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, (ulong)bricksRemaining, contentX + 215, contentY + 23, YellowColor);

            // Header button [NOWA GRA]
            int btnW = 80;
            int btnH = 24;
            int btnX = contentX + 288;
            int btnY = contentY + 10;
            canvas.DrawFilledRectangle(Color.FromArgb(36, 45, 55), btnX, btnY, btnW, btnH);
            canvas.DrawRectangle(Border, btnX, btnY, btnW, btnH);
            SmallTextRenderer.DrawCentered(canvas, "NOWA GRA", btnX, btnY + 8, btnW, TextColor);

            // Playing Field
            int fieldX = contentX + 12;
            int fieldY = contentY + 44;
            int fieldW = 360;
            int fieldH = 345;

            canvas.DrawFilledRectangle(FieldBg, fieldX, fieldY, fieldW, fieldH);
            canvas.DrawRectangle(Border, fieldX - 1, fieldY - 1, fieldW + 2, fieldH + 2);

            // Draw Bricks
            int gridLeft = (fieldW - (BrickCols * BrickWidth + (BrickCols - 1) * BrickGap)) / 2;
            int gridTop = 18;

            for (int r = 0; r < BrickRows; r++)
            {
                Color brickColor = RowColors[r % RowColors.Length];
                Color highlight = Color.FromArgb(
                    Math.Min(255, brickColor.R + 40),
                    Math.Min(255, brickColor.G + 40),
                    Math.Min(255, brickColor.B + 40));

                for (int c = 0; c < BrickCols; c++)
                {
                    if (!bricks[c, r])
                        continue;

                    int bx = fieldX + gridLeft + c * (BrickWidth + BrickGap);
                    int by = fieldY + gridTop + r * (BrickHeight + BrickGap);

                    canvas.DrawFilledRectangle(brickColor, bx, by, BrickWidth, BrickHeight);
                    // Top highlight line
                    canvas.DrawFilledRectangle(highlight, bx, by, BrickWidth, 1);
                }
            }

            // Draw Paddle
            int px = fieldX + (int)paddleX;
            int py = fieldY + fieldH - 18;
            canvas.DrawFilledRectangle(AccentColor, px, py, PaddleWidth, PaddleHeight);
            canvas.DrawFilledRectangle(Color.FromArgb(170, 225, 255), px, py, PaddleWidth, 2);

            // Draw Ball
            int ballDrawX = fieldX + (int)ballX;
            int ballDrawY = fieldY + (int)ballY;
            canvas.DrawFilledRectangle(Color.White, ballDrawX, ballDrawY, BallSize, BallSize);

            // Draw Overlay messages
            if (status == GameStatus.Ready)
            {
                DrawOverlayBanner(canvas, fieldX, fieldY, fieldW, fieldH, "KLIKNIJ LUB SPACJA ABY WYSTRZELIC", AccentColor);
            }
            else if (status == GameStatus.Paused)
            {
                DrawOverlayBanner(canvas, fieldX, fieldY, fieldW, fieldH, "PAUZA - WCISNIJ SPACJE", YellowColor);
            }
            else if (status == GameStatus.GameOver)
            {
                DrawOverlayBanner(canvas, fieldX, fieldY, fieldW, fieldH, "KONIEC GRY! KLIKNIJ ABY ZAGRAC", RedColor);
            }
            else if (status == GameStatus.Victory)
            {
                DrawOverlayBanner(canvas, fieldX, fieldY, fieldW, fieldH, "WYGRANA! BRAWO! KLIKNIJ ABY ZAGRAC", GreenColor);
            }

            // Controls Footer
            SmallTextRenderer.Draw(canvas, "Ruch: Mysz / Strzalki  Start: Spacja  P: Pauza  R: Reset",
                contentX + 14, contentY + 398, MutedColor);
        }

        private void DrawOverlayBanner(Canvas canvas, int fx, int fy, int fw, int fh, string text, Color color)
        {
            int bw = 300;
            int bh = 34;
            int bx = fx + (fw - bw) / 2;
            int by = fy + (fh - bh) / 2 + 30;

            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 24), bx, by, bw, bh);
            canvas.DrawRectangle(Border, bx, by, bw, bh);
            SmallTextRenderer.DrawCentered(canvas, text, bx, by + 13, bw, color);
        }

        private static float Abs(float v) => v < 0 ? -v : v;
    }
}
