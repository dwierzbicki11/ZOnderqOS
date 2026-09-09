using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public class ScrollBar : Widget
    {
        private bool dragging;
        private int dragOffset;
        private int contentItems;
        private int visibleItems;
        private int value;

        public int Value
        {
            get { return value; }
            set
            {
                int max = MaxValue;
                this.value = Math.Max(0, Math.Min(value, max));
                ValueChanged?.Invoke(this.value);
            }
        }

        public int MaxValue
        {
            get { return Math.Max(0, contentItems - visibleItems); }
        }

        public Action<int> ValueChanged { get; set; }

        public ScrollBar(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = true;
        }

        public void SetRange(int contentCount, int visibleCount)
        {
            contentItems = Math.Max(0, contentCount);
            visibleItems = Math.Max(1, visibleCount);
            Value = value;
        }

        private int GetThumbHeight()
        {
            if (contentItems <= visibleItems || Height <= 0)
                return Height;

            int thumb = (Height * visibleItems) / contentItems;
            return Math.Max(24, Math.Min(Height, thumb));
        }

        private int GetThumbY()
        {
            int travel = Height - GetThumbHeight();
            if (travel <= 0 || MaxValue <= 0)
                return Y;
            return Y + (Value * travel) / MaxValue;
        }

        public bool HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible || MaxValue <= 0)
                return false;

            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();

            if (isClicked && !wasClicked)
            {
                if (mouseX >= X && mouseX <= X + Width && mouseY >= thumbY && mouseY <= thumbY + thumbHeight)
                {
                    dragging = true;
                    dragOffset = mouseY - thumbY;
                    return true;
                }

                if (mouseX >= X && mouseX <= X + Width && mouseY >= Y && mouseY <= Y + Height)
                {
                    Value = mouseY < thumbY ? Value - visibleItems : Value + visibleItems;
                    return true;
                }
            }

            if (!isClicked)
                dragging = false;

            if (dragging && isClicked)
            {
                int travel = Height - thumbHeight;
                if (travel > 0)
                {
                    int newY = Math.Max(Y, Math.Min(Y + travel, mouseY - dragOffset));
                    Value = ((newY - Y) * MaxValue) / travel;
                }
                return true;
            }

            return false;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || MaxValue <= 0)
                return;

            canvas.DrawFilledRectangle(Color.FromArgb(45, 45, 45), X, Y, Width, Height);
            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();
            canvas.DrawFilledRectangle(Color.FromArgb(100, 115, 130), X + 1, thumbY, Math.Max(1, Width - 2), thumbHeight);
            canvas.DrawRectangle(Color.FromArgb(150, 165, 180), X + 1, thumbY, Math.Max(1, Width - 2), thumbHeight);
        }
    }
}
