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
        private int dragTrackX, dragTrackY, dragTrackWidth, dragTrackHeight;
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
            // Old drag offsets do not apply to a resized list.
            if (rangeChanged)
                dragging = false;
            Value = value;
        }

        private int GetThumbHeight()
        {
            if (Height <= 0)
                return 0;
            if (contentItems <= visibleItems)
                return Height;

            // Large lists must not overflow the intermediate product.
            int thumb = (int)(((long)Height * visibleItems) / contentItems);
            // On short tracks the old 22px minimum filled the entire
            // track, leaving no travel even with thousands of hidden rows.
            // Reserve at least one pixel of travel whenever Height > 1.
            int minimumThumb = Math.Min(22, Math.Max(1, Height / 2));
            int maximumThumb = Height > 1 ? Height - 1 : Height;
            return Math.Min(maximumThumb, Math.Max(minimumThumb, thumb));
        }

        private int GetThumbY()
        {
            int travel = Height - GetThumbHeight();
            if (travel <= 0 || MaxValue <= 0)
                return Y;
            return Y + (int)(((long)Value * travel) / MaxValue);
        }

        public bool HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            hovered = Visible && Width > 0 && Height > 0 &&
                mouseX >= X && (long)mouseX - X < Width &&
                mouseY >= Y && (long)mouseY - Y < Height;

            if (!Visible || Width <= 0 || Height <= 0 || MaxValue <= 0)
            {
                dragging = false;
                return false;
            }

            // A window resize or move invalidates the old thumb offset.
            // Otherwise a held mouse button can jump the list on the next frame.
            if (dragging && (X != dragTrackX || Y != dragTrackY ||
                Width != dragTrackWidth || Height != dragTrackHeight))
                dragging = false;

            // A modal overlay may swallow the release of an earlier drag.
            // A new press is a new gesture, never a continuation of the
            // stale drag with its old thumb offset.
            if (isClicked && !wasClicked)
                dragging = false;

            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();

            if (isClicked && !wasClicked)
            {
                if (mouseX >= X && (long)mouseX - X < Width &&
                    mouseY >= thumbY && (long)mouseY - thumbY < thumbHeight)
                {
                    dragging = true;
                    dragOffset = mouseY - thumbY;
                    dragTrackX = X;
                    dragTrackY = Y;
                    dragTrackWidth = Width;
                    dragTrackHeight = Height;
                    return true;
                }

                if (hovered)
                {
                    Value = mouseY < thumbY
                        ? Math.Max(0, Value - visibleItems)
                        : (int)Math.Min((long)MaxValue, (long)Value + visibleItems);
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
                    Value = (int)(((long)(newY - Y) * MaxValue) / travel);
                }
                return true;
            }

            return false;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible || Width <= 0 || Height <= 0 || MaxValue <= 0)
                return;

            int trackWidth = Math.Min(2, Width);
            int trackX = X + (Width - trackWidth) / 2;
            canvas.DrawFilledRectangle(Color.FromArgb(38, 45, 53), trackX, Y, trackWidth, Height);

            int thumbHeight = GetThumbHeight();
            int thumbY = GetThumbY();
            int thumbWidth = Math.Min(Width, Math.Max(4, Width - 2));
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
