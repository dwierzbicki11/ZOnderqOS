using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class TetrisApp : Application
    {
        private const int BoardCols = 10;
        private const int BoardRows = 20;
        private const int CellSize = 18;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color HeaderPanel = Color.FromArgb(27, 34, 41);
        private static readonly Color GridBg = Color.FromArgb(14, 18, 23);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(73, 163, 224);

        // Piece colors
        private static readonly Color[] PieceColors =
        {
            Color.Transparent,
            Color.FromArgb(50, 200, 220),  // 1: I - Cyan
            Color.FromArgb(50, 100, 230),  // 2: J - Blue
            Color.FromArgb(235, 140, 40),  // 3: L - Orange
            Color.FromArgb(240, 210, 50),  // 4: O - Yellow
            Color.FromArgb(60, 200, 80),   // 5: S - Green
            Color.FromArgb(170, 70, 220),  // 6: T - Purple
            Color.FromArgb(230, 60, 60)    // 7: Z - Red
        };

        private static readonly sbyte[,,,] PieceOffsets = new sbyte[7, 4, 4, 2]
        {
            // 0: I
            {
                { { -1, 0 }, { 0, 0 }, { 1, 0 }, { 2, 0 } },
                { { 0, -1 }, { 0, 0 }, { 0, 1 }, { 0, 2 } },
                { { -1, 1 }, { 0, 1 }, { 1, 1 }, { 2, 1 } },
                { { 1, -1 }, { 1, 0 }, { 1, 1 }, { 1, 2 } }
            },
            // 1: J
            {
                { { -1, -1 }, { -1, 0 }, { 0, 0 }, { 1, 0 } },
                { { 1, -1 }, { 0, -1 }, { 0, 0 }, { 0, 1 } },
                { { -1, 0 }, { 0, 0 }, { 1, 0 }, { 1, 1 } },
                { { 0, -1 }, { 0, 0 }, { 0, 1 }, { -1, 1 } }
            },
            // 2: L
            {
                { { 1, -1 }, { -1, 0 }, { 0, 0 }, { 1, 0 } },
                { { 0, -1 }, { 0, 0 }, { 0, 1 }, { 1, 1 } },
                { { -1, 0 }, { 0, 0 }, { 1, 0 }, { -1, 1 } },
                { { -1, -1 }, { 0, -1 }, { 0, 0 }, { 0, 1 } }
            },
            // 3: O
            {
                { { 0, 0 }, { 1, 0 }, { 0, 1 }, { 1, 1 } },
                { { 0, 0 }, { 1, 0 }, { 0, 1 }, { 1, 1 } },
                { { 0, 0 }, { 1, 0 }, { 0, 1 }, { 1, 1 } },
                { { 0, 0 }, { 1, 0 }, { 0, 1 }, { 1, 1 } }
            },
            // 4: S
            {
                { { 0, 0 }, { 1, 0 }, { -1, 1 }, { 0, 1 } },
                { { 0, -1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } },
                { { 0, 0 }, { 1, 0 }, { -1, 1 }, { 0, 1 } },
                { { 0, -1 }, { 0, 0 }, { 1, 0 }, { 1, 1 } }
            },
            // 5: T
            {
                { { -1, 0 }, { 0, 0 }, { 1, 0 }, { 0, 1 } },
                { { 0, -1 }, { 0, 0 }, { 0, 1 }, { -1, 0 } },
                { { -1, 0 }, { 0, 0 }, { 1, 0 }, { 0, -1 } },
                { { 0, -1 }, { 0, 0 }, { 0, 1 }, { 1, 0 } }
            },
            // 6: Z
            {
                { { -1, 0 }, { 0, 0 }, { 0, 1 }, { 1, 1 } },
                { { 1, -1 }, { 0, 0 }, { 1, 0 }, { 0, 1 } },
                { { -1, 0 }, { 0, 0 }, { 0, 1 }, { 1, 1 } },
                { { 1, -1 }, { 0, 0 }, { 1, 0 }, { 0, 1 } }
            }
        };

        private enum GameStatus { Ready, Playing, Paused, GameOver }

        private readonly byte[,] board = new byte[BoardCols, BoardRows];
        private int currentPiece;
        private int currentRotation;
        private int currentX;
        private int currentY;
        private int nextPiece;

        private GameStatus status = GameStatus.Ready;
        private uint score;
        private uint linesCleared;
        private uint level = 1;
        private long lastFallTicks;
        private readonly Action closeCallback;
        private readonly Random rng = new Random();

        public TetrisApp(int x, int y, Action onClose) : base("Tetris")
        {
            closeCallback = onClose;
            // 180 board width + 180 side panel + margins = 416 x 460
            Window = new Window(x, y, 416, 460, "Tetris");
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
            for (int r = 0; r < BoardRows; r++)
            {
                for (int c = 0; c < BoardCols; c++)
                {
                    board[c, r] = 0;
                }
            }

            score = 0;
            linesCleared = 0;
            level = 1;
            status = GameStatus.Ready;
            nextPiece = rng.Next(7);
            SpawnNextPiece();
            lastFallTicks = Stopwatch.GetTimestamp();
        }

        private void SpawnNextPiece()
        {
            currentPiece = nextPiece;
            nextPiece = rng.Next(7);
            currentRotation = 0;
            currentX = 4;
            currentY = 1;

            if (!PieceFits(currentPiece, currentRotation, currentX, currentY))
            {
                status = GameStatus.GameOver;
            }
        }

        private bool PieceFits(int piece, int rot, int px, int py)
        {
            for (int i = 0; i < 4; i++)
            {
                int bx = px + PieceOffsets[piece, rot, i, 0];
                int by = py + PieceOffsets[piece, rot, i, 1];

                if (bx < 0 || bx >= BoardCols || by >= BoardRows)
                    return false;

                if (by >= 0 && board[bx, by] != 0)
                    return false;
            }
            return true;
        }

        private long GetFallIntervalTicks()
        {
            if (Stopwatch.Frequency <= 0)
                return 1000000;

            // Interval speeds up with level: 600ms down to 100ms
            long ms = 600 - (long)((level - 1) * 45);
            if (ms < 100)
                ms = 100;

            return (Stopwatch.Frequency * ms) / 1000;
        }

        public override void Update()
        {
            if (status != GameStatus.Playing)
                return;

            long now = Stopwatch.GetTimestamp();
            if (now - lastFallTicks >= GetFallIntervalTicks())
            {
                lastFallTicks = now;
                DropPieceOne();
            }
        }

        private void DropPieceOne()
        {
            if (PieceFits(currentPiece, currentRotation, currentX, currentY + 1))
            {
                currentY++;
            }
            else
            {
                LockPiece();
            }
        }

        private void HardDrop()
        {
            while (PieceFits(currentPiece, currentRotation, currentX, currentY + 1))
            {
                currentY++;
                score += 2;
            }
            LockPiece();
        }

        private void LockPiece()
        {
            for (int i = 0; i < 4; i++)
            {
                int bx = currentX + PieceOffsets[currentPiece, currentRotation, i, 0];
                int by = currentY + PieceOffsets[currentPiece, currentRotation, i, 1];
                if (bx >= 0 && bx < BoardCols && by >= 0 && by < BoardRows)
                {
                    board[bx, by] = (byte)(currentPiece + 1);
                }
            }

            CheckLineClears();
            SpawnNextPiece();
            lastFallTicks = Stopwatch.GetTimestamp();
        }

        private void CheckLineClears()
        {
            uint cleared = 0;
            for (int r = BoardRows - 1; r >= 0; r--)
            {
                bool full = true;
                for (int c = 0; c < BoardCols; c++)
                {
                    if (board[c, r] == 0)
                    {
                        full = false;
                        break;
                    }
                }

                if (full)
                {
                    cleared++;
                    // Shift down
                    for (int y = r; y > 0; y--)
                    {
                        for (int x = 0; x < BoardCols; x++)
                        {
                            board[x, y] = board[x, y - 1];
                        }
                    }
                    for (int x = 0; x < BoardCols; x++)
                    {
                        board[x, 0] = 0;
                    }
                    r++; // Recheck the same row index now containing shifted blocks
                }
            }

            if (cleared > 0)
            {
                linesCleared += cleared;
                level = (linesCleared / 10) + 1;

                if (cleared == 1) score += 100 * level;
                else if (cleared == 2) score += 300 * level;
                else if (cleared == 3) score += 500 * level;
                else if (cleared >= 4) score += 800 * level;
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
                if (status == GameStatus.Playing) status = GameStatus.Paused;
                else if (status == GameStatus.Paused) status = GameStatus.Playing;
                return;
            }

            if (status == GameStatus.Ready)
            {
                if (key.Key == ConsoleKeyEx.Spacebar || key.Key == ConsoleKeyEx.Enter)
                {
                    status = GameStatus.Playing;
                    lastFallTicks = Stopwatch.GetTimestamp();
                    return;
                }
            }

            if (status == GameStatus.GameOver)
            {
                if (key.Key == ConsoleKeyEx.Spacebar || key.Key == ConsoleKeyEx.Enter)
                {
                    ResetGame();
                    status = GameStatus.Playing;
                    return;
                }
            }

            if (status != GameStatus.Playing)
                return;

            if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                if (PieceFits(currentPiece, currentRotation, currentX - 1, currentY))
                    currentX--;
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                if (PieceFits(currentPiece, currentRotation, currentX + 1, currentY))
                    currentX++;
            }
            else if (key.Key == ConsoleKeyEx.UpArrow || key.KeyChar == 'w' || key.KeyChar == 'W')
            {
                int nextRot = (currentRotation + 1) % 4;
                if (PieceFits(currentPiece, nextRot, currentX, currentY))
                {
                    currentRotation = nextRot;
                }
                else if (PieceFits(currentPiece, nextRot, currentX - 1, currentY)) // Wall kick left
                {
                    currentX--;
                    currentRotation = nextRot;
                }
                else if (PieceFits(currentPiece, nextRot, currentX + 1, currentY)) // Wall kick right
                {
                    currentX++;
                    currentRotation = nextRot;
                }
            }
            else if (key.Key == ConsoleKeyEx.DownArrow || key.KeyChar == 's' || key.KeyChar == 'S')
            {
                if (PieceFits(currentPiece, currentRotation, currentX, currentY + 1))
                {
                    currentY++;
                    score += 1;
                }
            }
            else if (key.Key == ConsoleKeyEx.Spacebar)
            {
                HardDrop();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            if (leftClicked && !leftWasClicked)
            {
                if (status == GameStatus.Ready || status == GameStatus.GameOver)
                {
                    ResetGame();
                    status = GameStatus.Playing;
                    lastFallTicks = Stopwatch.GetTimestamp();
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

            // Board grid
            int gridX = contentX + 14;
            int gridY = contentY + 12;
            int gridW = BoardCols * CellSize;
            int gridH = BoardRows * CellSize;

            canvas.DrawFilledRectangle(GridBg, gridX, gridY, gridW, gridH);
            canvas.DrawFilledRectangle(Border, gridX - 1, gridY - 1, gridW + 2, 1);
            canvas.DrawFilledRectangle(Border, gridX - 1, gridY + gridH, gridW + 2, 1);
            canvas.DrawFilledRectangle(Border, gridX - 1, gridY - 1, 1, gridH + 2);
            canvas.DrawFilledRectangle(Border, gridX + gridW, gridY - 1, 1, gridH + 2);

            // Draw settled blocks
            for (int r = 0; r < BoardRows; r++)
            {
                for (int c = 0; c < BoardCols; c++)
                {
                    byte val = board[c, r];
                    if (val > 0 && val < PieceColors.Length)
                    {
                        int bx = gridX + c * CellSize;
                        int by = gridY + r * CellSize;
                        canvas.DrawFilledRectangle(PieceColors[val], bx + 1, by + 1, CellSize - 2, CellSize - 2);
                    }
                }
            }

            // Draw Ghost Piece (outline of where piece lands)
            if (status == GameStatus.Playing)
            {
                int ghostY = currentY;
                while (PieceFits(currentPiece, currentRotation, currentX, ghostY + 1))
                {
                    ghostY++;
                }

                Color ghostColor = Color.FromArgb(40, 55, 70);
                for (int i = 0; i < 4; i++)
                {
                    int bx = currentX + PieceOffsets[currentPiece, currentRotation, i, 0];
                    int by = ghostY + PieceOffsets[currentPiece, currentRotation, i, 1];
                    if (bx >= 0 && bx < BoardCols && by >= 0 && by < BoardRows)
                    {
                        canvas.DrawFilledRectangle(ghostColor, gridX + bx * CellSize + 1,
                            gridY + by * CellSize + 1, CellSize - 2, CellSize - 2);
                    }
                }

                // Draw Active Piece
                Color pieceColor = PieceColors[currentPiece + 1];
                for (int i = 0; i < 4; i++)
                {
                    int bx = currentX + PieceOffsets[currentPiece, currentRotation, i, 0];
                    int by = currentY + PieceOffsets[currentPiece, currentRotation, i, 1];
                    if (bx >= 0 && bx < BoardCols && by >= 0 && by < BoardRows)
                    {
                        canvas.DrawFilledRectangle(pieceColor, gridX + bx * CellSize + 1,
                            gridY + by * CellSize + 1, CellSize - 2, CellSize - 2);
                    }
                }
            }

            // Side Panel (Next, Score, Lines, Level, Controls)
            int sideX = gridX + gridW + 18;
            int sideY = gridY;
            int sideW = contentWidth - (sideX - contentX) - 10;

            // Next Piece Box
            SmallTextRenderer.Draw(canvas, "NASTEPNY", sideX, sideY, MutedColor);
            int previewBoxY = sideY + 16;
            canvas.DrawFilledRectangle(HeaderPanel, sideX, previewBoxY, 80, 80);
            canvas.DrawFilledRectangle(Border, sideX, previewBoxY, 80, 80);

            if (nextPiece >= 0 && nextPiece < 7)
            {
                Color nextColor = PieceColors[nextPiece + 1];
                for (int i = 0; i < 4; i++)
                {
                    int ox = PieceOffsets[nextPiece, 0, i, 0];
                    int oy = PieceOffsets[nextPiece, 0, i, 1];
                    int px = sideX + 34 + ox * 14;
                    int py = previewBoxY + 36 + oy * 14;
                    canvas.DrawFilledRectangle(nextColor, px, py, 12, 12);
                }
            }

            // Score
            int statsY = previewBoxY + 95;
            SmallTextRenderer.Draw(canvas, "WYNIK", sideX, statsY, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, score, sideX, statsY + 14, AccentColor);

            // Lines
            SmallTextRenderer.Draw(canvas, "LINIE", sideX, statsY + 36, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, linesCleared, sideX, statsY + 50, TextColor);

            // Level
            SmallTextRenderer.Draw(canvas, "POZIOM", sideX, statsY + 72, MutedColor);
            SmallTextRenderer.DrawUInt(canvas, level, sideX, statsY + 86, Color.FromArgb(240, 210, 50));

            // Controls legend
            int controlsY = statsY + 118;
            SmallTextRenderer.Draw(canvas, "STEROWANIE", sideX, controlsY, MutedColor);
            SmallTextRenderer.Draw(canvas, "< > - Ruch", sideX, controlsY + 14, TextColor);
            SmallTextRenderer.Draw(canvas, "^   - Obrot", sideX, controlsY + 26, TextColor);
            SmallTextRenderer.Draw(canvas, "v   - Opad", sideX, controlsY + 38, TextColor);
            SmallTextRenderer.Draw(canvas, "SPC - Zrzut", sideX, controlsY + 50, TextColor);
            SmallTextRenderer.Draw(canvas, "P   - Pauza", sideX, controlsY + 62, TextColor);

            // Overlay message for Ready / GameOver / Paused
            if (status == GameStatus.Ready)
            {
                RenderCenterPrompt(canvas, gridX, gridY, "SPACJA: START", AccentColor);
            }
            else if (status == GameStatus.GameOver)
            {
                RenderCenterPrompt(canvas, gridX, gridY, "KONIEC GRY!", Color.FromArgb(240, 70, 70));
            }
            else if (status == GameStatus.Paused)
            {
                RenderCenterPrompt(canvas, gridX, gridY, "PAUZA", Color.FromArgb(240, 200, 50));
            }
        }

        private static void RenderCenterPrompt(Canvas canvas, int gx, int gy, string text, Color color)
        {
            int bw = BoardCols * CellSize;
            int bh = BoardRows * CellSize;
            int px = gx + 10;
            int py = gy + bh / 2 - 20;
            int pw = bw - 20;
            int ph = 40;

            canvas.DrawFilledRectangle(Color.FromArgb(10, 14, 18), px, py, pw, ph);
            canvas.DrawFilledRectangle(Border, px, py, pw, ph);
            SmallTextRenderer.DrawCentered(canvas, text, px, py + 14, pw, color);
        }
    }
}
