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
    /// <summary>
    /// Full-screen session lock for the graphical desktop. The authenticated session and
    /// its applications stay alive behind this gate; only the current user's password can
    /// unlock it. The idle loop is event-driven and uses a rare heartbeat, matching the
    /// rest of the ZOnderqOS GUI instead of continuously redrawing the framebuffer.
    /// </summary>
    public static class LockScreenManager
    {
        public static bool Run(Canvas canvas)
        {
            if (canvas == null || !SecurityContext.IsAuthenticated ||
                string.IsNullOrEmpty(SecurityContext.CurrentUser))
                return false;

            try
            {
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                LockScreen screen = new LockScreen((int)canvas.Width, (int)canvas.Height,
                    SecurityContext.CurrentUser);

                bool previousLeft = MouseManager.LeftButton;
                int previousMouseX = (int)MouseManager.X;
                int previousMouseY = (int)MouseManager.Y;
                int frame = 0;

                screen.Render(canvas);
                Cursor.Draw(canvas, previousMouseX, previousMouseY);
                canvas.Display();

                while (!screen.Unlocked && SecurityContext.IsAuthenticated)
                {
                    frame++;
                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;
                        screen.HandleKeyboard(key);
                        if (screen.Unlocked || !SecurityContext.IsAuthenticated)
                            break;
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeft = MouseManager.LeftButton;
                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeft != previousLeft;

                    if (!screen.Unlocked && SecurityContext.IsAuthenticated)
                        screen.HandleMouse(mouseX, mouseY, currentLeft, previousLeft);

                    previousLeft = currentLeft;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

                    bool heartbeat = frame % 134 == 0;
                    if (!screen.Unlocked && SecurityContext.IsAuthenticated &&
                        (keyboardActivity || pointerMoved || buttonChanged || heartbeat))
                    {
                        screen.Render(canvas);
                        Cursor.Draw(canvas, mouseX, mouseY);
                        canvas.Display();
                    }

                    Thread.Sleep(15);
                }

                return screen.Unlocked && SecurityContext.IsAuthenticated;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Lock screen failed: " + ex.Message, "AUTH");
                return false;
            }
        }

        private sealed class LockScreen
        {
            private const int MaxPasswordLength = 128;
            private const string PasswordMask =
                "********************************************************************************************************************************";

            private static readonly Color Background = Color.FromArgb(9, 14, 21);
            private static readonly Color TopBand = Color.FromArgb(15, 23, 33);
            private static readonly Color Card = Color.FromArgb(24, 30, 37);
            private static readonly Color CardAlt = Color.FromArgb(29, 37, 45);
            private static readonly Color Border = Color.FromArgb(58, 72, 85);
            private static readonly Color Text = Color.FromArgb(232, 237, 242);
            private static readonly Color Muted = Color.FromArgb(134, 150, 165);
            private static readonly Color Good = Color.FromArgb(78, 185, 126);
            private static readonly Color Danger = Color.FromArgb(215, 86, 91);

            private readonly int screenWidth;
            private readonly int screenHeight;
            private readonly string username;
            private readonly char[] password = new char[MaxPasswordLength];

            private int passwordLength;
            private int failedAttempts;
            private string status = "SESJA ZABLOKOWANA";
            private Color statusColor = Muted;

            public bool Unlocked { get; private set; }

            public LockScreen(int width, int height, string currentUser)
            {
                screenWidth = width;
                screenHeight = height;
                username = currentUser ?? string.Empty;
            }

            public void HandleKeyboard(KeyEvent key)
            {
                if (key == null || Unlocked)
                    return;

                if (key.Key == ConsoleKeyEx.Backspace)
                {
                    if (passwordLength > 0)
                    {
                        passwordLength--;
                        password[passwordLength] = '\0';
                    }
                    return;
                }

                if (key.Key == ConsoleKeyEx.Delete || key.Key == ConsoleKeyEx.Escape)
                {
                    ClearPassword();
                    SetStatus("WPISZ HASLO, ABY ODBLOKOWAC", Muted);
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    Authenticate();
                    return;
                }

                char ch = key.KeyChar;
                if (ch >= 32 && ch <= 126 && passwordLength < MaxPasswordLength)
                {
                    password[passwordLength++] = ch;
                    SetStatus("ENTER ODBLOKOWUJE SESJE", Muted);
                }
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (Unlocked || !leftClicked || leftWasClicked)
                    return;

                int cardX = (screenWidth - 520) / 2;
                int cardY = (screenHeight - 390) / 2;

                if (Hit(mouseX, mouseY, cardX + 48, cardY + 230, 424, 50))
                {
                    Authenticate();
                    return;
                }

                if (Hit(mouseX, mouseY, screenWidth - 226, screenHeight - 66, 92, 42))
                {
                    Cosmos.Kernel.System.Power.Reboot();
                    return;
                }

                if (Hit(mouseX, mouseY, screenWidth - 124, screenHeight - 66, 100, 42))
                    Cosmos.Kernel.System.Power.Shutdown();
            }

            public void Render(Canvas canvas)
            {
                canvas.Clear(Background);
                canvas.DrawFilledRectangle(TopBand, 0, 0, screenWidth, 86);
                canvas.DrawFilledRectangle(SystemTheme.Accent, 0, 84, screenWidth, 2);

                IconManager.DrawScaled(canvas, IconType.Start, 28, 22, 40, 40);
                canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, 84, 18);
                SmallTextRenderer.Draw(canvas, "SECURE SESSION  /  LOCKED", 86, 56, Muted);

                int cardX = (screenWidth - 520) / 2;
                int cardY = (screenHeight - 390) / 2;
                const int cardWidth = 520;
                const int cardHeight = 390;

                canvas.DrawFilledRectangle(Color.FromArgb(5, 9, 14),
                    cardX + 8, cardY + 9, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(Card, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawRectangle(Border, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, cardX, cardY, 5, cardHeight);

                IconManager.DrawScaled(canvas, IconType.Start, cardX + 36, cardY + 35, 54, 54);
                canvas.DrawString("Sesja zablokowana", PCScreenFont.DefaultFont, Text,
                    cardX + 112, cardY + 34);
                SmallTextRenderer.Draw(canvas, "OTWARTE APLIKACJE POZOSTAJA W PAMIECI",
                    cardX + 114, cardY + 75, Muted);

                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", cardX + 48, cardY + 123, Muted);
                canvas.DrawFilledRectangle(CardAlt, cardX + 48, cardY + 143, 424, 46);
                canvas.DrawRectangle(Border, cardX + 48, cardY + 143, 424, 46);
                SmallTextRenderer.DrawClipped(canvas, username, cardX + 63, cardY + 162, 394, Text);

                SmallTextRenderer.Draw(canvas, "HASLO", cardX + 48, cardY + 202, Muted);
                canvas.DrawFilledRectangle(Color.FromArgb(31, 42, 51),
                    cardX + 48, cardY + 222, 424, 46);
                canvas.DrawRectangle(SystemTheme.AccentBorder,
                    cardX + 48, cardY + 222, 424, 46);
                canvas.DrawFilledRectangle(SystemTheme.Accent,
                    cardX + 48, cardY + 222, 3, 46);

                if (passwordLength == 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", cardX + 63, cardY + 241, Muted);
                }
                else
                {
                    int visible = System.Math.Min(passwordLength, 58);
                    SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible,
                        cardX + 63, cardY + 241, Text);
                }

                Color unlockColor = passwordLength > 0 ? SystemTheme.Accent : Color.FromArgb(54, 67, 79);
                canvas.DrawFilledRectangle(unlockColor, cardX + 48, cardY + 286, 424, 50);
                canvas.DrawRectangle(SystemTheme.AccentBorder, cardX + 48, cardY + 286, 424, 50);
                IconManager.DrawScaled(canvas, IconType.Play, cardX + 64, cardY + 300, 22, 22);
                SmallTextRenderer.DrawCentered(canvas, "ODBLOKUJ", cardX + 98, cardY + 307, 354, Text);

                canvas.DrawFilledRectangle(CardAlt, cardX + 48, cardY + 349, 424, 28);
                canvas.DrawRectangle(Border, cardX + 48, cardY + 349, 424, 28);
                canvas.DrawFilledRectangle(statusColor, cardX + 61, cardY + 360, 5, 5);
                SmallTextRenderer.DrawClipped(canvas, status, cardX + 77, cardY + 360, 380, statusColor);

                SmallTextRenderer.Draw(canvas,
                    "Ctrl+Alt+L blokuje pulpit bez zamykania uruchomionych aplikacji.",
                    28, screenHeight - 38, Muted);
                RenderPowerButton(canvas, screenWidth - 226, screenHeight - 66, 92,
                    "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, screenWidth - 124, screenHeight - 66, 100,
                    "WYLACZ", IconType.Shutdown, true);
            }

            private void Authenticate()
            {
                if (passwordLength <= 0)
                {
                    SetStatus("HASLO JEST WYMAGANE", Danger);
                    return;
                }

                string enteredPassword = new string(password, 0, passwordLength);
                bool ok;
                try
                {
                    ok = UserManager.ValidateCredentials(username, enteredPassword);
                }
                catch
                {
                    ok = false;
                }

                enteredPassword = null;
                ClearPassword();

                if (ok)
                {
                    Unlocked = true;
                    failedAttempts = 0;
                    SecurityLogger.LogEvent("INFO", "Graphical session unlocked for user " + username + ".");
                    return;
                }

                failedAttempts++;
                SecurityLogger.LogEvent("WARN", "Failed graphical unlock attempt for user " + username + ".");
                SetStatus("NIEPRAWIDLOWE HASLO", Danger);

                int delayMs = 250 + failedAttempts * 150;
                if (delayMs > 1200)
                    delayMs = 1200;
                Thread.Sleep(delayMs);
            }

            private void ClearPassword()
            {
                for (int i = 0; i < passwordLength; i++)
                    password[i] = '\0';
                passwordLength = 0;
            }

            private void SetStatus(string message, Color color)
            {
                status = message ?? string.Empty;
                statusColor = color;
            }

            private static void RenderPowerButton(Canvas canvas, int x, int y, int width,
                string label, IconType icon, bool danger)
            {
                Color bg = danger ? Color.FromArgb(57, 38, 43) : Color.FromArgb(27, 35, 43);
                Color border = danger ? Color.FromArgb(112, 61, 69) : Border;
                canvas.DrawFilledRectangle(bg, x, y, width, 42);
                canvas.DrawRectangle(border, x, y, width, 42);
                IconManager.DrawScaled(canvas, icon, x + 9, y + 10, 20, 20);
                SmallTextRenderer.DrawClipped(canvas, label, x + 36, y + 17, width - 42, Text);
            }

            private static bool Hit(int px, int py, int x, int y, int width, int height)
            {
                return px >= x && px < x + width && py >= y && py < y + height;
            }
        }
    }
}
