using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public class ScrollBar : Widget
    {
        private bool dragging;
        private bool hovered;
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
                int next = Math.Max(0, Math.Min(value, max));
                if (this.value == next)
                    return;

                this.value = next;
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
            int nextContent = Math.Max(0, contentCount);
            int nextVisible = Math.Max(1, visibleCount);
            bool rangeChanged = contentItems != nextContent || visibleItems != nextVisible;

            contentItems = nextContent;
            visibleItems = nextVisible;
            Value = value;

            if (rangeChanged && MaxValue == 0 && this.value != 0)
                Value = 0;
        }

        private int GetThumbHeight()
        {
            if (contentItems <= visibleItems || Height <= 0)
                return Height;

            int thumb = (Height * visibleItems) / contentItems;
            return Math.Max(22, Math.Min(Height, thumb));
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
            hovered = mouseX >= X && mouseX < X + Width && mouseY >= Y && mouseY < Y + Height;

            if (!Visible || MaxValue <= 0)
            {
                dragging = false;
                return false;
            }

            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();

            if (isClicked && !wasClicked)
            {
                if (mouseX >= X && mouseX < X + Width && mouseY >= thumbY && mouseY < thumbY + thumbHeight)
                {
                    dragging = true;
                    dragOffset = mouseY - thumbY;
                    return true;
                }

                if (hovered)
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

            int trackX = X + Math.Max(0, Width / 2 - 1);
            canvas.DrawFilledRectangle(Color.FromArgb(38, 45, 53), trackX, Y, 2, Height);

            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();
            int thumbWidth = Math.Max(4, Width - 2);
            int thumbX = X + (Width - thumbWidth) / 2;

            Color thumb = dragging
                ? Color.FromArgb(70, 155, 220)
                : hovered
                    ? Color.FromArgb(105, 132, 154)
                    : Color.FromArgb(82, 99, 114);

            canvas.DrawFilledRectangle(thumb, thumbX, thumbY, thumbWidth, thumbHeight);
        }
    }
}
