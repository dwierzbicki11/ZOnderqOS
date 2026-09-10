using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Apps;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public class Taskbar : Widget
    {
        public List<Widget> Children { get; } = new List<Widget>();
        public Color BackgroundColor { get; set; } = Color.FromArgb(22, 27, 33);

        private readonly ApplicationManager applicationManager;
        private int hoveredAppIndex = -1;
        private int cachedMinute = -1;
        private int cachedDay = -1;
        private int cachedVolumeCount = -1;
        private string cachedTime = "--:--";
        private string cachedDate = "--.--";
        private string cachedVolumeLabel = "VOL 0";

        private const int StartButtonWidth = 48;
        private const int AppButtonSize = 40;
        private const int AppButtonGap = 5;
        private const int SidePadding = 6;
        private const int TrayWidth = 238;

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

            string name = app.Name;
            if (name.IndexOf("terminal", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("shell", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Terminal;
            if (name.IndexOf("task", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("zadan", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Settings;
            if (name.IndexOf("file", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("manager", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Folder;
            if (name.IndexOf("setting", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.Settings;
            if (name.IndexOf("about", StringComparison.OrdinalIgnoreCase) >= 0 ||
                name.IndexOf("diagnostic", StringComparison.OrdinalIgnoreCase) >= 0)
                return IconType.About;
            return IconType.File;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            UpdateTrayCache();

            canvas.DrawFilledRectangle(Color.FromArgb(13, 17, 22), X, Y, Width, Height);
            canvas.DrawFilledRectangle(BackgroundColor, X, Y + 1, Width, Height - 1);
            canvas.DrawLine(Color.FromArgb(48, 60, 72), X, Y, X + Width, Y);

            for (int i = 0; i < Children.Count; i++)
                Children[i].Render(canvas);

            IconManager.DrawScaled(canvas, IconType.Start, SidePadding + 9, Y + 11, 22, 22);
            canvas.DrawLine(Color.FromArgb(48, 59, 70), StartButtonWidth + 2, Y + 8,
                StartButtonWidth + 2, Y + Height - 8);

            int appX = StartButtonWidth + SidePadding + 6;
            int maxAppX = Width - TrayWidth;
            List<Application> apps = applicationManager != null ? applicationManager.Applications : null;
            Application activeApplication = applicationManager != null ? applicationManager.ActiveApplication : null;
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

                    bool active = activeApplication == app;
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

            RenderSystemTray(canvas);
        }

        private void UpdateTrayCache()
        {
            DateTime currentTime = DateTime.UtcNow.AddHours(2);
            int minute = currentTime.Hour * 60 + currentTime.Minute;
            if (minute != cachedMinute)
            {
                cachedMinute = minute;
                cachedTime = currentTime.ToString("HH:mm");
            }

            if (currentTime.DayOfYear != cachedDay)
            {
                cachedDay = currentTime.DayOfYear;
                cachedDate = currentTime.ToString("dd.MM");
            }

            int volumeCount = 0;
            try
            {
                volumeCount = StorageManager.Partitions.Count;
            }
            catch
            {
                volumeCount = 0;
            }

            if (volumeCount != cachedVolumeCount)
            {
                cachedVolumeCount = volumeCount;
                cachedVolumeLabel = "VOL " + volumeCount;
            }
        }

        private void RenderSystemTray(Canvas canvas)
        {
            int trayX = Width - TrayWidth;
            canvas.DrawLine(Color.FromArgb(48, 59, 70), trayX, Y + 7, trayX, Y + Height - 7);

            bool networkReady = global::ZonderqOS.Network.IsReady;
            DrawTrayTile(canvas, trayX + 9, 58, IconType.Settings, "NET", networkReady);
            DrawTrayTile(canvas, trayX + 73, 72, IconType.FileManager, cachedVolumeLabel, cachedVolumeCount > 0);

            int clockX = Width - 78;
            canvas.DrawLine(Color.FromArgb(48, 59, 70), clockX - 10, Y + 7,
                clockX - 10, Y + Height - 7);

            SmallTextRenderer.DrawCentered(canvas, cachedTime, clockX, Y + 12, 68,
                Color.FromArgb(232, 237, 242));
            SmallTextRenderer.DrawCentered(canvas, cachedDate, clockX, Y + 27, 68,
                Color.FromArgb(132, 148, 162));
        }

        private void DrawTrayTile(Canvas canvas, int x, int width, IconType icon, string label, bool ready)
        {
            Color background = ready ? Color.FromArgb(31, 48, 60) : Color.FromArgb(30, 35, 41);
            Color border = ready ? Color.FromArgb(55, 93, 119) : Color.FromArgb(49, 58, 67);
            Color indicator = ready ? Color.FromArgb(72, 173, 118) : Color.FromArgb(111, 119, 127);

            canvas.DrawFilledRectangle(background, x, Y + 6, width, Height - 12);
            canvas.DrawRectangle(border, x, Y + 6, width, Height - 12);
            IconManager.DrawScaled(canvas, icon, x + 6, Y + 13, 14, 14);
            SmallTextRenderer.DrawClipped(canvas, label, x + 25, Y + 18,
                Math.Max(8, width - 32), Color.FromArgb(207, 216, 224));
            canvas.DrawFilledRectangle(indicator, x + width - 7, Y + 10, 3, 3);
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
            int maxAppX = Width - TrayWidth;
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
