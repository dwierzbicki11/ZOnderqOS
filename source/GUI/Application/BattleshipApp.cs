using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Hardware;

namespace ZonderqOS.GUI.Apps
{
    public sealed class BattleshipApp : Application
    {
        private const int GridSize = 10;
        private const int CellSize = 22;
        private const int HeaderHeight = 38;

        // UI Colors
        private static readonly Color Surface = Color.FromArgb(16, 22, 32);
        private static readonly Color Panel = Color.FromArgb(24, 34, 48);
        private static readonly Color Border = Color.FromArgb(46, 68, 96);
        private static readonly Color WaterColor = Color.FromArgb(12, 28, 48);
        private static readonly Color WaterGrid = Color.FromArgb(28, 52, 82);
        private static readonly Color ShipColor = Color.FromArgb(45, 140, 225);
        private static readonly Color ShipBorder = Color.FromArgb(70, 180, 255);
        private static readonly Color HitColor = Color.FromArgb(235, 60, 60);
        private static readonly Color MissColor = Color.FromArgb(75, 105, 135);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color MutedColor = Color.FromArgb(130, 155, 180);
        private static readonly Color AccentColor = Color.FromArgb(40, 150, 240);
        private static readonly Color GreenColor = Color.FromArgb(50, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);

        private enum GamePhase
        {
            Placement = 0,
            PlayerTurn = 1,
            EnemyTurn = 2,
            GameOver = 3
        }

        // Cell States: 0 = Water, 1 = Ship, 2 = Miss, 3 = Hit
        private readonly byte[,] playerGrid = new byte[GridSize, GridSize];
        private readonly byte[,] enemyGrid = new byte[GridSize, GridSize];
        private readonly bool[,] enemyVisible = new bool[GridSize, GridSize];

        private GamePhase phase = GamePhase.Placement;
        private string statusMessage = "Rozstaw swoja flote lub kliknij [LOSUJ FLOTE], nastepnie [START BITWY].";
        private Color statusColor = TextColor;
        private int playerHits = 0;
        private int enemyHits = 0;
        private const int TotalShipCells = 20; // 4 + 3*2 + 2*3 + 1*4 = 20

        // AI Hunting state
        private readonly List<Point> aiTargetQueue = new List<Point>(16);
        private readonly Random rng = new Random();
        private readonly Action closeCallback;

        // Ship lengths: 4, 3, 3, 2, 2, 2, 1, 1, 1, 1
        private static readonly int[] ShipSizes = new int[] { 4, 3, 3, 2, 2, 2, 1, 1, 1, 1 };

        public BattleshipApp(int x, int y, Action onClose) : base("Statki")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 620, 420, "Bitwa Morska (Statki) - Flota Wojenna");
            Window.CloseAction = Close;

