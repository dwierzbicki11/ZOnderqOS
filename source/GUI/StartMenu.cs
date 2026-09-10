using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public class StartMenu : Widget
    {
        public List<Button> Items { get; } = new List<Button>();

        private readonly ScrollBar scrollBar;
        private int scrollOffset;

        private const int ItemHeight = 40;
        private const int Padding = 10;
        private const int HeaderHeight = 58;
        private const int FooterHeight = 30;

        public StartMenu(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = false;
            scrollBar = new ScrollBar(X + Width - 15, Y + HeaderHeight + Padding, 8,
                Math.Max(20, Height - HeaderHeight - FooterHeight - Padding * 2));
            scrollBar.ValueChanged = value => scrollOffset = value;
        }

        public void AddItem(string text, Action onClick)
        {
            var button = new Button(X + Padding, Y + HeaderHeight + Padding, Width - 30, ItemHeight - 6, text, () =>
            {
                onClick?.Invoke();
                Visible = false;
            });

            button.BackgroundColor = Color.FromArgb(42, 49, 57);
            button.TextColor = Color.FromArgb(232, 236, 240);
            Items.Add(button);
            UpdateLayout();
        }

        private int GetVisibleItemCount()
        {
            int availableHeight = Height - HeaderHeight - FooterHeight - Padding * 2;
            return Math.Max(1, availableHeight / ItemHeight);
        }

        private void UpdateLayout()
        {
            int visibleCount = GetVisibleItemCount();
            int maxOffset = Math.Max(0, Items.Count - visibleCount);
            scrollOffset = Math.Max(0, Math.Min(scrollOffset, maxOffset));

            int contentY = Y + HeaderHeight + Padding;
            int contentHeight = Math.Max(20, Height - HeaderHeight - FooterHeight - Padding * 2);

            scrollBar.X = X + Width - 15;
            scrollBar.Y = contentY;
            scrollBar.Width = 8;
            scrollBar.Height = contentHeight;
            scrollBar.SetRange(Items.Count, visibleCount);
            scrollBar.Value = scrollOffset;

            for (int i = 0; i < Items.Count; i++)
            {
                int visibleIndex = i - scrollOffset;
                Items[i].X = X + Padding;
                Items[i].Y = contentY + visibleIndex * ItemHeight;
                Items[i].Width = Math.Max(30, Width - 30);
                Items[i].Height = ItemHeight - 6;
                Items[i].Visible = visibleIndex >= 0 && visibleIndex < visibleCount;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateLayout();

            Color shadow = Color.FromArgb(18, 22, 27);
            Color background = Color.FromArgb(29, 34, 40);
            Color header = Color.FromArgb(34, 42, 50);
            Color footer = Color.FromArgb(25, 30, 36);
            Color accent = Color.FromArgb(65, 140, 200);
            Color border = Color.FromArgb(72, 84, 96);

            canvas.DrawFilledRectangle(shadow, X + 5, Y + 5, Width, Height);
            canvas.DrawFilledRectangle(background, X, Y, Width, Height);
            canvas.DrawRectangle(border, X, Y, Width, Height);

            canvas.DrawFilledRectangle(header, X + 1, Y + 1, Width - 2, HeaderHeight - 1);
            canvas.DrawFilledRectangle(accent, X + 1, Y + 1, 4, HeaderHeight - 1);
            canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Color.FromArgb(238, 242, 246), X + 16, Y + 8);
            canvas.DrawLine(Color.FromArgb(58, 70, 82), X + 1, Y + HeaderHeight, X + Width - 2, Y + HeaderHeight);

            foreach (var button in Items)
            {
                if (button.Visible)
                    button.Render(canvas);
            }

            scrollBar.Render(canvas);

            int footerY = Y + Height - FooterHeight;
            canvas.DrawFilledRectangle(footer, X + 1, footerY, Width - 2, FooterHeight - 1);
            canvas.DrawLine(Color.FromArgb(58, 70, 82), X + 1, footerY, X + Width - 2, footerY);
            canvas.DrawString("GEN3", PCScreenFont.DefaultFont, Color.FromArgb(135, 153, 170), X + 16, footerY + 1);
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible)
                return;

            UpdateLayout();
            if (scrollBar.HandleMouse(mouseX, mouseY, isClicked, wasClicked))
                return;

            foreach (var button in Items)
            {
                if (!button.Visible)
                    continue;

                bool isOver = button.Contains(mouseX, mouseY);
                button.IsHovered = isOver;
                if (isOver && isClicked && !wasClicked)
                    button.InvokeClick();
            }
        }
    }
}
