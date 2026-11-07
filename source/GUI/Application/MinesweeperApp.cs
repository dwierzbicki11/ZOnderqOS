using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class MinesweeperApp : Application
    {
        private const int GridCols = 9;
        private const int GridRows = 9;
        private const int TotalMines = 10;
        private const int CellSize = 32;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color HeaderPanel = Color.FromArgb(27, 34, 41);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color CellUnrevealed = Color.FromArgb(42, 53, 66);
        private static readonly Color CellUnrevealedHover = Color.FromArgb(52, 65, 80);
        private static readonly Color CellRevealed = Color.FromArgb(24, 30, 38);
        private static readonly Color CellMineExploded = Color.FromArgb(160, 40, 40);
        private static readonly Color CellHighlight = Color.FromArgb(64, 80, 99);
        private static readonly Color CellShadow = Color.FromArgb(16, 21, 27);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color FlagColor = Color.FromArgb(230, 60, 60);

        private static readonly Color[] NumberColors =
        {
            Color.Transparent,
            Color.FromArgb(90, 160, 240),  // 1: Blue
            Color.FromArgb(80, 200, 120),  // 2: Green
            Color.FromArgb(240, 80, 80),   // 3: Red
            Color.FromArgb(180, 100, 240), // 4: Purple
            Color.FromArgb(240, 160, 50),  // 5: Orange
            Color.FromArgb(50, 220, 220),  // 6: Cyan
            Color.FromArgb(220, 220, 220), // 7: White
            Color.FromArgb(150, 150, 150)  // 8: Gray
        };

        private enum GameStatus { Ready, Playing, Won, Lost }

        private struct Cell
        {
            public bool IsMine;
            public bool IsRevealed;
            public bool IsFlagged;
            public byte AdjacentMines;
        }

        private readonly Cell[] cells = new Cell[GridCols * GridRows];
        private GameStatus status = GameStatus.Ready;
        private long gameStartTicks;
        private uint elapsedSeconds;
        private int remainingMines = TotalMines;
        private int explodedIndex = -1;
        private int hoveredCell = -1;
        private bool isLeftHeld;
        private readonly Action closeCallback;
        private readonly Random rng = new Random();

        public MinesweeperApp(int x, int y, Action onClose) : base("Saper")
        {
            closeCallback = onClose;
            // 9 * 32 = 288 + 32 margin = 320 content width. Window 344 x 420.
            Window = new Window(x, y, 344, 420, "Saper");
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
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i].IsMine = false;
                cells[i].IsRevealed = false;
                cells[i].IsFlagged = false;
                cells[i].AdjacentMines = 0;
            }
            status = GameStatus.Ready;
            gameStartTicks = 0;
            elapsedSeconds = 0;
            remainingMines = TotalMines;
            explodedIndex = -1;
        }

        private void GenerateMines(int safeCol, int safeRow)
        {
            int placed = 0;
            while (placed < TotalMines)
            {
                int r = rng.Next(GridRows);
                int c = rng.Next(GridCols);
                // Safe zone: clicked cell and immediate neighbors must not contain mines
                if (Math.Abs(r - safeRow) <= 1 && Math.Abs(c - safeCol) <= 1)
                    continue;

                int idx = r * GridCols + c;
                if (!cells[idx].IsMine)
                {
                    cells[idx].IsMine = true;
                    placed++;
                }
            }

            // Calculate adjacent counts
            for (int r = 0; r < GridRows; r++)
            {
                for (int c = 0; c < GridCols; c++)
                {
                    int idx = r * GridCols + c;
                    if (cells[idx].IsMine)
                        continue;

                    byte count = 0;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int nr = r + dr;
                            int nc = c + dc;
                            if (nr >= 0 && nr < GridRows && nc >= 0 && nc < GridCols)
                            {
                                if (cells[nr * GridCols + nc].IsMine)
                                    count++;
                            }
                        }
                    }
                    cells[idx].AdjacentMines = count;
                }
            }
        }

        public override void Update()
        {
            if (status == GameStatus.Playing && Stopwatch.Frequency > 0 && gameStartTicks > 0)
            {
                long elapsed = Stopwatch.GetTimestamp() - gameStartTicks;
                if (elapsed >= 0)
                    elapsedSeconds = (uint)(elapsed / Stopwatch.Frequency);
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

            if (key.Key == ConsoleKeyEx.R || key.Key == ConsoleKeyEx.Spacebar)
            {
                ResetGame();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            isLeftHeld = leftClicked;

            int gridOriginX = Window.X + (Window.Width - (GridCols * CellSize)) / 2;
            int gridOriginY = Window.Y + 95;

            // Hover tracking
            int relX = mouseX - gridOriginX;
            int relY = mouseY - gridOriginY;
            if (relX >= 0 && relX < GridCols * CellSize && relY >= 0 && relY < GridRows * CellSize)
            {
                int col = relX / CellSize;
                int row = relY / CellSize;
                hoveredCell = row * GridCols + col;
            }
            else
            {
                hoveredCell = -1;
            }

            // Face button click (Restart)
            int faceX = Window.X + (Window.Width - 32) / 2;
            int faceY = Window.Y + 48;
            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= faceX && mouseX < faceX + 32 && mouseY >= faceY && mouseY < faceY + 32)
                {
                    ResetGame();
                    return;
                }
            }

            // Left click on grid
            if (leftClicked && !leftWasClicked && hoveredCell >= 0)
            {
                OnCellLeftClick(hoveredCell % GridCols, hoveredCell / GridCols);
                return;
            }

            // Right click on grid (Flag)
            if (rightClicked && !rightWasClicked && hoveredCell >= 0)
            {
                OnCellRightClick(hoveredCell);
                return;
            }

            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);
        }

        private void OnCellLeftClick(int col, int row)
        {
            int idx = row * GridCols + col;
            if (cells[idx].IsFlagged || cells[idx].IsRevealed)
                return;

            if (status == GameStatus.Won || status == GameStatus.Lost)
                return;

            if (status == GameStatus.Ready)
            {
                GenerateMines(col, row);
                status = GameStatus.Playing;
                gameStartTicks = Stopwatch.GetTimestamp();
            }

            if (cells[idx].IsMine)
            {
                // Game Over!
                status = GameStatus.Lost;
                explodedIndex = idx;
                // Reveal all mines
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].IsMine)
                        cells[i].IsRevealed = true;
                }
                return;
            }

            // Reveal safe cells via BFS
            RevealCellFlood(col, row);

            // Check Win Condition
            int unrevealedCount = 0;
            for (int i = 0; i < cells.Length; i++)
            {
                if (!cells[i].IsRevealed)
                    unrevealedCount++;
            }

            if (unrevealedCount == TotalMines)
            {
                status = GameStatus.Won;
                remainingMines = 0;
                for (int i = 0; i < cells.Length; i++)
                {
                    if (cells[i].IsMine)
                        cells[i].IsFlagged = true;
                }
            }
        }

        private void RevealCellFlood(int startCol, int startRow)
        {
            // Fixed-size stack for zero allocation
            int[] stack = new int[GridCols * GridRows];
            int stackPtr = 0;

            stack[stackPtr++] = startRow * GridCols + startCol;

            while (stackPtr > 0)
            {
                int curr = stack[--stackPtr];
                if (cells[curr].IsRevealed || cells[curr].IsFlagged)
                    continue;

                cells[curr].IsRevealed = true;

                // If cell has 0 neighboring mines, cascade reveal
                if (cells[curr].AdjacentMines == 0 && !cells[curr].IsMine)
                {
                    int r = curr / GridCols;
                    int c = curr % GridCols;
                    for (int dr = -1; dr <= 1; dr++)
                    {
                        for (int dc = -1; dc <= 1; dc++)
                        {
                            int nr = r + dr;
                            int nc = c + dc;
                            if (nr >= 0 && nr < GridRows && nc >= 0 && nc < GridCols)
                            {
                                int nidx = nr * GridCols + nc;
                                if (!cells[nidx].IsRevealed && !cells[nidx].IsFlagged)
                                {
                                    stack[stackPtr++] = nidx;
                                }
                            }
                        }
                    }
                }
            }
        }

        private void OnCellRightClick(int idx)
        {
            if (status != GameStatus.Playing && status != GameStatus.Ready)
                return;

            if (cells[idx].IsRevealed)
                return;

            cells[idx].IsFlagged = !cells[idx].IsFlagged;
            if (cells[idx].IsFlagged)
                remainingMines--;
            else
                remainingMines++;
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

            // Header panel (Status, Mines remaining, Face button, Timer)
            int headerX = contentX + 8;
            int headerY = contentY + 6;
            int headerWidth = contentWidth - 16;
            int headerHeight = 42;
            canvas.DrawFilledRectangle(HeaderPanel, headerX, headerY, headerWidth, headerHeight);
            canvas.DrawFilledRectangle(Border, headerX, headerY + headerHeight - 1, headerWidth, 1);

            // Mine counter (left)
            SmallTextRenderer.Draw(canvas, "MINY", headerX + 10, headerY + 7, MutedColor);
            ulong minesDisplay = remainingMines >= 0 ? (ulong)remainingMines : 0;
            SmallTextRenderer.DrawUInt(canvas, minesDisplay, headerX + 10, headerY + 22, TextColor);

            // Face button (center)
            int faceX = Window.X + (Window.Width - 30) / 2;
            int faceY = headerY + 6;
            canvas.DrawFilledRectangle(Border, faceX, faceY, 30, 30);
            canvas.DrawFilledRectangle(CellUnrevealed, faceX + 1, faceY + 1, 28, 28);

            string faceIcon = ":)";
            Color faceColor = Color.FromArgb(240, 210, 50);
            if (status == GameStatus.Won)
            {
                faceIcon = "B)";
                faceColor = Color.FromArgb(80, 220, 120);
            }
            else if (status == GameStatus.Lost)
            {
                faceIcon = "X(";
                faceColor = Color.FromArgb(240, 70, 70);
            }
            else if (isLeftHeld && hoveredCell >= 0)
            {
                faceIcon = ":o";
            }
            SmallTextRenderer.DrawCentered(canvas, faceIcon, faceX, faceY + 8, 30, faceColor);

            // Timer (right)
            int timerX = headerX + headerWidth - 45;
            SmallTextRenderer.Draw(canvas, "CZAS", timerX, headerY + 7, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, elapsedSeconds, timerX, headerY + 22, TextColor);

            // Board grid
            int gridOriginX = Window.X + (Window.Width - (GridCols * CellSize)) / 2;
            int gridOriginY = contentY + 54;

            for (int r = 0; r < GridRows; r++)
            {
                for (int c = 0; c < GridCols; c++)
                {
                    int idx = r * GridCols + c;
                    int cellX = gridOriginX + c * CellSize;
                    int cellY = gridOriginY + r * CellSize;

                    RenderCell(canvas, cellX, cellY, idx);
                }
            }
        }

        private void RenderCell(Canvas canvas, int x, int y, int idx)
        {
            Cell cell = cells[idx];
            bool isHovered = (idx == hoveredCell);

            if (!cell.IsRevealed)
            {
                // Unrevealed beveled tile
                Color bg = isHovered ? CellUnrevealedHover : CellUnrevealed;
                canvas.DrawFilledRectangle(bg, x, y, CellSize, CellSize);

                // 3D Bevel highlight & shadow
                canvas.DrawFilledRectangle(CellHighlight, x, y, CellSize, 2);
                canvas.DrawFilledRectangle(CellHighlight, x, y, 2, CellSize);
                canvas.DrawFilledRectangle(CellShadow, x, y + CellSize - 2, CellSize, 2);
                canvas.DrawFilledRectangle(CellShadow, x + CellSize - 2, y, 2, CellSize);

                if (cell.IsFlagged)
                {
                    // Draw Flag 🚩
                    int fx = x + 10;
                    int fy = y + 7;
                    // Pole
                    canvas.DrawFilledRectangle(TextColor, fx + 10, fy, 2, 18);
                    // Flag pennant
                    canvas.DrawFilledRectangle(FlagColor, fx + 2, fy + 2, 8, 7);
                    // Base
                    canvas.DrawFilledRectangle(TextColor, fx + 6, fy + 17, 8, 2);
                }
            }
            else
            {
                // Revealed tile
                Color bg = (idx == explodedIndex) ? CellMineExploded : CellRevealed;
                canvas.DrawFilledRectangle(bg, x, y, CellSize, CellSize);
                canvas.DrawFilledRectangle(Border, x, y, CellSize, 1);
                canvas.DrawFilledRectangle(Border, x, y, 1, CellSize);

                if (cell.IsMine)
                {
                    // Draw Mine 💣
                    int mx = x + CellSize / 2;
                    int my = y + CellSize / 2;
                    canvas.DrawFilledRectangle(Color.FromArgb(230, 230, 230), mx - 6, my - 6, 12, 12);
                    canvas.DrawFilledRectangle(Color.FromArgb(20, 20, 20), mx - 4, my - 4, 8, 8);
                    // Spikes
                    canvas.DrawFilledRectangle(Color.FromArgb(230, 230, 230), mx - 8, my - 1, 16, 2);
                    canvas.DrawFilledRectangle(Color.FromArgb(230, 230, 230), mx - 1, my - 8, 2, 16);
                }
                else if (cell.AdjacentMines > 0)
                {
                    byte count = cell.AdjacentMines;
                    Color numColor = count < NumberColors.Length ? NumberColors[count] : TextColor;
                    SmallTextRenderer.DrawUInt(canvas, (ulong)count, x + 11, y + 9, numColor);
                }
            }
        }
    }
}
