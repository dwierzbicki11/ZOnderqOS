using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public class StartMenu : Widget
    {
        public List<Button> Items { get; } = new List<Button>();

        public StartMenu(int x, int y, int width, int height) : base(x, y, width, height)
        {
            Visible = false; // Na starcie menu jest ukryte
        }

        public void AddItem(string text, Action onClick)
        {
            int btnHeight = 35;
            // Obliczanie pozycji Y dla kolejnego przycisku wewnątrz menu
            int currentY = Y + 5 + (Items.Count * btnHeight);
            
            var btn = new Button(X + 5, currentY, Width - 10, btnHeight - 5, text, () => {
                onClick?.Invoke();
                Visible = false; // Zamknij menu po kliknięciu w dowolną opcję
            });
            
            btn.BackgroundColor = Color.FromArgb(40, 40, 40);
            btn.TextColor = Color.White;
            Items.Add(btn);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            // Tło menu Start
            canvas.DrawFilledRectangle(Color.FromArgb(25, 25, 25), X, Y, Width, Height);
            // Wyraźne obramowanie (np. niebieskie, nawiązujące do systemów operacyjnych)
            canvas.DrawRectangle(Color.DeepSkyBlue, X, Y, Width, Height);

            foreach (var btn in Items)
            {
                btn.Render(canvas);
            }
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            if (!Visible) return;

            foreach (var btn in Items)
            {
                bool isOver = btn.Contains(mouseX, mouseY);
                btn.IsHovered = isOver;

                if (isOver && isClicked && !wasClicked)
                {
                    btn.InvokeClick();
                }
            }
        }
    }
}