using System;
using System.Diagnostics;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class Game2048App : Application
    {
        private const int GridSize = 4;
        private const int CellSize = 64;
        private const int CellGap = 8;
        private const int BoardSize = GridSize * CellSize + (GridSize + 1) * CellGap; // 296 px

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color HeaderPanel = Color.FromArgb(27, 34, 41);
        private static readonly Color BoardBg = Color.FromArgb(14, 18, 23);
        private static readonly Color EmptyCell = Color.FromArgb(28, 35, 45);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(73, 163, 224);
        private static readonly Color GoldColor = Color.FromArgb(240, 195, 45);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color GreenColor = Color.FromArgb(60, 200, 100);

        private readonly int[,] grid = new int[GridSize, GridSize];
        private uint score;
        private uint bestScore;
        private bool isGameOver;
        private bool hasWon;
        private bool continueAfterWin;

        private long toneEndTicks;
        private readonly Random rng = new Random();
        private readonly Action closeCallback;

        // Mouse drag tracking for swipes
        private int dragStartX = -1;
        private int dragStartY = -1;

        public Game2048App(int x, int y, Action onClose) : base("2048")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 360, 480, "2048");
            Window.CloseAction = Close;
            ResetGame();
        }

        public override void Close()
        {
            ZonderqOS.Hardware.Speaker.Stop();
            base.Close();
            closeCallback?.Invoke();
        }

        private void ResetGame()
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    grid[c, r] = 0;
                }
            }

            score = 0;
            isGameOver = false;
            hasWon = false;
            continueAfterWin = false;

            SpawnRandomTile();
            SpawnRandomTile();
        }

        private void SpawnRandomTile()
        {
            // Find all empty cells
            int emptyCount = 0;
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    if (grid[c, r] == 0)
                        emptyCount++;
                }
            }

            if (emptyCount == 0)
                return;

            int targetIndex = rng.Next(0, emptyCount);
            int current = 0;

            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    if (grid[c, r] == 0)
                    {
                        if (current == targetIndex)
                        {
                            // 90% chance of 2, 10% chance of 4
                            grid[c, r] = rng.Next(0, 10) == 0 ? 4 : 2;
                            return;
                        }
                        current++;
                    }
                }
            }
        }

        private void PlaySound(ushort freq, int durationMs)
        {
            ZonderqOS.Hardware.Speaker.Tone(freq);
            long freqClock = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            toneEndTicks = Stopwatch.GetTimestamp() + (freqClock * durationMs) / 1000;
        }

        public override void Update()
        {
            if (toneEndTicks > 0 && Stopwatch.GetTimestamp() >= toneEndTicks)
            {
                ZonderqOS.Hardware.Speaker.Stop();
                toneEndTicks = 0;
            }
        }

        private bool CanMove()
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    if (grid[c, r] == 0)
                        return true;

                    if (c + 1 < GridSize && grid[c, r] == grid[c + 1, r])
                        return true;

                    if (r + 1 < GridSize && grid[c, r] == grid[c, r + 1])
                        return true;
                }
            }
            return false;
        }

        private void MoveLeft()
        {
            if (isGameOver)
                return;

            bool changed = false;
            int maxMerged = 0;

            for (int r = 0; r < GridSize; r++)
            {
                int[] line = new int[GridSize];
                int idx = 0;
                for (int c = 0; c < GridSize; c++)
                {
                    if (grid[c, r] != 0)
                        line[idx++] = grid[c, r];
                }

                for (int i = 0; i < GridSize - 1; i++)
                {
                    if (line[i] != 0 && line[i] == line[i + 1])
                    {
                        line[i] *= 2;
                        score += (uint)line[i];
                        if (line[i] > maxMerged) maxMerged = line[i];
                        line[i + 1] = 0;
                        i++;
                    }
                }

                int[] newLine = new int[GridSize];
                idx = 0;
                for (int i = 0; i < GridSize; i++)
                {
                    if (line[i] != 0)
                        newLine[idx++] = line[i];
                }

                for (int c = 0; c < GridSize; c++)
                {
                    if (grid[c, r] != newLine[c])
                    {
                        changed = true;
                        grid[c, r] = newLine[c];
                    }
                }
            }

            AfterMove(changed, maxMerged);
        }

        private void MoveRight()
        {
            if (isGameOver)
                return;

            bool changed = false;
            int maxMerged = 0;

            for (int r = 0; r < GridSize; r++)
            {
                int[] line = new int[GridSize];
                int idx = 0;
                for (int c = GridSize - 1; c >= 0; c--)
                {
                    if (grid[c, r] != 0)
                        line[idx++] = grid[c, r];
                }

                for (int i = 0; i < GridSize - 1; i++)
                {
                    if (line[i] != 0 && line[i] == line[i + 1])
                    {
                        line[i] *= 2;
                        score += (uint)line[i];
                        if (line[i] > maxMerged) maxMerged = line[i];
                        line[i + 1] = 0;
                        i++;
                    }
                }

                int[] newLine = new int[GridSize];
                idx = 0;
                for (int i = 0; i < GridSize; i++)
                {
                    if (line[i] != 0)
                        newLine[idx++] = line[i];
                }

                for (int c = GridSize - 1; c >= 0; c--)
                {
                    int val = newLine[GridSize - 1 - c];
                    if (grid[c, r] != val)
                    {
                        changed = true;
                        grid[c, r] = val;
                    }
                }
            }

            AfterMove(changed, maxMerged);
        }

        private void MoveUp()
        {
            if (isGameOver)
                return;

            bool changed = false;
            int maxMerged = 0;

            for (int c = 0; c < GridSize; c++)
            {
                int[] line = new int[GridSize];
                int idx = 0;
                for (int r = 0; r < GridSize; r++)
                {
                    if (grid[c, r] != 0)
                        line[idx++] = grid[c, r];
                }

                for (int i = 0; i < GridSize - 1; i++)
                {
                    if (line[i] != 0 && line[i] == line[i + 1])
                    {
                        line[i] *= 2;
                        score += (uint)line[i];
                        if (line[i] > maxMerged) maxMerged = line[i];
                        line[i + 1] = 0;
                        i++;
                    }
                }

                int[] newLine = new int[GridSize];
                idx = 0;
                for (int i = 0; i < GridSize; i++)
                {
                    if (line[i] != 0)
                        newLine[idx++] = line[i];
                }

                for (int r = 0; r < GridSize; r++)
                {
                    if (grid[c, r] != newLine[r])
                    {
                        changed = true;
                        grid[c, r] = newLine[r];
                    }
                }
            }

            AfterMove(changed, maxMerged);
        }

        private void MoveDown()
        {
            if (isGameOver)
                return;

            bool changed = false;
            int maxMerged = 0;

            for (int c = 0; c < GridSize; c++)
            {
                int[] line = new int[GridSize];
                int idx = 0;
                for (int r = GridSize - 1; r >= 0; r--)
                {
                    if (grid[c, r] != 0)
                        line[idx++] = grid[c, r];
                }

                for (int i = 0; i < GridSize - 1; i++)
                {
                    if (line[i] != 0 && line[i] == line[i + 1])
                    {
                        line[i] *= 2;
                        score += (uint)line[i];
                        if (line[i] > maxMerged) maxMerged = line[i];
                        line[i + 1] = 0;
                        i++;
                    }
                }

                int[] newLine = new int[GridSize];
                idx = 0;
                for (int i = 0; i < GridSize; i++)
                {
                    if (line[i] != 0)
                        newLine[idx++] = line[i];
                }

                for (int r = GridSize - 1; r >= 0; r--)
                {
                    int val = newLine[GridSize - 1 - r];
                    if (grid[c, r] != val)
                    {
                        changed = true;
                        grid[c, r] = val;
                    }
                }
            }

            AfterMove(changed, maxMerged);
        }

        private void AfterMove(bool changed, int maxMerged)
        {
            if (!changed)
                return;

            if (score > bestScore)
                bestScore = score;

            SpawnRandomTile();

            // Sound feedback
            if (maxMerged >= 2048 && !hasWon)
            {
                hasWon = true;
                PlaySound(1046, 150);
            }
            else if (maxMerged > 0)
            {
                ushort tone = (ushort)Math.Min(1200, 480 + (int)(Math.Log2(maxMerged) * 60));
                PlaySound(tone, 30);
            }
            else
            {
                PlaySound(380, 20);
            }

            if (!CanMove())
            {
                isGameOver = true;
                PlaySound(160, 150);
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

            if (key.Key == ConsoleKeyEx.LeftArrow || key.KeyChar == 'a' || key.KeyChar == 'A')
            {
                MoveLeft();
            }
            else if (key.Key == ConsoleKeyEx.RightArrow || key.KeyChar == 'd' || key.KeyChar == 'D')
            {
                MoveRight();
            }
            else if (key.Key == ConsoleKeyEx.UpArrow || key.KeyChar == 'w' || key.KeyChar == 'W')
            {
                MoveUp();
            }
            else if (key.Key == ConsoleKeyEx.DownArrow || key.KeyChar == 's' || key.KeyChar == 'S')
            {
                MoveDown();
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

            // [NOWA GRA] button in header
            int btnX = contentX + contentWidth - 84;
            int btnY = contentY + 36;
            int btnW = 76;
            int btnH = 22;

            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= btnX && mouseX < btnX + btnW &&
                    mouseY >= btnY && mouseY < btnY + btnH)
                {
                    ResetGame();
                    return;
                }
            }

            // On-screen D-Pad buttons at bottom
            int boardY = contentY + 68;
            int dpadY = boardY + BoardSize + 10;
            int centerX = contentX + contentWidth / 2;

            int upX = centerX - 18, upY = dpadY, dpadW = 36, dpadH = 26;
            int leftX = centerX - 60, leftY = dpadY + 30;
            int downX = centerX - 18, downY = dpadY + 30;
            int rightX = centerX + 24, rightY = dpadY + 30;

            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= upX && mouseX < upX + dpadW && mouseY >= upY && mouseY < upY + dpadH)
                {
                    MoveUp();
                    return;
                }
                if (mouseX >= leftX && mouseX < leftX + dpadW && mouseY >= leftY && mouseY < leftY + dpadH)
                {
                    MoveLeft();
                    return;
                }
                if (mouseX >= downX && mouseX < downX + dpadW && mouseY >= downY && mouseY < downY + dpadH)
                {
                    MoveDown();
                    return;
                }
                if (mouseX >= rightX && mouseX < rightX + dpadW && mouseY >= rightY && mouseY < rightY + dpadH)
                {
                    MoveRight();
                    return;
                }

                // If won or game over, clicking anywhere on the board restarts or continues
                int boardX = contentX + (contentWidth - BoardSize) / 2;
                if (mouseX >= boardX && mouseX < boardX + BoardSize &&
                    mouseY >= boardY && mouseY < boardY + BoardSize)
                {
                    if (isGameOver)
                    {
                        ResetGame();
                        return;
                    }
                    if (hasWon && !continueAfterWin)
                    {
                        continueAfterWin = true;
                        return;
                    }
                }
            }

            // Board swipe gestures
            int bX = contentX + (contentWidth - BoardSize) / 2;
            if (leftClicked && !leftWasClicked)
            {
                if (mouseX >= bX && mouseX < bX + BoardSize &&
                    mouseY >= boardY && mouseY < boardY + BoardSize)
                {
                    dragStartX = mouseX;
                    dragStartY = mouseY;
                }
            }
            else if (!leftClicked && leftWasClicked && dragStartX >= 0)
            {
                int dx = mouseX - dragStartX;
                int dy = mouseY - dragStartY;
                dragStartX = -1;
                dragStartY = -1;

                if (Math.Abs(dx) > 25 || Math.Abs(dy) > 25)
                {
                    if (Math.Abs(dx) > Math.Abs(dy))
                    {
                        if (dx > 0) MoveRight(); else MoveLeft();
                    }
                    else
                    {
                        if (dy > 0) MoveDown(); else MoveUp();
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

            // Header: Title & Subtitle
            SmallTextRenderer.Draw(canvas, "2048", contentX + 14, contentY + 12, GoldColor);
            SmallTextRenderer.Draw(canvas, "Polacz kafelki!", contentX + 14, contentY + 28, MutedColor);

            // Score & Best boxes
            int scoreBoxW = 68;
            int scoreBoxH = 34;
            int scoreBoxX = contentX + contentWidth - 158;
            int bestBoxX = contentX + contentWidth - 84;
            int scoreBoxY = contentY + 8;

            canvas.DrawFilledRectangle(HeaderPanel, scoreBoxX, scoreBoxY, scoreBoxW, scoreBoxH);
            canvas.DrawRectangle(Border, scoreBoxX, scoreBoxY, scoreBoxW, scoreBoxH);
            SmallTextRenderer.DrawCentered(canvas, "WYNIK", scoreBoxX, scoreBoxY + 4, scoreBoxW, MutedColor);
            SmallTextRenderer.DrawCentered(canvas, score.ToString(), scoreBoxX, scoreBoxY + 18, scoreBoxW, TextColor);

            canvas.DrawFilledRectangle(HeaderPanel, bestBoxX, scoreBoxY, scoreBoxW, scoreBoxH);
            canvas.DrawRectangle(Border, bestBoxX, scoreBoxY, scoreBoxW, scoreBoxH);
            SmallTextRenderer.DrawCentered(canvas, "REKORD", bestBoxX, scoreBoxY + 4, scoreBoxW, MutedColor);
            SmallTextRenderer.DrawCentered(canvas, bestScore.ToString(), bestBoxX, scoreBoxY + 18, scoreBoxW, AccentColor);

            // [NOWA GRA] button
            int btnX = contentX + contentWidth - 84;
            int btnY = contentY + 46;
            int btnW = 68;
            int btnH = 18;
            canvas.DrawFilledRectangle(Color.FromArgb(36, 45, 55), btnX, btnY, btnW, btnH);
            canvas.DrawRectangle(Border, btnX, btnY, btnW, btnH);
            SmallTextRenderer.DrawCentered(canvas, "NOWA GRA", btnX, btnY + 5, btnW, TextColor);

            // Board Container
            int boardX = contentX + (contentWidth - BoardSize) / 2;
            int boardY = contentY + 68;

            canvas.DrawFilledRectangle(BoardBg, boardX, boardY, BoardSize, BoardSize);
            canvas.DrawRectangle(Border, boardX - 1, boardY - 1, BoardSize + 2, BoardSize + 2);

            // Draw Cells
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    int val = grid[c, r];
                    int cx = boardX + CellGap + c * (CellSize + CellGap);
                    int cy = boardY + CellGap + r * (CellSize + CellGap);

                    Color bg = GetTileBgColor(val);
                    Color fg = GetTileTextColor(val);

                    canvas.DrawFilledRectangle(bg, cx, cy, CellSize, CellSize);
                    canvas.DrawRectangle(Border, cx, cy, CellSize, CellSize);

                    if (val > 0)
                    {
                        string str = val.ToString();
                        SmallTextRenderer.DrawCentered(canvas, str, cx, cy + (CellSize - 7) / 2, CellSize, fg);
                    }
                }
            }

            // On-screen D-Pad buttons at bottom
            int dpadY = boardY + BoardSize + 10;
            int centerX = contentX + contentWidth / 2;

            int upX = centerX - 18, upY = dpadY, dpadW = 36, dpadH = 26;
            int leftX = centerX - 60, leftY = dpadY + 30;
            int downX = centerX - 18, downY = dpadY + 30;
            int rightX = centerX + 24, rightY = dpadY + 30;

            DrawDpadBtn(canvas, "^", upX, upY, dpadW, dpadH);
            DrawDpadBtn(canvas, "<", leftX, leftY, dpadW, dpadH);
            DrawDpadBtn(canvas, "v", downX, downY, dpadW, dpadH);
            DrawDpadBtn(canvas, ">", rightX, rightY, dpadW, dpadH);

            // Overlay Banners
            if (isGameOver)
            {
                DrawOverlay(canvas, boardX, boardY, "KONIEC GRY!", "Brak mozliwych ruchow. Kliknij tutaj.", RedColor);
            }
            else if (hasWon && !continueAfterWin)
            {
                DrawOverlay(canvas, boardX, boardY, "WYGRANA 2048!", "Gratulacje! Kliknij aby grac dalej.", GoldColor);
            }
        }

        private void DrawDpadBtn(Canvas canvas, string label, int x, int y, int w, int h)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(32, 40, 50), x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            SmallTextRenderer.DrawCentered(canvas, label, x, y + (h - 7) / 2, w, TextColor);
        }

        private void DrawOverlay(Canvas canvas, int bx, int by, string title, string sub, Color titleColor)
        {
            int ow = BoardSize - 20;
            int oh = 80;
            int ox = bx + 10;
            int oy = by + (BoardSize - oh) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(14, 18, 24), ox, oy, ow, oh);
            canvas.DrawRectangle(titleColor, ox, oy, ow, oh);

            SmallTextRenderer.DrawCentered(canvas, title, ox, oy + 20, ow, titleColor);
            SmallTextRenderer.DrawCentered(canvas, sub, ox, oy + 44, ow, TextColor);
        }

        private static Color GetTileBgColor(int value)
        {
            return value switch
            {
                0 => EmptyCell,
                2 => Color.FromArgb(238, 228, 218),
                4 => Color.FromArgb(237, 224, 200),
                8 => Color.FromArgb(242, 177, 121),
                16 => Color.FromArgb(245, 149, 99),
                32 => Color.FromArgb(246, 124, 95),
                64 => Color.FromArgb(246, 94, 59),
                128 => Color.FromArgb(237, 207, 114),
                256 => Color.FromArgb(237, 204, 97),
                512 => Color.FromArgb(237, 200, 80),
                1024 => Color.FromArgb(237, 197, 63),
                2048 => Color.FromArgb(237, 194, 46),
                _ => Color.FromArgb(94, 218, 145)
            };
        }

        private static Color GetTileTextColor(int value)
        {
            return value switch
            {
                0 => Color.Transparent,
                2 or 4 => Color.FromArgb(119, 110, 101),
                _ => Color.White
            };
        }
    }
}
