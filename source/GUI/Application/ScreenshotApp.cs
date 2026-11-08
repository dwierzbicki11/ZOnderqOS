using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Apps
{
    public sealed class ScreenshotApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color PanelBg = Color.FromArgb(27, 34, 41);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color ButtonNormal = Color.FromArgb(33, 42, 52);
        private static readonly Color ButtonHover = Color.FromArgb(46, 60, 75);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(78, 185, 126);

        private readonly Action closeCallback;
        private readonly Action<string> openImageCallback;
        private readonly Func<Canvas> canvasProvider;
        private string lastSavedPath;
        private string status = "Gotowy do wykonania zrzutu";
        private int hoveredBtn = -1; // 0: Capture, 1: Open

        public ScreenshotApp(int x, int y, Func<Canvas> getCanvas, Action<string> onOpenImage, Action onClose)
            : base("Zrzut ekranu")
        {
            canvasProvider = getCanvas;
            openImageCallback = onOpenImage;
            closeCallback = onClose;

            Window = new Window(x, y, 440, 340, "Zrzut ekranu");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void TakeScreenshot()
        {
            Canvas canvas = canvasProvider?.Invoke();
            if (canvas == null)
            {
                status = "Blad: Brak dostepu do ekranu";
                return;
            }

            status = "Trwa zapisywanie zrzutu...";
            string path = ScreenshotService.CaptureAndSave(canvas);
            if (!string.IsNullOrEmpty(path))
            {
                lastSavedPath = path;
                status = "Zapisano: " + Path.GetFileName(path);
            }
            else
            {
                status = "Nie udalo sie zapisac zrzutu";
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);

            int btnW = 200;
            int btnH = 34;
            int btnX = contentX + (contentWidth - btnW) / 2;
            int btn1Y = contentY + 160;
            int btn2Y = contentY + 204;

            hoveredBtn = -1;
            if (mouseX >= btnX && mouseX < btnX + btnW)
            {
                if (mouseY >= btn1Y && mouseY < btn1Y + btnH)
                    hoveredBtn = 0;
                else if (mouseY >= btn2Y && mouseY < btn2Y + btnH && !string.IsNullOrEmpty(lastSavedPath))
                    hoveredBtn = 1;
            }

            if (leftClicked && !leftWasClicked)
            {
                if (hoveredBtn == 0)
                {
                    TakeScreenshot();
                }
                else if (hoveredBtn == 1 && !string.IsNullOrEmpty(lastSavedPath))
                {
                    openImageCallback?.Invoke(lastSavedPath);
                }
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Fill background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header Banner
            int bannerY = contentY + 8;
            int bannerH = 130;
            canvas.DrawFilledRectangle(PanelBg, contentX + 8, bannerY, contentWidth - 16, bannerH);
            canvas.DrawFilledRectangle(Border, contentX + 8, bannerY, contentWidth - 16, bannerH);

            // Camera Icon / Graphic
            int icX = contentX + 24;
            int icY = bannerY + 22;
            canvas.DrawFilledRectangle(ButtonNormal, icX, icY + 8, 48, 36);
            canvas.DrawFilledRectangle(AccentColor, icX + 16, icY + 2, 16, 8);
            canvas.DrawFilledRectangle(Color.FromArgb(46, 117, 182), icX + 14, icY + 16, 20, 20);
            canvas.DrawFilledRectangle(Color.White, icX + 22, icY + 24, 4, 4);

            // Details
            int textX = icX + 64;
            SmallTextRenderer.Draw(canvas, "ZRZUT EKRANU ZOnderqOS", textX, bannerY + 16, TextColor);
            SmallTextRenderer.Draw(canvas, "Rozdzielczosc: " + canvas.Width + " x " + canvas.Height, textX, bannerY + 36, MutedColor);
            SmallTextRenderer.Draw(canvas, "Format: 32-bit BMP (pelna jakosc)", textX, bannerY + 52, MutedColor);
            SmallTextRenderer.Draw(canvas, "Lokalizacja: 0:/Screenshots/", textX, bannerY + 68, MutedColor);
            SmallTextRenderer.Draw(canvas, status, textX, bannerY + 92, AccentColor);

            // Main Capture Button
            int btnW = 220;
            int btnH = 34;
            int btnX = contentX + (contentWidth - btnW) / 2;
            int btn1Y = contentY + 154;

            Color b1Bg = (hoveredBtn == 0) ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(b1Bg, btnX, btn1Y, btnW, btnH);
            canvas.DrawFilledRectangle(Border, btnX, btn1Y, btnW, 1);
            canvas.DrawFilledRectangle(Border, btnX, btn1Y + btnH - 1, btnW, 1);
            canvas.DrawFilledRectangle(Border, btnX, btn1Y, 1, btnH);
            canvas.DrawFilledRectangle(Border, btnX + btnW - 1, btn1Y, 1, btnH);
            SmallTextRenderer.DrawCentered(canvas, "PRZECHWYC CALY EKRAN", btnX, btn1Y + 13, btnW, AccentColor);

            // Secondary Button: Open in Viewer (enabled if screenshot taken)
            if (!string.IsNullOrEmpty(lastSavedPath))
            {
                int btn2Y = contentY + 198;
                Color b2Bg = (hoveredBtn == 1) ? ButtonHover : ButtonNormal;
                canvas.DrawFilledRectangle(b2Bg, btnX, btn2Y, btnW, btnH);
                canvas.DrawFilledRectangle(Border, btnX, btn2Y, btnW, 1);
                canvas.DrawFilledRectangle(Border, btnX, btn2Y + btnH - 1, btnW, 1);
                canvas.DrawFilledRectangle(Border, btnX, btn2Y, 1, btnH);
                canvas.DrawFilledRectangle(Border, btnX + btnW - 1, btn2Y, 1, btnH);
                SmallTextRenderer.DrawCentered(canvas, "OTWORZ W PRZEGLADARCE", btnX, btn2Y + 13, btnW, TextColor);
            }

            // Shortcut Hint
            int hintY = contentY + contentHeight - 24;
            SmallTextRenderer.DrawCentered(canvas, "Skrot klawiszowy: Ctrl + Alt + S robi natychmiastowy zrzut",
                contentX, hintY, contentWidth, MutedColor);
        }
    }
}
