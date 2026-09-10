using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class Taskbar : Widget
    {
        public List<Widget> Children { get; } = new List<Widget>();
        public Color BackgroundColor { get; set; } = Color.FromArgb(22, 27, 33);

        private readonly ApplicationManager applicationManager;
        private readonly Font font = PCScreenFont.DefaultFont;
        private const int StartButtonWidth = 48;
        private const int AppButtonSize = 40;
        private const int AppButtonGap = 5;
        private const int SidePadding = 6;

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick, ApplicationManager manager)
            : base(0, screenHeight - height, screenWidth, height)
        {
            applicationManager = manager;

            var startButton = new Button(0, Y, StartButtonWidth, height, "", onStartClick);
            startButton.BackgroundColor = Color.FromArgb(31, 38, 46);
            startButton.TextColor = Color.White;
            Children.Add(startButton);
        }

        private IconType GetApplicationIcon(Application app)
        {
            if (app == null || string.IsNullOrEmpty(app.Name))
                return IconType.File;

            string name = app.Name.ToLowerInvariant();
            if (name.Contains("terminal") || name.Contains("shell"))
                return IconType.Terminal;
            if (name.Contains("file") || name.Contains("manager"))
                return IconType.Folder;
            if (name.Contains("setting"))
                return IconType.Settings;
            if (name.Contains("about"))
                return IconType.About;
            return IconType.File;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(Color.FromArgb(15, 19, 24), X, Y, Width, Height);
            canvas.DrawFilledRectangle(BackgroundColor, X, Y + 1, Width, Height - 1);
            canvas.DrawLine(Color.FromArgb(52, 65, 78), X, Y, X + Width, Y);

            foreach (var child in Children)
                child.Render(canvas);

            // Start button is intentionally icon-only to keep the taskbar compact.
            IconManager.DrawScaled(canvas, IconType.Start, SidePadding + 10, Y + 12, 20, 20);

            int appX = StartButtonWidth + SidePadding + 6;
            int rightReserved = 160;
            int maxAppX = Width - rightReserved;
            List<Application> apps = applicationManager != null ? applicationManager.Applications : null;

            if (apps != null)
            {
                for (int i = 0; i < apps.Count; i++)
                {
                    Application app = apps[i];
                    if (app == null || !app.IsRunning || app.Window == null)
                        continue;
                    if (appX + AppButtonSize > maxAppX)
                        break;

                    bool active = applicationManager.ActiveApplication == app;
                    bool minimized = app.Window.IsMinimized;

                    Color bg = active
                        ? Color.FromArgb(48, 77, 104)
                        : Color.FromArgb(30, 37, 45);
                    Color border = active
                        ? Color.FromArgb(76, 145, 202)
                        : Color.FromArgb(54, 64, 74);

                    canvas.DrawFilledRectangle(bg, appX, Y + 2, AppButtonSize, Height - 4);
                    canvas.DrawRectangle(border, appX, Y + 2, AppButtonSize, Height - 4);

                    IconManager.DrawScaled(canvas, GetApplicationIcon(app), appX + 10, Y + 10, 20, 20);

                    if (active && !minimized)
                    {
                        canvas.DrawFilledRectangle(Color.FromArgb(70, 155, 220), appX + 12, Y + Height - 4, 16, 2);
                    }
                    else if (minimized)
                    {
                        canvas.DrawFilledRectangle(Color.FromArgb(105, 112, 120), appX + 14, Y + Height - 4, 12, 2);
                    }

                    appX += AppButtonSize + AppButtonGap;
                }
            }

            // Compact system area on the right.
            int clockY = Y + (Height - TextHelper.GetTextHeight(font)) / 2;
            DateTime currentTime = DateTime.UtcNow.AddHours(2);
            string timeString = currentTime.ToString("HH:mm");
            int timeWidth = TextHelper.GetTextWidth(timeString, font);
            int clockX = Width - timeWidth - 16;

            canvas.DrawLine(Color.FromArgb(55, 65, 75), clockX - 10, Y + 9, clockX - 10, Y + Height - 9);
            canvas.DrawString(timeString, font, Color.FromArgb(225, 231, 237), clockX, clockY);
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

            int appX = StartButtonWidth + SidePadding + 6;
            int rightReserved = 160;
            int maxAppX = Width - rightReserved;
            List<Application> apps = applicationManager.Applications;

            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null)
                    continue;
                if (appX + AppButtonSize > maxAppX)
                    break;

                if (mouseX >= appX && mouseX < appX + AppButtonSize)
                {
                    applicationManager.ToggleMinimize(app);
                    return;
                }

                appX += AppButtonSize + AppButtonGap;
            }
        }
    }
}
