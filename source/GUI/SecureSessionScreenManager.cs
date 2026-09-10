using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    public enum SecureSessionAction
    {
        Cancel = 0,
        Lock = 1,
        Logout = 2
    }

    /// <summary>
    /// Secure-session screen opened with Ctrl+Alt+Delete. It provides only actions that
    /// are safe to expose while a user session is active. Rendering is event driven, so
    /// leaving this screen open does not continuously redraw the full framebuffer.
    /// </summary>
    public static class SecureSessionScreenManager
    {
        private static readonly string[] Labels =
        {
            "ZABLOKUJ",
            "WYLOGUJ",
            "ANULUJ",
            "REBOOT",
            "WYLACZ"
        };

        private static readonly string[] Descriptions =
        {
            "Zachowaj otwarte aplikacje i zabezpiecz pulpit haslem",
            "Zamknij aplikacje sesji i wroc do ekranu logowania",
            "Wroc do aktualnej sesji bez zmian",
            "Uruchom ponownie ZOnderqOS",
            "Wylacz komputer"
        };

        private static readonly IconType[] Icons =
        {
            IconType.Start,
            IconType.Close,
            IconType.ArrowUp,
            IconType.Reboot,
            IconType.Shutdown
        };

        private static readonly Color Background = Color.FromArgb(9, 14, 21);
        private static readonly Color TopBand = Color.FromArgb(15, 23, 33);
        private static readonly Color Panel = Color.FromArgb(24, 30, 37);
        private static readonly Color PanelHover = Color.FromArgb(34, 45, 55);
        private static readonly Color PanelSelected = Color.FromArgb(37, 57, 74);
        private static readonly Color Border = Color.FromArgb(58, 72, 85);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(134, 150, 165);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        public static SecureSessionAction Run(Canvas canvas)
        {
            if (canvas == null || !SecurityContext.IsAuthenticated)
                return SecureSessionAction.Cancel;

            try
            {
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                Screen screen = new Screen((int)canvas.Width, (int)canvas.Height);

                bool previousLeft = MouseManager.LeftButton;
                int previousMouseX = (int)MouseManager.X;
                int previousMouseY = (int)MouseManager.Y;
                int frame = 0;

                screen.Render(canvas);
                Cursor.Draw(canvas, previousMouseX, previousMouseY);
                canvas.Display();

                while (!screen.Finished && SecurityContext.IsAuthenticated)
                {
                    frame++;
                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;
                        screen.HandleKeyboard(key);
                        if (screen.Finished)
                            break;
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeft = MouseManager.LeftButton;
                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeft != previousLeft;

                    if (!screen.Finished)
                        screen.HandleMouse(mouseX, mouseY, currentLeft, previousLeft);

                    previousLeft = currentLeft;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

                    bool heartbeat = frame % 134 == 0;
                    if (!screen.Finished && (keyboardActivity || pointerMoved || buttonChanged || heartbeat))
                    {
                        screen.Render(canvas);
                        Cursor.Draw(canvas, mouseX, mouseY);
                        canvas.Display();
                    }

                    Thread.Sleep(15);
                }

                return screen.Action;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Secure session screen failed: " + ex.Message, "AUTH");
                return SecureSessionAction.Cancel;
            }
        }

        private sealed class Screen
        {
            private readonly int screenWidth;
            private readonly int screenHeight;
            private int selected;
            private int hovered = -1;

            public bool Finished { get; private set; }
            public SecureSessionAction Action { get; private set; }

            public Screen(int width, int height)
            {
                screenWidth = width;
                screenHeight = height;
                selected = 0;
                Action = SecureSessionAction.Cancel;
            }

            public void HandleKeyboard(KeyEvent key)
            {
                if (key == null || Finished)
                    return;

                if (key.Key == ConsoleKeyEx.Escape)
                {
                    Action = SecureSessionAction.Cancel;
                    Finished = true;
                    return;
                }

                if (key.Key == ConsoleKeyEx.UpArrow)
                {
                    selected--;
                    if (selected < 0)
                        selected = Labels.Length - 1;
                    return;
                }

                if (key.Key == ConsoleKeyEx.DownArrow)
                {
                    selected++;
                    if (selected >= Labels.Length)
                        selected = 0;
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                    Activate(selected);
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                hovered = HitRow(mouseX, mouseY);
                if (hovered >= 0)
                    selected = hovered;

                if (leftClicked && !leftWasClicked && hovered >= 0)
                    Activate(hovered);
            }

            public void Render(Canvas canvas)
            {
                canvas.Clear(Background);
                canvas.DrawFilledRectangle(TopBand, 0, 0, screenWidth, 86);
                canvas.DrawFilledRectangle(SystemTheme.Accent, 0, 84, screenWidth, 2);

                IconManager.DrawScaled(canvas, IconType.Start, 28, 22, 40, 40);
                canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, 84, 18);
                SmallTextRenderer.Draw(canvas, "SECURE SESSION  /  CTRL+ALT+DELETE", 86, 56, Muted);

                int panelWidth = 640;
                int panelHeight = 480;
                int panelX = (screenWidth - panelWidth) / 2;
                int panelY = (screenHeight - panelHeight) / 2;

                canvas.DrawFilledRectangle(Color.FromArgb(5, 9, 14), panelX + 8, panelY + 9, panelWidth, panelHeight);
                canvas.DrawFilledRectangle(Panel, panelX, panelY, panelWidth, panelHeight);
                canvas.DrawRectangle(Border, panelX, panelY, panelWidth, panelHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, panelX, panelY, 5, panelHeight);

                canvas.DrawString("Opcje sesji", PCScreenFont.DefaultFont, Text, panelX + 28, panelY + 24);
                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", panelX + 30, panelY + 66, Muted);
                SmallTextRenderer.DrawClipped(canvas, SecurityContext.CurrentUser ?? "-",
                    panelX + 108, panelY + 66, 240, SystemTheme.Accent);
                SmallTextRenderer.Draw(canvas, "GORA/DOL WYBOR  |  ENTER WYKONAJ  |  ESC ANULUJ",
                    panelX + 30, panelY + 90, Muted);

                for (int i = 0; i < Labels.Length; i++)
                    RenderRow(canvas, panelX, panelY, i);

                SmallTextRenderer.Draw(canvas,
                    "Wylogowanie zamyka aplikacje, aby kolejny uzytkownik nie odziedziczyl danych sesji.",
                    panelX + 30, panelY + panelHeight - 28, Muted);
            }

            private void RenderRow(Canvas canvas, int panelX, int panelY, int index)
            {
                int x = panelX + 30;
                int y = panelY + 122 + index * 64;
                int width = 580;
                int height = 54;
                bool isSelected = index == selected;
                bool isHovered = index == hovered;
                bool danger = index == 1 || index == 4;

                Color bg = isSelected ? PanelSelected : isHovered ? PanelHover : Color.FromArgb(29, 37, 45);
                Color border = isSelected || isHovered ? SystemTheme.AccentBorder : Border;
                canvas.DrawFilledRectangle(bg, x, y, width, height);
                canvas.DrawRectangle(border, x, y, width, height);
                if (isSelected)
                    canvas.DrawFilledRectangle(danger ? Danger : SystemTheme.Accent, x, y + 5, 3, height - 10);

                IconManager.DrawScaled(canvas, Icons[index], x + 15, y + 15, 24, 24);
                SmallTextRenderer.Draw(canvas, Labels[index], x + 54, y + 15, danger ? Danger : Text);
                SmallTextRenderer.DrawClipped(canvas, Descriptions[index], x + 54, y + 34,
                    width - 72, Muted);
            }

            private int HitRow(int mouseX, int mouseY)
            {
                int panelX = (screenWidth - 640) / 2;
                int panelY = (screenHeight - 480) / 2;
                int x = panelX + 30;
                int width = 580;

                for (int i = 0; i < Labels.Length; i++)
                {
                    int y = panelY + 122 + i * 64;
                    if (mouseX >= x && mouseX < x + width && mouseY >= y && mouseY < y + 54)
                        return i;
                }

                return -1;
            }

            private void Activate(int index)
            {
                if (index == 0)
                {
                    Action = SecureSessionAction.Lock;
                    Finished = true;
                }
                else if (index == 1)
                {
                    Action = SecureSessionAction.Logout;
                    Finished = true;
                }
                else if (index == 2)
                {
                    Action = SecureSessionAction.Cancel;
                    Finished = true;
                }
                else if (index == 3)
                {
                    Cosmos.Kernel.System.Power.Reboot();
                }
                else if (index == 4)
                {
                    Cosmos.Kernel.System.Power.Shutdown();
                }
            }
        }
    }
}
