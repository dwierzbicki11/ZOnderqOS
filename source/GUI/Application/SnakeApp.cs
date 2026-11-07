using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SnakeApp : Application
    {
        private const int GridCols = 20;
        private const int GridRows = 20;
        private const int CellSize = 18;
        private const int MaxSegments = GridCols * GridRows;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color HeaderPanel = Color.FromArgb(27, 34, 41);
        private static readonly Color GridBg = Color.FromArgb(16, 21, 27);
        private static readonly Color GridLine = Color.FromArgb(24, 32, 42);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color HeadColor = Color.FromArgb(73, 163, 224);
        private static readonly Color BodyColor = Color.FromArgb(46, 117, 182);
        private static readonly Color FoodColor = Color.FromArgb(224, 83, 83);
        private static readonly Color FoodHighlight = Color.FromArgb(255, 160, 160);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(78, 185, 126);

        private enum GameStatus { Ready, Playing, Paused, GameOver }

        private readonly int[] snakeX = new int[MaxSegments];
        private readonly int[] snakeY = new int[MaxSegments];
        private int snakeLength;
        private int dirX;
        private int dirY;
        private int nextDirX;
        private int nextDirY;
        private int foodX;
        private int foodY;

        private GameStatus status = GameStatus.Ready;
        private uint score;
        private uint highScore;
        private long lastMoveTicks;
        private readonly Action closeCallback;
        private readonly Random rng = new Random();

        public SnakeApp(int x, int y, Action onClose) : base("Waz")
        {
            closeCallback = onClose;
            // 20 * 18 = 360 px grid. Window 416 x 480.
            Window = new Window(x, y, 416, 480, "Waz");
            Window.CloseAction = Close;
            ResetGame();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void ResetGame()
        {
            snakeLength = 3;
            int startX = GridCols / 2;
            int startY = GridRows / 2;

            for (int i = 0; i < snakeLength; i++)
            {
                snakeX[i] = startX - i;
                snakeY[i] = startY;
            }

            dirX = 1;
            dirY = 0;
            nextDirX = 1;
            nextDirY = 0;

            score = 0;
            status = GameStatus.Ready;
            SpawnFood();
            lastMoveTicks = Stopwatch.GetTimestamp();
        }

        private void SpawnFood()
        {
            bool onSnake;
            int attempts = 0;
            do
            {
                foodX = rng.Next(GridCols);
                foodY = rng.Next(GridRows);
                onSnake = false;
                for (int i = 0; i < snakeLength; i++)
                {
                    if (snakeX[i] == foodX && snakeY[i] == foodY)
                    {
                        onSnake = true;
                        break;
                    }
                }
                attempts++;
            } while (onSnake && attempts < 200);
        }

        private long GetMoveIntervalTicks()
        {
            if (Stopwatch.Frequency <= 0)
                return 1000000;

            // Speed scales with score: 150ms down to 60ms
            long ms = 150 - (long)(score * 3);
            if (ms < 60)
                ms = 60;

            return (Stopwatch.Frequency * ms) / 1000;
        }

        public override void Update()
        {
            if (status != GameStatus.Playing)
                return;

            long now = Stopwatch.GetTimestamp();
            long interval = GetMoveIntervalTicks();

            if (now - lastMoveTicks >= interval)
            {
                lastMoveTicks = now;
                Step();
            }
        }

        private void Step()
        {
            dirX = nextDirX;
            dirY = nextDirY;

            int newHeadX = snakeX[0] + dirX;
            int newHeadY = snakeY[0] + dirY;

            // Check wall collision
            if (newHeadX < 0 || newHeadX >= GridCols || newHeadY < 0 || newHeadY >= GridRows)
            {
                OnGameOver();
                return;
            }

            // Check self-collision
            for (int i = 0; i < snakeLength; i++)
            {
                if (snakeX[i] == newHeadX && snakeY[i] == newHeadY)
                {
                    OnGameOver();
                    return;
                }
            }

            // Check food collision
            bool ateFood = (newHeadX == foodX && newHeadY == foodY);

            // Move body segments
            int targetLength = ateFood ? snakeLength + 1 : snakeLength;
            if (targetLength > MaxSegments)
                targetLength = MaxSegments;

            for (int i = targetLength - 1; i > 0; i--)
            {
                snakeX[i] = snakeX[i - 1];
                snakeY[i] = snakeY[i - 1];
            }

            snakeX[0] = newHeadX;
            snakeY[0] = newHeadY;
            snakeLength = targetLength;

            if (ateFood)
            {
                score += 10;
                if (score > highScore)
                    highScore = score;
                SpawnFood();
            }
        }

        private void OnGameOver()
        {
            status = GameStatus.GameOver;
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

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (status == GameStatus.Ready)
                {
                    status = GameStatus.Playing;
                    lastMoveTicks = Stopwatch.GetTimestamp();
                }
                else if (status == GameStatus.Playing)
                {
                    status = GameStatus.Paused;
                }
                else if (status == GameStatus.Paused)
                {
                    status = GameStatus.Playing;
                    lastMoveTicks = Stopwatch.GetTimestamp();
                }
                else if (status == GameStatus.GameOver)
                {
                    ResetGame();
                    status = GameStatus.Playing;
                }
                return;
            }

            // Direction controls (Arrow keys or WASD)
            if (key.Key == ConsoleKeyEx.UpArrow || key.KeyChar == 'w' || key.KeyChar == 'W')
            {
                if (dirY != 1)
                {
                    nextDirX = 0;
                    nextDirY = -1;
                    if (status == GameStatus.Ready) status = GameStatus.Playing;
                }
            }
            else if (key.Key == ConsoleKeyEx.DownArrow || key.KeyChar == 's' || key.KeyChar == 'S')
            {
                if (dirY != -1)
                {
                    nextDirX = 0;
                    nextDirY = 1;
                    if (status == GameStatus.Ready) status = GameStatus.Playing;
                }
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                if (dirX != 1)
                {
                    nextDirX = -1;
                    nextDirY = 0;
                    if (status == GameStatus.Ready) status = GameStatus.Playing;
                }
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                if (dirX != -1)
                {
                    nextDirX = 1;
                    nextDirY = 0;
                    if (status == GameStatus.Ready) status = GameStatus.Playing;
                }
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            if (leftClicked && !leftWasClicked)
            {
                if (status == GameStatus.Ready)
                {
                    status = GameStatus.Playing;
                    lastMoveTicks = Stopwatch.GetTimestamp();
                }
                else if (status == GameStatus.GameOver)
                {
                    ResetGame();
                    status = GameStatus.Playing;
                }
            }

            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
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

            // Fill background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header panel (Score, Record, Status)
            int headerX = contentX + 8;
            int headerY = contentY + 6;
            int headerWidth = contentWidth - 16;
            int headerHeight = 36;
            canvas.DrawFilledRectangle(HeaderPanel, headerX, headerY, headerWidth, headerHeight);
            canvas.DrawFilledRectangle(Border, headerX, headerY + headerHeight - 1, headerWidth, 1);

            // Score
            SmallTextRenderer.Draw(canvas, "WYNIK", headerX + 10, headerY + 5, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, score, headerX + 10, headerY + 19, AccentColor);

            // High Score
            SmallTextRenderer.Draw(canvas, "REKORD", headerX + 90, headerY + 5, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, highScore, headerX + 90, headerY + 19, TextColor);

            // Status message (right side)
            string statusStr = "SPACJA: START";
            Color statusColor = MutedColor;
            if (status == GameStatus.Playing)
            {
                statusStr = "GRAJ";
                statusColor = AccentColor;
            }
            else if (status == GameStatus.Paused)
            {
                statusStr = "PAUZA";
                statusColor = Color.FromArgb(240, 200, 50);
            }
            else if (status == GameStatus.GameOver)
            {
                statusStr = "KONIEC GRY";
                statusColor = Color.FromArgb(240, 70, 70);
            }
            int sw = SmallTextRenderer.Width(statusStr);
            SmallTextRenderer.Draw(canvas, statusStr, headerX + headerWidth - sw - 10, headerY + 12, statusColor);

            // Board grid
            int gridOriginX = Window.X + (Window.Width - (GridCols * CellSize)) / 2;
            int gridOriginY = contentY + 48;

            canvas.DrawFilledRectangle(GridBg, gridOriginX, gridOriginY, GridCols * CellSize, GridRows * CellSize);
            canvas.DrawFilledRectangle(Border, gridOriginX - 1, gridOriginY - 1, GridCols * CellSize + 2, 1);
            canvas.DrawFilledRectangle(Border, gridOriginX - 1, gridOriginY + GridRows * CellSize, GridCols * CellSize + 2, 1);
            canvas.DrawFilledRectangle(Border, gridOriginX - 1, gridOriginY - 1, 1, GridRows * CellSize + 2);
            canvas.DrawFilledRectangle(Border, gridOriginX + GridCols * CellSize, gridOriginY - 1, 1, GridRows * CellSize + 2);

            // Food 🍎
            int fx = gridOriginX + foodX * CellSize;
            int fy = gridOriginY + foodY * CellSize;
            canvas.DrawFilledRectangle(FoodColor, fx + 2, fy + 2, CellSize - 4, CellSize - 4);
            canvas.DrawFilledRectangle(FoodHighlight, fx + 4, fy + 4, 3, 3);

            // Snake segments
            for (int i = snakeLength - 1; i >= 0; i--)
            {
                int sx = gridOriginX + snakeX[i] * CellSize;
                int sy = gridOriginY + snakeY[i] * CellSize;

                if (i == 0)
                {
                    // Head
                    canvas.DrawFilledRectangle(HeadColor, sx + 1, sy + 1, CellSize - 2, CellSize - 2);

                    // Eyes based on direction
                    int eyeX1 = sx + 4, eyeY1 = sy + 4;
                    int eyeX2 = sx + 11, eyeY2 = sy + 4;
                    if (dirX == 1) // Right
                    {
                        eyeX1 = sx + 12; eyeY1 = sy + 4;
                        eyeX2 = sx + 12; eyeY2 = sy + 11;
                    }
                    else if (dirX == -1) // Left
                    {
                        eyeX1 = sx + 3; eyeY1 = sy + 4;
                        eyeX2 = sx + 3; eyeY2 = sy + 11;
                    }
                    else if (dirY == 1) // Down
                    {
                        eyeX1 = sx + 4; eyeY1 = sy + 12;
                        eyeX2 = sx + 11; eyeY2 = sy + 12;
                    }
                    canvas.DrawFilledRectangle(TextColor, eyeX1, eyeY1, 2, 2);
                    canvas.DrawFilledRectangle(TextColor, eyeX2, eyeY2, 2, 2);
                }
                else
                {
                    // Body
                    canvas.DrawFilledRectangle(BodyColor, sx + 2, sy + 2, CellSize - 4, CellSize - 4);
                }
            }

            // Overlay message for Ready / GameOver / Paused
            if (status == GameStatus.Ready)
            {
                RenderCenterPrompt(canvas, gridOriginX, gridOriginY, "WCISNIJ SPACJE ABY ZACZAC", AccentColor);
            }
            else if (status == GameStatus.GameOver)
            {
                RenderCenterPrompt(canvas, gridOriginX, gridOriginY, "KONIEC GRY! WCISNIJ [R]", Color.FromArgb(240, 70, 70));
            }
            else if (status == GameStatus.Paused)
            {
                RenderCenterPrompt(canvas, gridOriginX, gridOriginY, "PAUZA", Color.FromArgb(240, 200, 50));
            }
        }

        private static void RenderCenterPrompt(Canvas canvas, int gx, int gy, string text, Color color)
        {
            int bw = GridCols * CellSize;
            int bh = GridRows * CellSize;
            int px = gx + 20;
            int py = gy + bh / 2 - 20;
            int pw = bw - 40;
            int ph = 40;

            canvas.DrawFilledRectangle(Color.FromArgb(10, 14, 18), px, py, pw, ph);
            canvas.DrawFilledRectangle(Border, px, py, pw, ph);
            SmallTextRenderer.DrawCentered(canvas, text, px, py + 14, pw, color);
        }
    }
}