            StartNewGame();
        }

        public override void Close()
        {
            Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        private void StartNewGame()
        {
            ClearGrid(playerGrid);
            ClearGrid(enemyGrid);
            for (int r = 0; r < GridSize; r++)
                for (int c = 0; c < GridSize; c++)
                    enemyVisible[r, c] = false;

            playerHits = 0;
            enemyHits = 0;
            aiTargetQueue.Clear();

            // Auto place both fleets
            RandomizeFleet(playerGrid);
            RandomizeFleet(enemyGrid);

            phase = GamePhase.Placement;
            statusMessage = "Flota rozstawiona! Kliknij [START BITWY] aby rozpoczac pojedynek.";
            statusColor = GoldColor;
        }

        private static void ClearGrid(byte[,] grid)
        {
            for (int r = 0; r < GridSize; r++)
                for (int c = 0; c < GridSize; c++)
                    grid[r, c] = 0;
        }

        private void RandomizeFleet(byte[,] grid)
        {
            ClearGrid(grid);
            for (int i = 0; i < ShipSizes.Length; i++)
            {
                int len = ShipSizes[i];
                bool placed = false;
                int attempts = 0;

                while (!placed && attempts < 200)
                {
                    attempts++;
                    bool horizontal = rng.Next(0, 2) == 0;
                    int r = rng.Next(0, horizontal ? GridSize : GridSize - len + 1);
                    int c = rng.Next(0, horizontal ? GridSize - len + 1 : GridSize);

                    if (CanPlaceShip(grid, r, c, len, horizontal))
                    {
                        for (int k = 0; k < len; k++)
                        {
                            int pr = horizontal ? r : r + k;
                            int pc = horizontal ? c + k : c;
                            grid[pr, pc] = 1; // Ship
                        }
                        placed = true;
                    }
                }
            }
        }

        private static bool CanPlaceShip(byte[,] grid, int row, int col, int length, bool horizontal)
        {
            // Check with 1-cell border margin around ship
            int rStart = Math.Max(0, row - 1);
            int rEnd = Math.Min(GridSize - 1, horizontal ? row + 1 : row + length);
            int cStart = Math.Max(0, col - 1);
            int cEnd = Math.Min(GridSize - 1, horizontal ? col + length : col + 1);

            for (int r = rStart; r <= rEnd; r++)
            {
                for (int c = cStart; c <= cEnd; c++)
                {
                    if (grid[r, c] != 0)
                        return false;
                }
            }
            return true;
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            if (leftClicked && !leftWasClicked)
            {
                int wx = Window.X;
                int wy = Window.Y + HeaderHeight;

                // Buttons row at wy + 10
                int btnY = wy + 10;
                // [NOWA GRA] wx + 16, w: 90
                if (Hit(mouseX, mouseY, wx + 16, btnY, 90, 24))
                {
                    StartNewGame();
                    return;
                }

                // [LOSUJ FLOTĘ] wx + 114, w: 105 (available during placement)
                if (phase == GamePhase.Placement && Hit(mouseX, mouseY, wx + 114, btnY, 105, 24))
                {
                    RandomizeFleet(playerGrid);
                    statusMessage = "Przetasowano pozycje Twoich okretow.";
                    statusColor = AccentColor;
                    Speaker.Beep(440, 2);
                    return;
                }

                // [START BITWY] wx + 226, w: 115 (available during placement)
                if (phase == GamePhase.Placement && Hit(mouseX, mouseY, wx + 226, btnY, 115, 24))
                {
                    phase = GamePhase.PlayerTurn;
                    statusMessage = "BITWA ROZPOCZETA! Wybierz cel na mapie Wody Wroga.";
                    statusColor = GreenColor;
                    Speaker.Beep(659, 4);
                    return;
                }

                // Player fires at Enemy Waters (Right grid)
                if (phase == GamePhase.PlayerTurn)
                {
                    int enemyGridX = wx + 340;
                    int enemyGridY = wy + 82;
                    int gw = GridSize * CellSize;

                    if (Hit(mouseX, mouseY, enemyGridX, enemyGridY, gw, gw))
                    {
                        int col = (mouseX - enemyGridX) / CellSize;
                        int row = (mouseY - enemyGridY) / CellSize;

                        if (row >= 0 && row < GridSize && col >= 0 && col < GridSize)
                        {
                            HandlePlayerShot(row, col);
                        }
                    }
                }
            }
        }

        private void HandlePlayerShot(int row, int col)
        {
            if (enemyVisible[row, col])
            {
                statusMessage = "To pole zostalo juz ostrzelane! Wybierz inny sektor.";
                statusColor = GoldColor;
                return;
            }

            enemyVisible[row, col] = true;

            if (enemyGrid[row, col] == 1) // Hit!
            {
                enemyGrid[row, col] = 3; // Marked as hit
                playerHits++;
                Speaker.Beep(784, 5); // High explosion tone

                if (playerHits >= TotalShipCells)
                {
                    phase = GamePhase.GameOver;
                    statusMessage = "ZWYCIESTWO! Zatopiles wszystkie 10 okretow wroga!";
                    statusColor = GreenColor;
                    Speaker.Beep(1046, 8);
                }
                else
                {
                    statusMessage = "TRAFIENIE! Trafiles wrogi okret! Masz dodatkowy strzal.";
                    statusColor = RedColor;
                }
            }
            else // Miss
            {
                enemyGrid[row, col] = 2; // Marked as miss
                Speaker.Beep(260, 3); // Low splash tone
                statusMessage = "Pudlo! Torpeda trafila w wode. Tura floty przeciwnika...";
                statusColor = MutedColor;

                // Trigger AI Turn
                ExecuteAiTurn();
            }
        }

        private void ExecuteAiTurn()
        {
            int shotRow = -1;
            int shotCol = -1;

            // 1. Check if AI has queued targets from a previous hit
            while (aiTargetQueue.Count > 0)
            {
                Point target = aiTargetQueue[0];
                aiTargetQueue.RemoveAt(0);

                if (target.X >= 0 && target.X < GridSize && target.Y >= 0 && target.Y < GridSize)
                {
                    byte state = playerGrid[target.Y, target.X];
                    if (state == 0 || state == 1)
                    {
                        shotRow = target.Y;
                        shotCol = target.X;
                        break;
                    }
                }
            }

            // 2. If no targets queued, choose a random untargeted cell
            if (shotRow < 0)
            {
                int attempts = 0;
                while (attempts < 200)
                {
                    attempts++;
                    int r = rng.Next(0, GridSize);
                    int c = rng.Next(0, GridSize);
                    byte state = playerGrid[r, c];
                    if (state == 0 || state == 1)
                    {
                        shotRow = r;
                        shotCol = c;
                        break;
                    }
                }
            }

            if (shotRow >= 0 && shotCol >= 0)
            {
                if (playerGrid[shotRow, shotCol] == 1) // AI hit player ship
                {
                    playerGrid[shotRow, shotCol] = 3;
                    enemyHits++;
                    Speaker.Beep(330, 4);

                    // Queue adjacent neighbors (hunt mode)
                    aiTargetQueue.Add(new Point(shotCol + 1, shotRow));
                    aiTargetQueue.Add(new Point(shotCol - 1, shotRow));
                    aiTargetQueue.Add(new Point(shotCol, shotRow + 1));
                    aiTargetQueue.Add(new Point(shotCol, shotRow - 1));

                    if (enemyHits >= TotalShipCells)
                    {
                        phase = GamePhase.GameOver;
                        statusMessage = "PORAZKA! Wrog zatopil Twoja flote. Sprobuj ponownie!";
                        statusColor = RedColor;
                    }
                    else
                    {
                        statusMessage = "Wrog trafil Twoj okret w sektorze " + (char)('A' + shotCol) + (shotRow + 1) + "! Twoja tura.";
                        statusColor = RedColor;
                    }
                }
                else
                {
                    playerGrid[shotRow, shotCol] = 2; // AI Miss
                    statusMessage = "Wrog spudlowal w sektorze " + (char)('A' + shotCol) + (shotRow + 1) + ". Twoja tura!";
                    statusColor = GreenColor;
                }
            }

            if (phase != GamePhase.GameOver)
            {
                phase = GamePhase.PlayerTurn;
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - HeaderHeight;

            Window.Render(canvas);

            // Window background
            canvas.DrawFilledRectangle(Surface, wx, wy, ww, wh);

            // Controls Toolbar (wy + 8, h: 28)
            int btnY = wy + 8;
            DrawButton(canvas, "NOWA GRA", wx + 14, btnY, 90, 24, Panel, Border, TextColor);
            if (phase == GamePhase.Placement)
            {
                DrawButton(canvas, "LOSUJ FLOTE", wx + 110, btnY, 105, 24, Panel, Border, AccentColor);
                DrawButton(canvas, "START BITWY", wx + 222, btnY, 115, 24, GreenColor, Border, Color.Black);
            }

            // Status Panel (wy + 40, h: 26)
            canvas.DrawFilledRectangle(Panel, wx + 14, wy + 38, ww - 28, 24);
            canvas.DrawRectangle(Border, wx + 14, wy + 38, ww - 28, 24);
            SmallTextRenderer.Draw(canvas, statusMessage, wx + 22, wy + 44, statusColor);

            // Left Grid: Twoja Flota
            int leftGridX = wx + 36;
            int gridY = wy + 86;
            SmallTextRenderer.Draw(canvas, "TWOJA FLOTA (Trafienia wroga: " + enemyHits + "/" + TotalShipCells + ")", leftGridX, gridY - 18, AccentColor);
            RenderGrid(canvas, leftGridX, gridY, playerGrid, true);

            // Right Grid: Wody Wroga
            int rightGridX = wx + 340;
            SmallTextRenderer.Draw(canvas, "WODY WROGA (Twoje trafienia: " + playerHits + "/" + TotalShipCells + ")", rightGridX, gridY - 18, GoldColor);
            RenderGrid(canvas, rightGridX, gridY, enemyGrid, false);

            // Footer Legend (wy + 322)
            int legY = wy + 322;
            canvas.DrawFilledRectangle(Panel, wx + 14, legY, ww - 28, 48);
            canvas.DrawRectangle(Border, wx + 14, legY, ww - 28, 48);

            // Legend Items
            canvas.DrawFilledRectangle(ShipColor, wx + 24, legY + 8, 14, 14);
            SmallTextRenderer.Draw(canvas, "Twoj okret", wx + 44, legY + 10, TextColor);

            canvas.DrawFilledRectangle(HitColor, wx + 140, legY + 8, 14, 14);
            SmallTextRenderer.Draw(canvas, "Trafiony (Ogien)", wx + 160, legY + 10, HitColor);

            canvas.DrawFilledRectangle(MissColor, wx + 290, legY + 8, 14, 14);
            SmallTextRenderer.Draw(canvas, "Pudlo (Woda)", wx + 310, legY + 10, MutedColor);

            SmallTextRenderer.Draw(canvas, "Sklad floty: 1x [4-maszt]  2x [3-maszt]  3x [2-maszt]  4x [1-maszt] (Laczenie: 10 okretow)", wx + 24, legY + 30, MutedColor);
        }

        private void RenderGrid(Canvas canvas, int gx, int gy, byte[,] grid, bool showShips)
        {
            // Column Headers (A-J)
            for (int c = 0; c < GridSize; c++)
            {
                string colLetter = ((char)('A' + c)).ToString();
                SmallTextRenderer.DrawCentered(canvas, colLetter, gx + c * CellSize, gy - 14, CellSize, MutedColor);
            }

            // Row Headers (1-10)
            for (int r = 0; r < GridSize; r++)
            {
                string rowNum = (r + 1).ToString();
                SmallTextRenderer.Draw(canvas, rowNum, gx - 16, gy + r * CellSize + 5, MutedColor);
            }

            // Grid Cells
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    int cx = gx + c * CellSize;
                    int cy = gy + r * CellSize;
                    byte state = grid[r, c];

                    Color cellBg = WaterColor;

                    if (state == 1 && showShips)
                    {
                        cellBg = ShipColor;
                    }
                    else if (state == 2) // Miss
                    {
                        cellBg = MissColor;
                    }
                    else if (state == 3) // Hit
                    {
                        cellBg = HitColor;
                    }

                    canvas.DrawFilledRectangle(cellBg, cx, cy, CellSize - 1, CellSize - 1);
                    canvas.DrawRectangle(WaterGrid, cx, cy, CellSize, CellSize);

                    // Markers
                    if (state == 2) // Dot for miss
                    {
                        canvas.DrawFilledRectangle(Color.FromArgb(180, 205, 230), cx + CellSize / 2 - 2, cy + CellSize / 2 - 2, 4, 4);
                    }
                    else if (state == 3) // Cross for hit
                    {
                        int pad = 4;
                        canvas.DrawLine(Color.White, cx + pad, cy + pad, cx + CellSize - pad, cy + CellSize - pad);
                        canvas.DrawLine(Color.White, cx + CellSize - pad, cy + pad, cx + pad, cy + CellSize - pad);
                    }
                }
            }
        }

        private static void DrawButton(Canvas canvas, string text, int x, int y, int w, int h, Color fill, Color border, Color textColor)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, fill, border);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 10) / 2, w, textColor);
        }
    }
}
