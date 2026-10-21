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
        private bool hovered;
        private float hoverProgress;
        private float clickProgress;
        public bool IsHovered
        {
            get => hovered;
            set
            {
                if (hovered == value)
                    return;
                hovered = value;
                UiAnimator.RequestFrames();
            }
        }
        public Action OnClick { get; set; }
        public Font Font { get; set; }

        public Button(int x, int y, int width, int height, string text, Action onClick = null) : base(x, y, width, height)
        {
            Text = text;
            BackgroundColor = UiPalette.SurfaceRaised;
            TextColor = UiPalette.Text;
            OnClick = onClick;
            Font = PCScreenFont.DefaultFont;

            int requiredWidth = TextHelper.GetTextWidth(Text, Font) + 24;
            if (Width < requiredWidth)
                Width = requiredWidth;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            hoverProgress = UiAnimator.Approach(hoverProgress, hovered ? 1f : 0f, 0.24f);
            clickProgress = UiAnimator.Approach(clickProgress, 0f, 0.20f);
            Color fill = UiAnimator.Blend(BackgroundColor, UiPalette.SurfaceHover, hoverProgress * 0.80f);
            fill = UiAnimator.Blend(fill, UiPalette.SurfacePressed, clickProgress * 0.8f);
            Color edge = UiAnimator.Blend(UiPalette.Border, UiPalette.AccentBorder, hoverProgress);
            UiPainter.RoundedPanel(canvas, X, Y, Width, Height, UiMetrics.ButtonRadius, fill, edge);
            TextHelper.DrawCenteredString(canvas, Text, Font, TextColor,
                X, Y + (clickProgress > 0.5f ? 1 : 0), Width, Height);
        }

        public bool Contains(int mouseX, int mouseY)
        {
            return mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;
        }

        public void InvokeClick()
        {
            clickProgress = 1f;
            UiAnimator.RequestFrames(7);
            OnClick?.Invoke();
        }
    }
}