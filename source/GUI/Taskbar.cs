using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using ZonderqOS.GUI.Apps;

namespace ZonderqOS.GUI
{
    public class Taskbar : Widget
    {
        public List<Widget> Children { get; } = new List<Widget>();
        public Color BackgroundColor { get; set; } = Color.FromArgb(30, 30, 30);

        private readonly string resolutionText;
        private readonly ApplicationManager applicationManager;
        private readonly Font font = PCScreenFont.DefaultFont;
        private const int AppButtonWidth = 150;
        private const int AppButtonGap = 4;

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick, ApplicationManager manager)
            : base(0, screenHeight - height, screenWidth, height)
        {
            resolutionText = $"Render: {screenWidth}x{screenHeight}";
            applicationManager = manager;

            var startButton = new Button(0, Y, 100, height, " Start ", onStartClick);
            startButton.BackgroundColor = Color.DarkSlateBlue;
            startButton.TextColor = Color.White;
            Children.Add(startButton);
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(BackgroundColor, X, Y, Width, Height);
            canvas.DrawLine(Color.FromArgb(80, 80, 80), X, Y, X + Width, Y);

            foreach (var child in Children)
                child.Render(canvas);

            int appX = 106;
            int maxAppX = Width - 430;
            List<Application> apps = applicationManager != null ? applicationManager.Applications : null;
            if (apps != null)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    Application app = apps[i];
                    if (app == null || !app.IsRunning || app.Window == null)
                        continue;
                    if (appX + AppButtonWidth > maxAppX)
                        break;

                    bool active = applicationManager.ActiveApplication == app;
                    bool minimized = app.Window.IsMinimized;
                    Color bg = active ? Color.FromArgb(65, 95, 125) : Color.FromArgb(45, 45, 45);
                    if (minimized)
                        bg = Color.FromArgb(38, 38, 38);
                    canvas.DrawFilledRectangle(bg, appX, Y + 3, AppButtonWidth, Height - 6);
                    canvas.DrawRectangle(active ? Color.LightSteelBlue : Color.FromArgb(90, 90, 90), appX, Y + 3, AppButtonWidth, Height - 6);

                    string name = app.Name ?? "Application";
                    int textWidth = TextHelper.GetTextWidth(name, font);
                    if (textWidth > AppButtonWidth - 14)
                    {
                        int maxChars = Math.Max(1, (AppButtonWidth - 24) / 16);
                        if (name.Length > maxChars)
                            name = name.Substring(0, maxChars) + "...";
                    }
                    canvas.DrawString(name, font, minimized ? Color.LightGray : Color.White, appX + 8, Y + 4);
                    appX += AppButtonWidth + AppButtonGap;
                }
            }

            int textHeight = TextHelper.GetTextHeight(font);
            int textY = Y + (Height - textHeight) / 2;
            canvas.DrawString(resolutionText, font, Color.LightGreen, Math.Max(106, Width - 405), textY);

            DateTime currentTime = DateTime.UtcNow.AddHours(2);
            string timeString = currentTime.ToString("HH:mm:ss");
            int textWidthTime = TextHelper.GetTextWidth(timeString, font);
            int textX = Width - textWidthTime - 20;
            canvas.DrawString(timeString, font, Color.White, textX, textY);
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            foreach (var child in Children)
            {
                if (child is Button btn)
                {
                    bool isOver = btn.Contains(mouseX, mouseY);
                    btn.IsHovered = isOver;
                    if (isOver && isClicked && !wasClicked)
                        btn.InvokeClick();
                }
            }

            if (!isClicked || wasClicked || mouseY < Y || mouseY >= Y + Height || applicationManager == null)
                return;

            int appX = 106;
            int maxAppX = Width - 430;
            List<Application> apps = applicationManager.Applications;
            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null)
                    continue;
                if (appX + AppButtonWidth > maxAppX)
                    break;

                if (mouseX >= appX && mouseX < appX + AppButtonWidth)
                {
                    applicationManager.ToggleMinimize(app);
                    return;
                }
                appX += AppButtonWidth + AppButtonGap;
            }
        }
    }
}
