using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.Games.Framework;

namespace ZonderqOS.Games.Tetris
{
    /// <summary>
    /// Portrait game template. Board rendering is present; gameplay is TODO.
    /// </summary>
    public sealed class TetrisGame : Game
    {
        private const int Cell = 20;
        private const int Cols = 10;
        private const int Rows = 20;
        private readonly byte[,] board = new byte[Rows, Cols];
        private int cursorColumn;

        public TetrisGame(int x, int y)
            : base("Tetris - szkielet", x, y, 300, 480, GameOrientation.Portrait, 30)
        {
        }

        protected override void Initialize()
        {
            cursorColumn = 4;
            for (int row = 0; row < Rows; row++)
                for (int col = 0; col < Cols; col++)
                    board[row, col] = 0;
        }

        protected override void Update(float deltaSeconds)
        {
            // TODO: gravity, tetromino rotation, collision, line clearing and score.
        }

        protected override void OnKeyDown(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.LeftArrow)
                cursorColumn = System.Math.Max(0, cursorColumn - 1);
            else if (key.Key == ConsoleKeyEx.RightArrow)
                cursorColumn = System.Math.Min(Cols - 1, cursorColumn + 1);
        }

        protected override void Render(GameCanvas graphics)
        {
            graphics.Clear(Color.FromArgb(16, 21, 33));
            graphics.DrawText("TETRIS - SZKIELET", 12, 10, Color.White);
            const int left = 50;
            const int top = 38;
            for (int row = 0; row < Rows; row++)
            {
                for (int col = 0; col < Cols; col++)
                {
                    graphics.FillRect(board[row, col] == 0
                        ? Color.FromArgb(35, 44, 57) : Color.Cyan,
                        left + col * Cell, top + row * Cell, Cell - 2, Cell - 2);
                }
            }

            graphics.FillRect(Color.Cyan, left + cursorColumn * Cell, top, Cell - 2, Cell - 2);
            graphics.DrawText("LEWO/PRAWO - TEST", 12, Height - 30, Color.White);
            graphics.DrawText("R - RESET / ESC - WYJSCIE", 12, Height - 16, Color.White);
        }
    }
}
