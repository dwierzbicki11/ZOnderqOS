using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public class StartMenu : Widget
    {
        public List<Button> Items { get; } = new List<Button>();

        private readonly ScrollBar scrollBar;
        private int scrollOffset;
        private const int ItemHeight = 35;
        private const int Padding = 5;

        public StartMenu(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = false;
            scrollBar = new ScrollBar(X + Width - 16, Y + Padding, 12, Height - Padding * 2);
            scrollBar.ValueChanged = value => scrollOffset = value;
        }

        public void AddItem(string text, Action onClick)
        {
            var btn = new Button(X + Padding, Y + Padding, Width - 26, ItemHeight - 5, text, () =>
            {
                onClick?.Invoke();
                Visible = false;
            });

            btn.BackgroundColor = Color.FromArgb(40, 40, 40);
            btn.TextColor = Color.White;
            Items.Add(btn);
            UpdateLayout();
        }

        private int GetVisibleItemCount()
        {
            return Math.Max(1, (Height - Padding * 2) / ItemHeight);
        }

        private void UpdateLayout()
        {
            int visibleCount = GetVisibleItemCount();
            int maxOffset = Math.Max(0, Items.Count - visibleCount);
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, maxOffset));
            scrollBar.X = X + Width - 16;
            scrollBar.Y = Y + Padding;
            scrollBar.Width = 12;
            scrollBar.Height = Math.Max(20, Height - Padding * 2);
            scrollBar.SetRange(Items.Count, visibleCount);
            scrollBar.Value = scrollOffset;

            for (int i = 0; i < Items.Count; i++)
            {
                int visibleIndex = i - scrollOffset;
                Items[i].X = X + Padding;
                Items[i].Y = Y + Padding + visibleIndex * ItemHeight;
                Items[i].Width = Math.Max(30, Width - 26);
                Items[i].Visible = visibleIndex >= 0 && visibleIndex < visibleCount;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            UpdateLayout();
            canvas.DrawFilledRectangle(Color.FromArgb(25, 25, 25), X, Y, Width, Height);
            canvas.DrawRectangle(Color.DeepSkyBlue, X, Y, Width, Height);

            foreach (var btn in Items)
            {
                if (btn.Visible)
                    btn.Render(canvas);
            }

            scrollBar.Render(canvas);
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible) return;

            UpdateLayout();
            if (scrollBar.HandleMouse(mouseX, mouseY, isClicked, wasClicked))
                return;

            foreach (var btn in Items)
            {
                if (!btn.Visible) continue;
                bool isOver = btn.Contains(mouseX, mouseY);
                btn.IsHovered = isOver;

                if (isOver && isClicked && !wasClicked)
                    btn.InvokeClick();
            }
        }
    }
}