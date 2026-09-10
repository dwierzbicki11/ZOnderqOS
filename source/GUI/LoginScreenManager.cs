using System;
using System.Drawing;
using System.IO;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Mouse;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Full-screen authentication gate used before the desktop is exposed. User data is
    /// loaded once when the screen starts (or on F5); the render loop itself reuses fixed
    /// arrays and cached strings so an idle login screen does not continuously allocate.
    /// </summary>
    public static class LoginScreenManager
    {
        public static bool Run()
        {
            try
            {
                Canvas canvas = Canvas.GetFullScreen();
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                IconManager.Preload();

                LoginScreen screen = new LoginScreen((int)canvas.Width, (int)canvas.Height);
                bool previousLeft = false;
                int previousMouseX = -1;
                int previousMouseY = -1;
                int frame = 0;

                int mouseX = (int)MouseManager.X;
                int mouseY = (int)MouseManager.Y;
                screen.Render(canvas);
                Cursor.Draw(canvas, mouseX, mouseY);
                canvas.Display();

                while (!SecurityContext.IsAuthenticated)
                {
                    frame++;
                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;
                        screen.HandleKeyboard(key);
                        if (SecurityContext.IsAuthenticated)
                            break;
                    }

                    mouseX = (int)MouseManager.X;
                    mouseY = (int)MouseManager.Y;
                    bool currentLeft = MouseManager.LeftButton;
                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeft != previousLeft;

                    if (!SecurityContext.IsAuthenticated)
                        screen.HandleMouse(mouseX, mouseY, currentLeft, previousLeft);

                    previousLeft = currentLeft;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

                    // Rare heartbeat keeps the cursor/status visually healthy without
                    // forcing a permanent 60 FPS full-frame redraw at 1920x1080.
                    bool heartbeat = frame % 134 == 0;
                    if (!SecurityContext.IsAuthenticated &&
                        (keyboardActivity || pointerMoved || buttonChanged || heartbeat))
                    {
                        screen.Render(canvas);
                        Cursor.Draw(canvas, mouseX, mouseY);
                        canvas.Display();
                    }

                    Thread.Sleep(15);
                }

                return true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Graphical login failed: " + ex.Message, "AUTH");
                return false;
            }
        }

        private sealed class LoginScreen
        {
            private const int MaxUsers = 8;
            private const int MaxUsernameLength = 32;
            private const int MaxPasswordLength = 128;
            private const string PasswordMask =
                "********************************************************************************************************************************";

            private static readonly Color Background = Color.FromArgb(10, 16, 24);
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
            private readonly string[] users = new string[MaxUsers];

            private int userCount;
            private int selectedUser;
            private bool passwordField;
            private int failedAttempts;
            private string username = string.Empty;
            private string password = string.Empty;
            private string status = "WYBIERZ KONTO LUB WPISZ LOGIN";
            private Color statusColor = Muted;

            public LoginScreen(int width, int height)
            {
                screenWidth = width;
                screenHeight = height;
                LoadUsers();
            }

            public void HandleKeyboard(KeyEvent key)
            {
                if (key.Key == ConsoleKeyEx.F5)
                {
                    LoadUsers();
                    SetStatus("ODSWIEZONO LISTE KONT", Good);
                    return;
                }

                if (key.Key == ConsoleKeyEx.UpArrow)
                {
                    SelectUser(-1);
                    return;
                }

                if (key.Key == ConsoleKeyEx.DownArrow)
                {
                    SelectUser(1);
                    return;
                }

                if (key.Key == ConsoleKeyEx.Escape)
                {
                    if (passwordField)
                    {
                        password = string.Empty;
                        passwordField = false;
                        SetStatus("LOGIN", Muted);
                    }
                    else
                    {
                        username = string.Empty;
                        SetStatus("WPISZ NAZWE UZYTKOWNIKA", Muted);
                    }
                    return;
                }

                if (key.Key == ConsoleKeyEx.Backspace)
                {
                    if (passwordField)
                    {
                        if (password.Length > 0)
                            password = password.Substring(0, password.Length - 1);
                    }
                    else if (username.Length > 0)
                    {
                        username = username.Substring(0, username.Length - 1);
                    }
                    return;
                }

                if (key.Key == ConsoleKeyEx.Delete)
                {
                    if (passwordField)
                        password = string.Empty;
                    else
                        username = string.Empty;
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    if (!passwordField)
                    {
                        if (!string.IsNullOrEmpty(username))
                        {
                            passwordField = true;
                            SetStatus("WPISZ HASLO I NACISNIJ ENTER", Muted);
                        }
                    }
                    else
                    {
                        Authenticate();
                    }
                    return;
                }

                char ch = key.KeyChar;
                if (ch < 32 || ch > 126)
                    return;

                if (passwordField)
                {
                    if (password.Length < MaxPasswordLength)
                        password += ch;
                }
                else if (username.Length < MaxUsernameLength && IsUsernameChar(ch))
                {
                    username += ch;
                }
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (!leftClicked || leftWasClicked)
                    return;

                int cardX = (screenWidth - 660) / 2;
                int cardY = (screenHeight - 500) / 2;
                int rightX = cardX + 242;

                for (int i = 0; i < userCount && i < 6; i++)
                {
                    int rowY = cardY + 142 + i * 48;
                    if (Hit(mouseX, mouseY, cardX + 24, rowY, 190, 40))
                    {
                        selectedUser = i;
                        username = users[i] ?? string.Empty;
                        password = string.Empty;
                        passwordField = true;
                        SetStatus("WPISZ HASLO", Muted);
                        return;
                    }
                }

                if (Hit(mouseX, mouseY, rightX + 28, cardY + 148, 350, 48))
                {
                    passwordField = false;
                    return;
                }

                if (Hit(mouseX, mouseY, rightX + 28, cardY + 224, 350, 48))
                {
                    passwordField = true;
                    return;
                }

                if (Hit(mouseX, mouseY, rightX + 28, cardY + 300, 350, 48))
                {
                    if (!passwordField && !string.IsNullOrEmpty(username))
                    {
                        passwordField = true;
                        SetStatus("WPISZ HASLO", Muted);
                    }
                    else if (passwordField)
                    {
                        Authenticate();
                    }
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
                SmallTextRenderer.Draw(canvas, "SECURE SESSION  /  COSMOS GEN3", 86, 56, Muted);

                int cardX = (screenWidth - 660) / 2;
                int cardY = (screenHeight - 500) / 2;
                const int cardWidth = 660;
                const int cardHeight = 500;
                int rightX = cardX + 242;

                canvas.DrawFilledRectangle(Color.FromArgb(5, 9, 14), cardX + 8, cardY + 9, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(Card, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawRectangle(Border, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, cardX, cardY, 5, cardHeight);

                SmallTextRenderer.Draw(canvas, "KONTA", cardX + 24, cardY + 34, Muted);
                canvas.DrawString("Logowanie", PCScreenFont.DefaultFont, Text, rightX + 28, cardY + 27);
                SmallTextRenderer.Draw(canvas, "AUTORYZACJA WYMAGANA PRZED URUCHOMIENIEM PULPITU",
                    rightX + 30, cardY + 68, Muted);

                RenderUsers(canvas, cardX, cardY);
                canvas.DrawLine(Border, rightX, cardY + 20, rightX, cardY + cardHeight - 20);

                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", rightX + 28, cardY + 128, Muted);
                RenderInput(canvas, rightX + 28, cardY + 148, 350, 48, false);

                SmallTextRenderer.Draw(canvas, "HASLO", rightX + 28, cardY + 204, Muted);
                RenderInput(canvas, rightX + 28, cardY + 224, 350, 48, true);

                Color loginColor = passwordField && !string.IsNullOrEmpty(username)
                    ? SystemTheme.Accent
                    : Color.FromArgb(54, 67, 79);
                canvas.DrawFilledRectangle(loginColor, rightX + 28, cardY + 300, 350, 48);
                canvas.DrawRectangle(SystemTheme.AccentBorder, rightX + 28, cardY + 300, 350, 48);
                IconManager.DrawScaled(canvas, IconType.Play, rightX + 44, cardY + 313, 22, 22);
                SmallTextRenderer.DrawCentered(canvas, passwordField ? "ZALOGUJ" : "DALEJ",
                    rightX + 78, cardY + 321, 280, Text);

                canvas.DrawFilledRectangle(CardAlt, rightX + 28, cardY + 375, 350, 67);
                canvas.DrawRectangle(Border, rightX + 28, cardY + 375, 350, 67);
                canvas.DrawFilledRectangle(statusColor, rightX + 43, cardY + 394, 6, 6);
                SmallTextRenderer.DrawClipped(canvas, status, rightX + 61, cardY + 393, 300, statusColor);
                SmallTextRenderer.Draw(canvas, "F5 KONTA  |  GORA/DOL WYBOR  |  ESC WSTECZ",
                    rightX + 43, cardY + 417, Muted);

                SmallTextRenderer.Draw(canvas, "ZOnderqOS chroni pulpit przed dostepem bez uwierzytelnienia.",
                    28, screenHeight - 38, Muted);
                RenderPowerButton(canvas, screenWidth - 226, screenHeight - 66, 92, "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, screenWidth - 124, screenHeight - 66, 100, "WYLACZ", IconType.Shutdown, true);
            }

            private void RenderUsers(Canvas canvas, int cardX, int cardY)
            {
                if (userCount <= 0)
                {
                    SmallTextRenderer.Draw(canvas, "BRAK KONT", cardX + 24, cardY + 148, Danger);
                    SmallTextRenderer.Draw(canvas, "WPISZ LOGIN RECZNIE", cardX + 24, cardY + 169, Muted);
                    return;
                }

                int shown = System.Math.Min(6, userCount);
                for (int i = 0; i < shown; i++)
                {
                    int rowY = cardY + 142 + i * 48;
                    bool selected = i == selectedUser;
                    canvas.DrawFilledRectangle(selected ? SystemTheme.AccentSoft : CardAlt,
                        cardX + 24, rowY, 190, 40);
                    canvas.DrawRectangle(selected ? SystemTheme.AccentBorder : Border,
                        cardX + 24, rowY, 190, 40);
                    IconManager.DrawScaled(canvas, IconType.Start, cardX + 35, rowY + 9, 22, 22);
                    SmallTextRenderer.DrawClipped(canvas, users[i], cardX + 68, rowY + 17, 132,
                        selected ? Text : Muted);
                }
            }

            private void RenderInput(Canvas canvas, int x, int y, int width, int height, bool passwordInput)
            {
                bool active = passwordField == passwordInput;
                canvas.DrawFilledRectangle(active ? Color.FromArgb(31, 42, 51) : CardAlt, x, y, width, height);
                canvas.DrawRectangle(active ? SystemTheme.AccentBorder : Border, x, y, width, height);
                if (active)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 3, height);

                if (!passwordInput)
                {
                    if (string.IsNullOrEmpty(username))
                        SmallTextRenderer.Draw(canvas, "nazwa uzytkownika", x + 15, y + 21, Muted);
                    else
                        SmallTextRenderer.DrawClipped(canvas, username, x + 15, y + 21, width - 30, Text);
                }
                else
                {
                    if (password.Length == 0)
                    {
                        SmallTextRenderer.Draw(canvas, "haslo", x + 15, y + 21, Muted);
                    }
                    else
                    {
                        int visible = System.Math.Min(password.Length, 48);
                        SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, x + 15, y + 21, Text);
                    }
                }
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

            private void LoadUsers()
            {
                for (int i = 0; i < MaxUsers; i++)
                    users[i] = null;

                userCount = 0;
                try
                {
                    if (File.Exists("/etc/passwd"))
                    {
                        string[] lines = File.ReadAllLines("/etc/passwd");
                        for (int i = 0; i < lines.Length && userCount < MaxUsers; i++)
                        {
                            string line = lines[i];
                            if (string.IsNullOrEmpty(line))
                                continue;

                            int separator = line.IndexOf(':');
                            if (separator <= 0)
                                continue;

                            string user = line.Substring(0, separator);
                            if (string.IsNullOrEmpty(user))
                                continue;

                            users[userCount++] = user;
                        }
                    }
                }
                catch
                {
                    userCount = 0;
                }

                selectedUser = 0;
                if (userCount > 0)
                    username = users[0] ?? string.Empty;
                else
                    username = string.Empty;

                password = string.Empty;
                passwordField = userCount > 0;
            }

            private void SelectUser(int delta)
            {
                if (userCount <= 0)
                    return;

                selectedUser += delta;
                if (selectedUser < 0)
                    selectedUser = userCount - 1;
                else if (selectedUser >= userCount)
                    selectedUser = 0;

                username = users[selectedUser] ?? string.Empty;
                password = string.Empty;
                passwordField = true;
                SetStatus("WYBRANO KONTO - WPISZ HASLO", Muted);
            }

            private void Authenticate()
            {
                if (string.IsNullOrEmpty(username))
                {
                    passwordField = false;
                    SetStatus("BRAK NAZWY UZYTKOWNIKA", Danger);
                    return;
                }

                if (UserManager.TryStartSession(username, password))
                {
                    failedAttempts = 0;
                    password = string.Empty;
                    SetStatus("ZALOGOWANO", Good);
                    return;
                }

                failedAttempts++;
                password = string.Empty;
                passwordField = true;
                SetStatus("NIEPRAWIDLOWY LOGIN LUB HASLO", Danger);

                // A short escalating delay slows trivial brute-force attempts while
                // avoiding a persistent lockout that could make the OS unrecoverable.
                Thread.Sleep(failedAttempts >= 3 ? 1800 : 650);
                if (failedAttempts >= 3)
                    failedAttempts = 0;
            }

            private void SetStatus(string message, Color color)
            {
                status = message ?? string.Empty;
                statusColor = color;
            }

            private static bool IsUsernameChar(char ch)
            {
                return (ch >= 'a' && ch <= 'z') ||
                       (ch >= 'A' && ch <= 'Z') ||
                       (ch >= '0' && ch <= '9') ||
                       ch == '_' || ch == '-';
            }

            private static bool Hit(int px, int py, int x, int y, int width, int height)
            {
                return px >= x && px < x + width && py >= y && py < y + height;
            }
        }
    }
}
