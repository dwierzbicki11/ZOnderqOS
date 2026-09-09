using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public class Label : Widget
    {
        public string Text { get; set; }
        public Color TextColor { get; set; }
        public Font Font { get; set; }

        public Label(int x, int y, string text, Color textColor) : base(x, y, 0, 0)
        {
            Text = text;
            TextColor = textColor;
            Font = PCScreenFont.DefaultFont;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;
            canvas.DrawString(Text, Font, TextColor, X, Y);
        }
    }
}