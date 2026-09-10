using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            switch (type)
            {
                case IconType.Terminal:
                    canvas.DrawRectangle(color, x + 2, y + 3, 14, 12);
                    canvas.DrawLine(color, x + 5, y + 7, x + 8, y + 9);
                    canvas.DrawLine(color, x + 8, y + 9, x + 5, y + 11);
                    canvas.DrawLine(color, x + 10, y + 12, x + 14, y + 12);
                    break;

                case IconType.Folder:
                    canvas.DrawFilledRectangle(color, x + 2, y + 5, 15, 11);
                    canvas.DrawFilledRectangle(color, x + 4, y + 3, 7, 3);
                    break;

                case IconType.File:
                    canvas.DrawRectangle(color, x + 4, y + 2, 10, 14);
                    canvas.DrawLine(color, x + 10, y + 2, x + 14, y + 6);
                    canvas.DrawLine(color, x + 10, y + 2, x + 10, y + 6);
                    canvas.DrawLine(color, x + 10, y + 6, x + 14, y + 6);
                    break;

                case IconType.Close:
                    canvas.DrawLine(color, x + 4, y + 4, x + 14, y + 14);
                    canvas.DrawLine(color, x + 14, y + 4, x + 4, y + 14);
                    break;

                case IconType.Maximize:
                    canvas.DrawRectangle(color, x + 3, y + 3, 12, 12);
                    break;

                case IconType.Restore:
                    canvas.DrawRectangle(color, x + 5, y + 5, 10, 10);
                    canvas.DrawLine(color, x + 3, y + 5, x + 3, y + 13);
                    canvas.DrawLine(color, x + 3, y + 5, x + 11, y + 5);
                    break;

                case IconType.Start:
                    canvas.DrawFilledRectangle(color, x + 3, y + 3, 5, 5);
                    canvas.DrawFilledRectangle(color, x + 10, y + 3, 5, 5);
                    canvas.DrawFilledRectangle(color, x + 3, y + 10, 5, 5);
                    canvas.DrawFilledRectangle(color, x + 10, y + 10, 5, 5);
                    break;

                case IconType.Settings:
                case IconType.About:
                case IconType.FileManager:
                    canvas.DrawRectangle(color, x + 3, y + 3, 12, 12);
                    canvas.DrawFilledRectangle(color, x + 6, y + 6, 6, 6);
                    break;
            }
        }

        public static int Size
        {
            get { return IconSize; }
        }
    }
}
