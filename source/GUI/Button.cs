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
            BackgroundColor = Color.LightGray;
            TextColor = Color.Black;
            OnClick = onClick;
            Font = PCScreenFont.DefaultFont;

            // Automatyczne dopasowanie szerokości przycisku przy użyciu TextHelper (z marginesem 24px)
            int requiredWidth = TextHelper.GetTextWidth(Text, Font) + 24;
            if (Width < requiredWidth)
            {
                Width = requiredWidth;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            Color bg = IsHovered ? Color.DarkGray : BackgroundColor;
            canvas.DrawFilledRectangle(bg, X, Y, Width, Height);
            canvas.DrawRectangle(Color.DimGray, X, Y, Width, Height);

            // Użycie globalnego systemu centrowania tekstu
            TextHelper.DrawCenteredString(canvas, Text, Font, TextColor, X, Y, Width, Height);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX <= (X + Width) && mouseY >= Y && mouseY <= (Y + Height);
        }

        public void InvokeClick()
        {
            OnClick?.Invoke();
        }
    }
}