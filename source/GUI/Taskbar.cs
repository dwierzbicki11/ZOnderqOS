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
        private int hoveredAppIndex = -1;

        private const int StartButtonWidth = 48;
        private const int AppButtonSize = 40;
        private const int AppButtonGap = 5;
        private const int SidePadding = 6;

        public Taskbar(int screenWidth, int screenHeight, int height, Action onStartClick, ApplicationManager manager)
            : base(0, screenHeight - height, screenWidth, height)
        {
            applicationManager = manager;

            var startButton = new Button(0, Y, StartButtonWidth, height, "", onStartClick);
            startButton.BackgroundColor = Color.FromArgb(27, 34, 42);
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
            if (name.Contains("about") || name.Contains("diagnostic"))
                return IconType.About;
            return IconType.File;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            canvas.DrawFilledRectangle(Color.FromArgb(13, 17, 22), X, Y, Width, Height);
            canvas.DrawFilledRectangle(BackgroundColor, X, Y + 1, Width, Height - 1);
            canvas.DrawLine(Color.FromArgb(48, 60, 72), X, Y, X + Width, Y);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);

            IconManager.DrawScaled(canvas, IconType.Start, SidePadding + 9, Y + 11, 22, 22);
            canvas.DrawLine(Color.FromArgb(48, 59, 70), StartButtonWidth + 2, Y + 8,
                StartButtonWidth + 2, Y + Height - 8);

            int appX = StartButtonWidth + SidePadding + 6;
            int rightReserved = 160;
            int maxAppX = Width - rightReserved;
            List<Application> apps = applicationManager != null ? applicationManager.Applications : null;
            int visibleIndex = 0;

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
                    bool hovered = visibleIndex == hoveredAppIndex;

                    Color bg = active
                        ? Color.FromArgb(43, 66, 88)
                        : hovered ? Color.FromArgb(35, 45, 55) : Color.FromArgb(25, 31, 38);
                    Color border = active
                        ? Color.FromArgb(67, 135, 191)
                        : hovered ? Color.FromArgb(61, 76, 90) : Color.FromArgb(38, 47, 56);

                    canvas.DrawFilledRectangle(bg, appX, Y + 3, AppButtonSize, Height - 6);
                    canvas.DrawRectangle(border, appX, Y + 3, AppButtonSize, Height - 6);
                    IconManager.DrawScaled(canvas, GetApplicationIcon(app), appX + 9, Y + 9, 22, 22);

                    if (active && !minimized)
                        canvas.DrawFilledRectangle(Color.FromArgb(69, 153, 218), appX + 10, Y + Height - 4, 20, 3);
                    else if (minimized)
                        canvas.DrawFilledRectangle(Color.FromArgb(98, 107, 116), appX + 14, Y + Height - 4, 12, 2);
                    else
                        canvas.DrawFilledRectangle(Color.FromArgb(58, 67, 76), appX + 17, Y + Height - 3, 6, 1);

                    appX += AppButtonSize + AppButtonGap;
                    visibleIndex++;
                }
            }

            int clockY = Y + (Height - TextHelper.GetTextHeight(font)) / 2;
            DateTime currentTime = DateTime.UtcNow.AddHours(2);
            string timeString = currentTime.ToString("HH:mm");
            int timeWidth = TextHelper.GetTextWidth(timeString, font);
            int clockX = Width - timeWidth - 16;

            canvas.DrawLine(Color.FromArgb(48, 59, 70), clockX - 12, Y + 8,
                clockX - 12, Y + Height - 8);
            canvas.DrawString(timeString, font, Color.FromArgb(225, 231, 237), clockX, clockY);
        }

        public void UpdateInteractions(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            for (int i = 0; i < Children.Count; i++)
            {
                if (Children[i] is Button btn)
                {
                    bool isOver = btn.Contains(mouseX, mouseY);
                    btn.IsHovered = isOver;
                    if (isOver && isClicked && !wasClicked)
                        btn.InvokeClick();
                }
            }

            hoveredAppIndex = -1;
            if (mouseY < Y || mouseY >= Y + Height || applicationManager == null)
                return;

            int appX = StartButtonWidth + SidePadding + 6;
            int rightReserved = 160;
            int maxAppX = Width - rightReserved;
            List<Application> apps = applicationManager.Applications;
            int visibleIndex = 0;

            for (int i = 0; i < apps.Count; i++)
            {
                Application app = apps[i];
                if (app == null || !app.IsRunning || app.Window == null)
                    continue;
                if (appX + AppButtonSize > maxAppX)
                    break;

                if (mouseX >= appX && mouseX < appX + AppButtonSize)
                {
                    hoveredAppIndex = visibleIndex;
                    if (isClicked && !wasClicked)
                        applicationManager.ToggleMinimize(app);
                    return;
                }

                appX += AppButtonSize + AppButtonGap;
                visibleIndex++;
            }
        }
    }
}
