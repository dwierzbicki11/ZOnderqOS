using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

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
        private static readonly Color ControlPanel = Color.FromArgb(24, 30, 37);
        private static readonly Color ButtonNormal = Color.FromArgb(33, 42, 52);
        private static readonly Color ButtonHover = Color.FromArgb(48, 62, 78);
        private static readonly Color GridBg = Color.FromArgb(16, 21, 27);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color HeadColor = Color.FromArgb(73, 163, 224);
        private static readonly Color BodyColor = Color.FromArgb(46, 117, 182);
        private static readonly Color FoodColor = Color.FromArgb(224, 83, 83);
        private static readonly Color FoodHighlight = Color.FromArgb(255, 160, 160);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(78, 185, 126);
        private static readonly Color WarningColor = Color.FromArgb(240, 200, 50);
        private static readonly Color DangerColor = Color.FromArgb(240, 70, 70);

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
        private int updateFrames;
        private int hoveredButton = -1; // 0: Left, 1: Up, 2: Down, 3: Right, 4: Restart, 5: ActionBtn
        private readonly Action closeCallback;
        private readonly Random rng = new Random();

        public SnakeApp(int x, int y, Action onClose) : base("Waz")
        {
            closeCallback = onClose;
            // 20 * 18 = 360 px grid. Window 416 x 512.
            Window = new Window(x, y, 416, 512, "Waz");
            Window.CloseAction = Close;
            ResetGame();
        }

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        public override bool NeedsContinuousRedraw => status == GameStatus.Playing;

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
            updateFrames = 0;
        }

        private void StartGame()
        {
            status = GameStatus.Playing;
            lastMoveTicks = Stopwatch.GetTimestamp();
            updateFrames = 0;
        }

        private void TogglePause()
        {
            if (status == GameStatus.Playing)
            {
                status = GameStatus.Paused;
            }
            else if (status == GameStatus.Paused)
            {
                StartGame();
            }
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

        private long GetMoveIntervalMs()
        {
            // Speed scales with score: 140ms down to 50ms
            long ms = 140 - (long)(score * 3);
            if (ms < 50)
                ms = 50;
            return ms;
        }

        private long GetMoveIntervalTicks()
        {
            if (Stopwatch.Frequency <= 0)
                return 1000000;

            return (Stopwatch.Frequency * GetMoveIntervalMs()) / 1000;
        }

        public override void Update()
        {
            if (status != GameStatus.Playing)
                return;

            updateFrames++;
            long now = Stopwatch.GetTimestamp();
            long interval = GetMoveIntervalTicks();
            long targetFrames = Math.Max(2, (long)(GetMoveIntervalMs() / 16));

            bool timeTriggered = (Stopwatch.Frequency > 0 && lastMoveTicks > 0 && now - lastMoveTicks >= interval);
            bool frameTriggered = (updateFrames >= targetFrames);

            if (timeTriggered || frameTriggered)
            {
                lastMoveTicks = now;
                updateFrames = 0;
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

            bool ateFood = (newHeadX == foodX && newHeadY == foodY);

            // Check self-collision
            // If snake didn't eat food, the tail segment will vacate on this step.
            int checkSegments = ateFood ? snakeLength : snakeLength - 1;
            for (int i = 0; i < checkSegments; i++)
            {
                if (snakeX[i] == newHeadX && snakeY[i] == newHeadY)
                {
                    OnGameOver();
                    return;
                }
            }

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
                ZonderqOS.Hardware.Speaker.Tone(1050);
                SpawnFood();
            }
            else
            {
                ZonderqOS.Hardware.Speaker.Stop();
            }
        }

        private void OnGameOver()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            status = GameStatus.GameOver;
        }

        public void TryTurnUp()
        {
            if (dirY != 1 && nextDirY != 1)
            {
                nextDirX = 0;
                nextDirY = -1;
                if (status == GameStatus.Ready) StartGame();
            }
        }

        public void TryTurnDown()
        {
            if (dirY != -1 && nextDirY != -1)
            {
                nextDirX = 0;
                nextDirY = 1;
                if (status == GameStatus.Ready) StartGame();
            }
        }

        public void TryTurnLeft()
        {
            if (dirX != 1 && nextDirX != 1)
            {
                nextDirX = -1;
                nextDirY = 0;
                if (status == GameStatus.Ready) StartGame();
            }
        }

        public void TryTurnRight()
        {
            if (dirX != -1 && nextDirX != -1)
            {
                nextDirX = 1;
                nextDirY = 0;
                if (status == GameStatus.Ready) StartGame();
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
                StartGame();
                return;
            }

            if (key.Key == ConsoleKeyEx.Spacebar)
            {
                if (status == GameStatus.Ready)
                    StartGame();
                else if (status == GameStatus.Playing)
                    TogglePause();
                else if (status == GameStatus.Paused)
                    StartGame();
                else if (status == GameStatus.GameOver)
                {
                    ResetGame();
                    StartGame();
                }
                return;
            }

            // Direction controls (Arrow keys or WASD)
            if (key.Key == ConsoleKeyEx.UpArrow || key.KeyChar == 'w' || key.KeyChar == 'W')
            {
                TryTurnUp();
            }
            else if (key.Key == ConsoleKeyEx.DownArrow || key.KeyChar == 's' || key.KeyChar == 'S')
            {
                TryTurnDown();
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                TryTurnLeft();
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                TryTurnRight();
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
            int contentWidth = Math.Max(1, Window.Width - 16);

            // Header action button hit-test
            int headerX = contentX + 8;
            int headerY = contentY + 6;
            int headerWidth = contentWidth - 16;
            int actBtnW = 86;
            int actBtnH = 24;
            int actBtnX = headerX + headerWidth - actBtnW - 6;
            int actBtnY = headerY + 6;

            // Bottom control bar hit-test
            int barY = contentY + 412;
            int startX = contentX + (contentWidth - 274) / 2;
            int btnY = barY + 3;

            // Hover detection
            hoveredButton = -1;
            if (mouseX >= actBtnX && mouseX < actBtnX + actBtnW &&
                mouseY >= actBtnY && mouseY < actBtnY + actBtnH)
            {
                hoveredButton = 5;
            }
            else if (mouseY >= btnY && mouseY < btnY + 28)
            {
                if (mouseX >= startX && mouseX < startX + 38) hoveredButton = 0; // Left
                else if (mouseX >= startX + 44 && mouseX < startX + 82) hoveredButton = 1; // Up
                else if (mouseX >= startX + 88 && mouseX < startX + 126) hoveredButton = 2; // Down
                else if (mouseX >= startX + 132 && mouseX < startX + 170) hoveredButton = 3; // Right
                else if (mouseX >= startX + 180 && mouseX < startX + 274) hoveredButton = 4; // Restart
            }

            if (leftClicked && !leftWasClicked)
            {
                // 1. Header action button click
                if (hoveredButton == 5)
                {
                    if (status == GameStatus.Ready)
                        StartGame();
                    else if (status == GameStatus.Playing)
                        TogglePause();
                    else if (status == GameStatus.Paused)
                        StartGame();
                    else if (status == GameStatus.GameOver)
                    {
                        ResetGame();
                        StartGame();
                    }
                    return;
                }

                // 2. Bottom control buttons click
                if (hoveredButton >= 0)
                {
                    switch (hoveredButton)
                    {
                        case 0: TryTurnLeft(); break;
                        case 1: TryTurnUp(); break;
                        case 2: TryTurnDown(); break;
                        case 3: TryTurnRight(); break;
                        case 4:
                            ResetGame();
                            StartGame();
                            break;
                    }
                    return;
                }

                // 3. Grid click (Steering or starting)
                int gridOriginX = Window.X + (Window.Width - (GridCols * CellSize)) / 2;
                int gridOriginY = contentY + 46;
                int relX = mouseX - gridOriginX;
                int relY = mouseY - gridOriginY;

                if (relX >= 0 && relX < GridCols * CellSize && relY >= 0 && relY < GridRows * CellSize)
                {
                    if (status == GameStatus.Ready)
                    {
                        StartGame();
                    }
                    else if (status == GameStatus.GameOver)
                    {
                        ResetGame();
                        StartGame();
                    }
                    else if (status == GameStatus.Paused)
                    {
                        StartGame();
                    }
                    else if (status == GameStatus.Playing)
                    {
                        int clickedCol = relX / CellSize;
                        int clickedRow = relY / CellSize;
                        int headCol = snakeX[0];
                        int headRow = snakeY[0];
                        int dx = clickedCol - headCol;
                        int dy = clickedRow - headRow;

                        if (Math.Abs(dx) >= Math.Abs(dy))
                        {
                            if (dx > 0 && dirX != -1 && nextDirX != -1) TryTurnRight();
                            else if (dx < 0 && dirX != 1 && nextDirX != 1) TryTurnLeft();
                            else if (dy > 0 && dirY != -1 && nextDirY != -1) TryTurnDown();
                            else if (dy < 0 && dirY != 1 && nextDirY != 1) TryTurnUp();
                        }
                        else
                        {
                            if (dy > 0 && dirY != -1 && nextDirY != -1) TryTurnDown();
                            else if (dy < 0 && dirY != 1 && nextDirY != 1) TryTurnUp();
                            else if (dx > 0 && dirX != -1 && nextDirX != -1) TryTurnRight();
                            else if (dx < 0 && dirX != 1 && nextDirX != 1) TryTurnLeft();
                        }
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

            // Fill background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header panel (Score, Record, Status Button)
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
            SmallTextRenderer.Draw(canvas, "REKORD", headerX + 80, headerY + 5, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, highScore, headerX + 80, headerY + 19, TextColor);

            // Header Action Button
            int actBtnW = 86;
            int actBtnH = 24;
            int actBtnX = headerX + headerWidth - actBtnW - 6;
            int actBtnY = headerY + 6;
            Color actBg = (hoveredButton == 5) ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(actBg, actBtnX, actBtnY, actBtnW, actBtnH);
            canvas.DrawFilledRectangle(Border, actBtnX, actBtnY, actBtnW, 1);
            canvas.DrawFilledRectangle(Border, actBtnX, actBtnY + actBtnH - 1, actBtnW, 1);
            canvas.DrawFilledRectangle(Border, actBtnX, actBtnY, 1, actBtnH);
            canvas.DrawFilledRectangle(Border, actBtnX + actBtnW - 1, actBtnY, 1, actBtnH);

            string actText;
            Color actColor;
            if (status == GameStatus.Ready)
            {
                actText = "START";
                actColor = AccentColor;
            }
            else if (status == GameStatus.Playing)
            {
                actText = "PAUZA";
                actColor = WarningColor;
            }
            else if (status == GameStatus.Paused)
            {
                actText = "WZNOW";
                actColor = AccentColor;
            }
            else
            {
                actText = "OD NOWA";
                actColor = DangerColor;
            }
            SmallTextRenderer.DrawCentered(canvas, actText, actBtnX, actBtnY + 8, actBtnW, actColor);

            // Board grid
            int gridOriginX = Window.X + (Window.Width - (GridCols * CellSize)) / 2;
            int gridOriginY = contentY + 46;

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

            // Bottom control bar (On-screen buttons)
            int barY = contentY + 412;
            int barH = 36;
            canvas.DrawFilledRectangle(ControlPanel, headerX, barY, headerWidth, barH);
            canvas.DrawFilledRectangle(Border, headerX, barY, headerWidth, 1);

            int startX = contentX + (contentWidth - 274) / 2;
            int btnY = barY + 4;

            DrawDirButton(canvas, startX, btnY, 38, 28, 0, hoveredButton == 0); // Left
            DrawDirButton(canvas, startX + 44, btnY, 38, 28, 1, hoveredButton == 1); // Up
            DrawDirButton(canvas, startX + 88, btnY, 38, 28, 2, hoveredButton == 2); // Down
            DrawDirButton(canvas, startX + 132, btnY, 38, 28, 3, hoveredButton == 3); // Right

            // Reset button
            int rstX = startX + 180;
            int rstW = 94;
            Color rstBg = (hoveredButton == 4) ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(rstBg, rstX, btnY, rstW, 28);
            canvas.DrawFilledRectangle(Border, rstX, btnY, rstW, 1);
            canvas.DrawFilledRectangle(Border, rstX, btnY + 27, rstW, 1);
            canvas.DrawFilledRectangle(Border, rstX, btnY, 1, 28);
            canvas.DrawFilledRectangle(Border, rstX + rstW - 1, btnY, 1, 28);
            SmallTextRenderer.DrawCentered(canvas, "NOWA GRA", rstX, btnY + 10, rstW, TextColor);

            // Overlay message for Ready / GameOver / Paused
            if (status == GameStatus.Ready)
            {
                RenderPrompt(canvas, gridOriginX, gridOriginY, "KLIKNIJ LUB WCISNIJ SPACJE", "ABY ROZPOCZAC GRE", AccentColor);
            }
            else if (status == GameStatus.GameOver)
            {
                RenderPrompt(canvas, gridOriginX, gridOriginY, "KONIEC GRY!", "KLIKNIJ ABY ZAGRAC PONOWNIE", DangerColor);
            }
            else if (status == GameStatus.Paused)
            {
                RenderPrompt(canvas, gridOriginX, gridOriginY, "PAUZA", "KLIKNIJ ABY WZNOWIC", WarningColor);
            }
        }

        private static void DrawDirButton(Canvas canvas, int x, int y, int w, int h, int dir, bool hovered)
        {
            Color bg = hovered ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawFilledRectangle(Border, x, y, w, 1);
            canvas.DrawFilledRectangle(Border, x, y + h - 1, w, 1);
            canvas.DrawFilledRectangle(Border, x, y, 1, h);
            canvas.DrawFilledRectangle(Border, x + w - 1, y, 1, h);

            int cx = x + w / 2;
            int cy = y + h / 2;
            Color arrowColor = hovered ? TextColor : MutedColor;

            // Draw crisp arrow triangles
            if (dir == 0) // Left ◄
            {
                for (int i = 0; i <= 4; i++)
                    canvas.DrawFilledRectangle(arrowColor, cx - 3 + i, cy - i, 1, i * 2 + 1);
            }
            else if (dir == 1) // Up ▲
            {
                for (int i = 0; i <= 4; i++)
                    canvas.DrawFilledRectangle(arrowColor, cx - i, cy - 3 + i, i * 2 + 1, 1);
            }
            else if (dir == 2) // Down ▼
            {
                for (int i = 0; i <= 4; i++)
                    canvas.DrawFilledRectangle(arrowColor, cx - i, cy + 3 - i, i * 2 + 1, 1);
            }
            else if (dir == 3) // Right ►
            {
                for (int i = 0; i <= 4; i++)
                    canvas.DrawFilledRectangle(arrowColor, cx + 3 - i, cy - i, 1, i * 2 + 1);
            }
        }

        private static void RenderPrompt(Canvas canvas, int gx, int gy, string line1, string line2, Color color)
        {
            int bw = GridCols * CellSize;
            int bh = GridRows * CellSize;
            int pw = 240;
            int ph = 54;
            int px = gx + (bw - pw) / 2;
            int py = gy + (bh - ph) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(12, 16, 21), px, py, pw, ph);
            canvas.DrawFilledRectangle(Border, px, py, pw, ph);
            canvas.DrawFilledRectangle(Border, px, py, pw, 1);
            canvas.DrawFilledRectangle(Border, px, py + ph - 1, pw, 1);
            canvas.DrawFilledRectangle(Border, px, py, 1, ph);
            canvas.DrawFilledRectangle(Border, px + pw - 1, py, 1, ph);

            SmallTextRenderer.DrawCentered(canvas, line1, px, py + 12, pw, color);
            SmallTextRenderer.DrawCentered(canvas, line2, px, py + 30, pw, MutedColor);
        }
    }
}
