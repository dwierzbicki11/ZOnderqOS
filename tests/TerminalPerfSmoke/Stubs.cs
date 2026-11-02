using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace Cosmos.Kernel.System.Graphics
{
    public class Canvas
    {
        public void DrawFilledRectangle(Color color, int x, int y, int width, int height) { }
        public void DrawRectangle(Color color, int x, int y, int width, int height) { }
        public void DrawLine(Color color, int x1, int y1, int x2, int y2) { }
        public Action<string> OnDrawString;
        public void DrawString(string text, Font font, Color color, int x, int y)
        {
            OnDrawString?.Invoke(text);
        }
    }
}
namespace Cosmos.Kernel.System.Graphics.Fonts
{
    public class Font
    {
        public int Width { get; set; } = 8;
        public int Height { get; set; } = 16;
    }
    public static class PCScreenFont
    {
        public static readonly Font DefaultFont = new Font();
    }
}
namespace ZonderqOS.GUI.Icons
{
    public enum IconType { Terminal }
    public static class IconManager
    {
        public static void DrawScaled(Cosmos.Kernel.System.Graphics.Canvas canvas,
            IconType type, int x, int y, int width, int height) { }
    }
}
namespace ZonderqOS.GUI
{
    public enum ConsoleKeyEx { PageUp, PageDown, End, Backspace }
    public struct KeyEvent
    {
        public ConsoleKeyEx Key;
        public char KeyChar;
    }
    public static class SmallTextRenderer
    {
        public static int Width(string text) => text.Length * 6 - 1;
        public static void Draw(Cosmos.Kernel.System.Graphics.Canvas canvas,
            string text, int x, int y, Color color) { }
    }
}