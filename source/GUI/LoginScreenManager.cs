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
    /// Full-screen authentication gate used before the desktop is exposed. Account data
    /// is loaded only on entry/F5. Password input uses a fixed character buffer and the
    /// shared AuthenticationGuard provides throttling across graphical authentication.
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
                bool previousLeft = MouseManager.LeftButton;
                int previousMouseX = (int)MouseManager.X;
                int previousMouseY = (int)MouseManager.Y;
                int frame = 0;

                screen.Render(canvas);
                Cursor.Draw(canvas, previousMouseX, previousMouseY);
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

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeft = MouseManager.LeftButton;
                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeft != previousLeft;

                    if (!SecurityContext.IsAuthenticated)
                        screen.HandleMouse(mouseX, mouseY, currentLeft, previousLeft);

                    previousLeft = currentLeft;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

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

                screen.ClearSensitiveData();
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
            private const int MaxUsers = 32;
            private const int VisibleUsers = 6;
            private const int MaxUsernameLength = 32;
            private const int MaxPasswordLength = PasswordPolicy.MaxLength;
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
            private readonly char[] password = new char[MaxPasswordLength];

            private int userCount;
            private int selectedUser;
            private int userOffset;
            private int passwordLength;
            private bool passwordField;
            private string username = string.Empty;
            private string status = "WYBIERZ KONTO LUB WPISZ LOGIN";
            private Color statusColor = Muted;

            public LoginScreen(int width, int height)
            {
                screenWidth = width;
                screenHeight = height;
                LoadUsers();
            }

            public void ClearSensitiveData()
            {
                ClearPassword();
            }

            public void HandleKeyboard(KeyEvent key)
            {
                if (key == null)
                    return;

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
                        ClearPassword();
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
                        if (passwordLength > 0)
                        {
                            passwordLength--;
                            password[passwordLength] = '\0';
                        }
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
                        ClearPassword();
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
                            ClearPassword();
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
                    if (passwordLength < MaxPasswordLength)
                        password[passwordLength++] = ch;
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

                for (int row = 0; row < VisibleUsers; row++)
                {
                    int index = userOffset + row;
                    if (index >= userCount)
                        break;

                    int rowY = cardY + 142 + row * 48;
                    if (Hit(mouseX, mouseY, cardX + 24, rowY, 190, 40))
                    {
                        selectedUser = index;
                        username = users[index] ?? string.Empty;
                        ClearPassword();
                        passwordField = true;
                        KeepSelectedVisible();
                        ShowAccountStatus();
                        return;
                    }
                }

                if (Hit(mouseX, mouseY, rightX + 28, cardY + 148, 350, 48))
                {
                    passwordField = false;
                    ClearPassword();
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
                        ClearPassword();
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
                if (userCount > VisibleUsers)
                {
                    SmallTextRenderer.DrawUInt(canvas, (ulong)(selectedUser + 1), cardX + 144, cardY + 34, Text);
                    SmallTextRenderer.Draw(canvas, "/", cardX + 164, cardY + 34, Muted);
                    SmallTextRenderer.DrawUInt(canvas, (ulong)userCount, cardX + 176, cardY + 34, Text);
                }

                canvas.DrawString("Logowanie", PCScreenFont.DefaultFont, Text, rightX + 28, cardY + 27);
                SmallTextRenderer.Draw(canvas, "AUTORYZACJA WYMAGANA PRZED URUCHOMIENIEM PULPITU",
                    rightX + 30, cardY + 68, Muted);

                RenderUsers(canvas, cardX, cardY);
                canvas.DrawLine(Border, rightX, cardY + 20, rightX, cardY + cardHeight - 20);

                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", rightX + 28, cardY + 128, Muted);
                RenderInput(canvas, rightX + 28, cardY + 148, 350, 48, false);

                SmallTextRenderer.Draw(canvas, "HASLO", rightX + 28, cardY + 204, Muted);
                RenderInput(canvas, rightX + 28, cardY + 224, 350, 48, true);

                Color loginColor = passwordField && !string.IsNullOrEmpty(username) && passwordLength > 0
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

                SmallTextRenderer.Draw(canvas,
                    "ZOnderqOS chroni pulpit przed dostepem bez uwierzytelnienia.",
                    28, screenHeight - 38, Muted);
                RenderPowerButton(canvas, screenWidth - 226, screenHeight - 66, 92,
                    "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, screenWidth - 124, screenHeight - 66, 100,
                    "WYLACZ", IconType.Shutdown, true);
            }

            private void RenderUsers(Canvas canvas, int cardX, int cardY)
            {
                if (userCount <= 0)
                {
                    SmallTextRenderer.Draw(canvas, "BRAK KONT", cardX + 24, cardY + 148, Danger);
                    SmallTextRenderer.Draw(canvas, "WPISZ LOGIN RECZNIE", cardX + 24, cardY + 169, Muted);
                    return;
                }

                for (int row = 0; row < VisibleUsers; row++)
                {
                    int index = userOffset + row;
                    if (index >= userCount)
                        break;

                    int rowY = cardY + 142 + row * 48;
                    bool selected = index == selectedUser;
                    canvas.DrawFilledRectangle(selected ? SystemTheme.AccentSoft : CardAlt,
                        cardX + 24, rowY, 190, 40);
                    canvas.DrawRectangle(selected ? SystemTheme.AccentBorder : Border,
                        cardX + 24, rowY, 190, 40);
                    IconManager.DrawScaled(canvas, IconType.Start, cardX + 35, rowY + 9, 22, 22);
                    SmallTextRenderer.DrawClipped(canvas, users[index], cardX + 68, rowY + 17, 132,
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
                else if (passwordLength == 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", x + 15, y + 21, Muted);
                }
                else
                {
                    int visible = System.Math.Min(passwordLength, 48);
                    SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, x + 15, y + 21, Text);
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
                userOffset = 0;
                string lastUser = UserProfileManager.GetLastUser();
                if (!string.IsNullOrEmpty(lastUser))
                {
                    for (int i = 0; i < userCount; i++)
                    {
                        if (users[i] == lastUser)
                        {
                            selectedUser = i;
                            break;
                        }
                    }
                }

                if (userCount > 0)
                    username = users[selectedUser] ?? string.Empty;
                else
                    username = string.Empty;

                ClearPassword();
                passwordField = userCount > 0;
                KeepSelectedVisible();
                ShowAccountStatus();
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
                ClearPassword();
                passwordField = true;
                KeepSelectedVisible();
                ShowAccountStatus();
            }

            private void KeepSelectedVisible()
            {
                if (selectedUser < userOffset)
                    userOffset = selectedUser;
                else if (selectedUser >= userOffset + VisibleUsers)
                    userOffset = selectedUser - VisibleUsers + 1;

                int maxOffset = System.Math.Max(0, userCount - VisibleUsers);
                if (userOffset > maxOffset)
                    userOffset = maxOffset;
                if (userOffset < 0)
                    userOffset = 0;
            }

            private void ShowAccountStatus()
            {
                SetStatus(userCount > 0
                    ? "WYBRANO KONTO - WPISZ HASLO"
                    : "WPISZ NAZWE UZYTKOWNIKA", Muted);
            }

            private void Authenticate()
            {
                if (string.IsNullOrEmpty(username))
                {
                    passwordField = false;
                    ClearPassword();
                    SetStatus("BRAK NAZWY UZYTKOWNIKA", Danger);
                    return;
                }

                if (passwordLength <= 0)
                {
                    SetStatus("HASLO JEST WYMAGANE", Danger);
                    return;
                }

                int retryAfter;
                if (!AuthenticationGuard.CanAttempt(username, out retryAfter))
                {
                    ClearPassword();
                    SetStatus("ZA DUZO PROB - ODCZEKAJ " + retryAfter + " S", Danger);
                    return;
                }

                string enteredPassword = new string(password, 0, passwordLength);
                ClearPassword();
                bool ok = UserManager.TryStartSession(username, enteredPassword);
                enteredPassword = null;

                if (ok)
                {
                    AuthenticationGuard.RecordSuccess(username);
                    SetStatus("ZALOGOWANO", Good);
                    return;
                }

                AuthenticationGuard.RecordFailure(username);
                retryAfter = AuthenticationGuard.GetRetryAfterSeconds(username);
                if (retryAfter > 0)
                    SetStatus("NIEPRAWIDLOWE DANE - BLOKADA " + retryAfter + " S", Danger);
                else
                    SetStatus("NIEPRAWIDLOWY LOGIN LUB HASLO", Danger);
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
