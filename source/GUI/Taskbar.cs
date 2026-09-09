using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;

namespace ZonderqOS.GUI
{
    public class Taskbar : Widget
    {
        public List<Widget> Children { get; } = new List<Widget>();
        public Color BackgroundColor { get; set; } = Color.FromArgb(30, 30, 30);

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick) 
    : base(0, screenHeight - height, screenWidth, height)
        {
            // Przycisk "Menu" / "Start" wyzwala teraz przekazaną akcję (przełączenie widoczności)
            var startButton = new Button(0, Y, 100, height, " Start ", onStartClick);
            
            startButton.BackgroundColor = Color.DarkSlateBlue;
            startButton.TextColor = Color.White;

            Children.Add(startButton);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            // 1. Główne tło paska
            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            
            // 2. Jasna linia na górnej krawędzi (subtelny efekt 3D/odcięcia)
            canvas.DrawLine(Color.FromArgb(80, 80, 80), X, Y, X + Width, Y);

            // 3. Renderowanie kontrolek na pasku (przyciski)
            foreach (var child in Children)
            {
                child.Render(canvas);
            }

            DateTime currentTime = DateTime.UtcNow.AddHours(2); 

            // Dodajemy sekundy, co ułatwi debugowanie płynności (odświeżanie pętli)
            string timeString = currentTime.ToString("HH:mm:ss");

            Font font = PCScreenFont.DefaultFont;

            int textWidth = TextHelper.GetTextWidth(timeString, font);
            int textHeight = TextHelper.GetTextHeight(font);

            // Margines od prawej krawędzi (powiększony, by zmieścić sekundy)
            int textX = Width - textWidth - 20;
            // Wyśrodkowanie pionowe na pasku
            int textY = Y + (Height - textHeight) / 2;

            // Renderowanie tekstu
            canvas.DrawString(timeString, font, Color.White, textX, textY);
        }

        // Metoda do obsługi hit-testingu specyficzna dla paska
        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            foreach (var child in Children)
            {
                if (child is Button btn)
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
}