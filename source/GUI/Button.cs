using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public class Button : Widget
    {
        public string Text { get; set; }
        public Color BackgroundColor { get; set; }
        public Color TextColor { get; set; }
        public bool IsHovered { get; set; } = false;
        public Action OnClick { get; set; }
        public Font Font { get; set; }

        public Button(int x, int y, int width, int height, string text, Action onClick = null) : base(x, y, width, height)
        {
            Text = text;
            BackgroundColor = Color.FromArgb(43, 50, 58);
            TextColor = Color.FromArgb(232, 236, 240);
            OnClick = onClick;
            Font = PCScreenFont.DefaultFont;

            int requiredWidth = TextHelper.GetTextWidth(Text, Font) + 24;
            if (Width < requiredWidth)
                Width = requiredWidth;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            Color bg = IsHovered ? Color.FromArgb(55, 91, 125) : BackgroundColor;
            Color border = IsHovered ? Color.FromArgb(75, 145, 205) : Color.FromArgb(70, 78, 88);
            canvas.DrawFilledRectangle(bg, X, Y, Width, Height);
            canvas.DrawRectangle(border, X, Y, Width, Height);
            TextHelper.DrawCenteredString(canvas, Text, Font, TextColor, X, Y, Width, Height);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;
        }

        public void InvokeClick()
        {
            OnClick?.Invoke();
        }
    }
}