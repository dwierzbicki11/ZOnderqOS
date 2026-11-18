using System;
using System.Drawing;
using System.IO;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;
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
            LoginScreen screen = null;
            try
            {
                Canvas canvas = DisplayModeSelector.OpenNativeOrBest();
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                IconManager.Preload();

                screen = new LoginScreen((int)canvas.Width, (int)canvas.Height);
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
            finally
            {
                screen?.ClearSensitiveData();
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

            private struct ScreenLayout
            {
                public bool Compact;
                public int TopBandHeight;
                public int TopIconX;
                public int TopIconY;
                public int TopIconSize;
                public int TopTitleX;
                public int TopTitleY;
                public int TopSubX;
                public int TopSubY;

                public int CardX;
                public int CardY;
                public int CardWidth;
                public int CardHeight;
                public int RightX;

                public int AccountsLabelX;
                public int AccountsLabelY;
                public int AccountsCounterX;
                public int UserRowX;
                public int UserRowY;
                public int UserRowWidth;
                public int UserRowHeight;
                public int UserRowStep;

                public int TitleY;
                public int SubtitleY;
                public int InputX;
                public int InputWidth;
                public int InputHeight;
                public int UserLabelY;
                public int UserInputY;
                public int PassLabelY;
                public int PassInputY;
                public int LoginBtnY;
                public int LoginBtnHeight;
                public int StatusY;
                public int StatusHeight;

                public int HintX;
                public int HintY;
                public int HintMaxWidth;
                public int PowerBtnY;
                public int PowerBtnHeight;
                public int RebootX;
                public int RebootWidth;
                public int ShutdownX;
                public int ShutdownWidth;
            }

            private ScreenLayout GetLayout()
            {
                ScreenLayout l = default(ScreenLayout);
                bool compact = screenWidth < 720 || screenHeight < 560;
                bool mediumHeight = !compact && screenHeight < 660;
                l.Compact = compact;

                if (compact)
                {
                    l.TopBandHeight = 56;
                    l.TopIconX = 20;
                    l.TopIconY = 10;
                    l.TopIconSize = 34;
                    l.TopTitleX = 64;
                    l.TopTitleY = 10;
                    l.TopSubX = 64;
                    l.TopSubY = 34;

                    l.PowerBtnHeight = 34;
                    l.PowerBtnY = screenHeight - 48;
                    l.RebootWidth = 92;
                    l.ShutdownWidth = 100;
                    l.RebootX = screenWidth - 226;
                    l.ShutdownX = screenWidth - 124;
                    l.HintX = 24;
                    l.HintY = l.PowerBtnY + (l.PowerBtnHeight - 7) / 2;
                    l.HintMaxWidth = System.Math.Max(60, l.RebootX - l.HintX - 16);

                    l.CardWidth = System.Math.Min(612, System.Math.Max(360, screenWidth - 28));
                    l.CardHeight = 336;
                    int availTop = l.TopBandHeight;
                    int availHeight = System.Math.Max(l.CardHeight, l.PowerBtnY - availTop);
                    l.CardX = (screenWidth - l.CardWidth) / 2;
                    l.CardY = availTop + (availHeight - l.CardHeight) / 2;

                    int leftWidth = l.CardWidth >= 560 ? 216 : System.Math.Max(140, l.CardWidth / 3);
                    l.RightX = l.CardX + leftWidth;

                    l.AccountsLabelX = l.CardX + 18;
                    l.AccountsLabelY = l.CardY + 20;
                    l.AccountsCounterX = l.CardX + leftWidth - 72;
                    l.UserRowX = l.CardX + 18;
                    l.UserRowY = l.CardY + 48;
                    l.UserRowWidth = leftWidth - 36;
                    l.UserRowHeight = 38;
                    l.UserRowStep = 44;

                    l.TitleY = l.CardY + 16;
                    l.SubtitleY = l.CardY + 40;
                    l.InputX = l.RightX + 22;
                    l.InputWidth = l.CardX + l.CardWidth - 22 - l.InputX;
                    l.InputHeight = 38;
                    l.UserLabelY = l.CardY + 64;
                    l.UserInputY = l.CardY + 78;
                    l.PassLabelY = l.CardY + 126;
                    l.PassInputY = l.CardY + 140;
                    l.LoginBtnY = l.CardY + 194;
                    l.LoginBtnHeight = 40;
                    l.StatusY = l.CardY + 250;
                    l.StatusHeight = 62;
                    return l;
                }

                l.TopBandHeight = 86;
                l.TopIconX = 28;
                l.TopIconY = 22;
                l.TopIconSize = 40;
                l.TopTitleX = 84;
                l.TopTitleY = 18;
                l.TopSubX = 86;
                l.TopSubY = 56;

                l.PowerBtnHeight = 42;
                l.PowerBtnY = screenHeight - 66;
                l.RebootWidth = 92;
                l.ShutdownWidth = 100;
                l.RebootX = screenWidth - 226;
                l.ShutdownX = screenWidth - 124;
                l.HintX = 28;
                l.HintY = screenHeight - 38;
                l.HintMaxWidth = System.Math.Max(60, l.RebootX - l.HintX - 16);

                l.CardWidth = 660;
                l.CardX = (screenWidth - l.CardWidth) / 2;
                l.RightX = l.CardX + 242;
                l.AccountsLabelX = l.CardX + 24;
                l.AccountsCounterX = l.CardX + 144;
                l.UserRowX = l.CardX + 24;
                l.UserRowWidth = 190;
                l.InputX = l.RightX + 28;
                l.InputWidth = 350;

                if (mediumHeight)
                {
                    l.CardHeight = 420;
                    int availTop = l.TopBandHeight;
                    int availHeight = System.Math.Max(l.CardHeight, l.PowerBtnY - availTop);
                    l.CardY = availTop + (availHeight - l.CardHeight) / 2;

                    l.AccountsLabelY = l.CardY + 22;
                    l.UserRowY = l.CardY + 116;
                    l.UserRowHeight = 38;
                    l.UserRowStep = 46;

                    l.TitleY = l.CardY + 20;
                    l.SubtitleY = l.CardY + 56;
                    l.InputHeight = 44;
                    l.UserLabelY = l.CardY + 96;
                    l.UserInputY = l.CardY + 112;
                    l.PassLabelY = l.CardY + 168;
                    l.PassInputY = l.CardY + 184;
                    l.LoginBtnY = l.CardY + 244;
                    l.LoginBtnHeight = 46;
                    l.StatusY = l.CardY + 308;
                    l.StatusHeight = 66;
                    return l;
                }

                l.CardHeight = 500;
                l.CardY = (screenHeight - l.CardHeight) / 2;
                l.AccountsLabelY = l.CardY + 34;
                l.UserRowY = l.CardY + 142;
                l.UserRowHeight = 40;
                l.UserRowStep = 48;

                l.TitleY = l.CardY + 27;
                l.SubtitleY = l.CardY + 68;
                l.InputHeight = 48;
                l.UserLabelY = l.CardY + 128;
                l.UserInputY = l.CardY + 148;
                l.PassLabelY = l.CardY + 204;
                l.PassInputY = l.CardY + 224;
                l.LoginBtnY = l.CardY + 300;
                l.LoginBtnHeight = 48;
                l.StatusY = l.CardY + 375;
                l.StatusHeight = 67;
                return l;
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (!leftClicked || leftWasClicked)
                    return;

                ScreenLayout layout = GetLayout();

                for (int row = 0; row < VisibleUsers; row++)
                {
                    int index = userOffset + row;
                    if (index >= userCount)
                        break;

                    int rowY = layout.UserRowY + row * layout.UserRowStep;
                    if (Hit(mouseX, mouseY, layout.UserRowX, rowY, layout.UserRowWidth, layout.UserRowHeight))
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

                if (Hit(mouseX, mouseY, layout.InputX, layout.UserInputY, layout.InputWidth, layout.InputHeight))
                {
                    passwordField = false;
                    ClearPassword();
                    return;
                }

                if (Hit(mouseX, mouseY, layout.InputX, layout.PassInputY, layout.InputWidth, layout.InputHeight))
                {
                    passwordField = true;
                    return;
                }

                if (Hit(mouseX, mouseY, layout.InputX, layout.LoginBtnY, layout.InputWidth, layout.LoginBtnHeight))
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

                if (Hit(mouseX, mouseY, layout.RebootX, layout.PowerBtnY, layout.RebootWidth, layout.PowerBtnHeight))
                {
                    ClearSensitiveData();
                    global::ZonderqOS.SystemCore.SystemPower.Reboot("gui-reboot");
                    return;
                }

                if (Hit(mouseX, mouseY, layout.ShutdownX, layout.PowerBtnY, layout.ShutdownWidth, layout.PowerBtnHeight))
                {
                    ClearSensitiveData();
                    global::ZonderqOS.SystemCore.SystemPower.Shutdown("gui-shutdown");
                }
            }

            public void Render(Canvas canvas)
            {
                ScreenLayout layout = GetLayout();

                canvas.Clear(Background);
                canvas.DrawFilledRectangle(TopBand, 0, 0, screenWidth, layout.TopBandHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, 0, layout.TopBandHeight - 2, screenWidth, 2);

                IconManager.DrawScaled(canvas, IconType.Start,
                    layout.TopIconX, layout.TopIconY, layout.TopIconSize, layout.TopIconSize);
                canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, layout.TopTitleX, layout.TopTitleY);
                SmallTextRenderer.Draw(canvas, "SECURE SESSION  /  COSMOS GEN3",
                    layout.TopSubX, layout.TopSubY, Muted);

                UiPainter.Shadow(canvas, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight,
                    UiMetrics.MenuRadius);
                UiPainter.RoundedPanel(canvas, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight,
                    UiMetrics.MenuRadius, Card, Border);
                canvas.DrawFilledRectangle(SystemTheme.Accent, layout.CardX + 16, layout.CardY + 2,
                    layout.CardWidth - 32, 2);

                SmallTextRenderer.Draw(canvas, "KONTA", layout.AccountsLabelX, layout.AccountsLabelY, Muted);
                if (userCount > VisibleUsers)
                {
                    SmallTextRenderer.DrawUInt(canvas, (ulong)(selectedUser + 1),
                        layout.AccountsCounterX, layout.AccountsLabelY, Text);
                    SmallTextRenderer.Draw(canvas, "/", layout.AccountsCounterX + 20, layout.AccountsLabelY, Muted);
                    SmallTextRenderer.DrawUInt(canvas, (ulong)userCount,
                        layout.AccountsCounterX + 32, layout.AccountsLabelY, Text);
                }

                canvas.DrawString("Logowanie", PCScreenFont.DefaultFont, Text, layout.InputX, layout.TitleY);
                SmallTextRenderer.DrawClipped(canvas, "AUTORYZACJA WYMAGANA PRZED URUCHOMIENIEM PULPITU",
                    layout.InputX + 2, layout.SubtitleY, layout.InputWidth, Muted);

                RenderUsers(canvas, layout);
                canvas.DrawLine(Border, layout.RightX, layout.CardY + 18,
                    layout.RightX, layout.CardY + layout.CardHeight - 18);

                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", layout.InputX, layout.UserLabelY, Muted);
                RenderInput(canvas, layout.InputX, layout.UserInputY, layout.InputWidth, layout.InputHeight, false);

                SmallTextRenderer.Draw(canvas, "HASLO", layout.InputX, layout.PassLabelY, Muted);
                RenderInput(canvas, layout.InputX, layout.PassInputY, layout.InputWidth, layout.InputHeight, true);

                Color loginColor = passwordField && !string.IsNullOrEmpty(username) && passwordLength > 0
                    ? SystemTheme.Accent
                    : Color.FromArgb(54, 67, 79);
                UiPainter.RoundedPanel(canvas, layout.InputX, layout.LoginBtnY,
                    layout.InputWidth, layout.LoginBtnHeight,
                    UiMetrics.ButtonRadius, loginColor, SystemTheme.AccentBorder);
                int btnIconSize = layout.LoginBtnHeight >= 44 ? 22 : 20;
                IconManager.DrawScaled(canvas, IconType.Play,
                    layout.InputX + 16, layout.LoginBtnY + (layout.LoginBtnHeight - btnIconSize) / 2,
                    btnIconSize, btnIconSize);
                SmallTextRenderer.DrawCentered(canvas, passwordField ? "ZALOGUJ" : "DALEJ",
                    layout.InputX + 50, layout.LoginBtnY + (layout.LoginBtnHeight - 7) / 2,
                    layout.InputWidth - 70, Text);

                UiPainter.RoundedPanel(canvas, layout.InputX, layout.StatusY,
                    layout.InputWidth, layout.StatusHeight,
                    UiMetrics.ButtonRadius, CardAlt, Border);
                int statusPadY = layout.StatusHeight >= 66 ? 18 : 15;
                int helpOffsetY = layout.StatusHeight >= 66 ? 42 : 38;
                canvas.DrawFilledRectangle(statusColor, layout.InputX + 15, layout.StatusY + statusPadY + 1, 6, 6);
                SmallTextRenderer.DrawClipped(canvas, status,
                    layout.InputX + 33, layout.StatusY + statusPadY, layout.InputWidth - 50, statusColor);
                SmallTextRenderer.DrawClipped(canvas, "F5 KONTA  |  GORA/DOL WYBOR  |  ESC WSTECZ",
                    layout.InputX + 15, layout.StatusY + helpOffsetY, layout.InputWidth - 26, Muted);

                SmallTextRenderer.DrawClipped(canvas,
                    "ZOnderqOS chroni pulpit przed dostepem bez uwierzytelnienia.",
                    layout.HintX, layout.HintY, layout.HintMaxWidth, Muted);
                RenderPowerButton(canvas, layout.RebootX, layout.PowerBtnY,
                    layout.RebootWidth, layout.PowerBtnHeight, "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, layout.ShutdownX, layout.PowerBtnY,
                    layout.ShutdownWidth, layout.PowerBtnHeight, "WYLACZ", IconType.Shutdown, true);
            }

            private void RenderUsers(Canvas canvas, ScreenLayout layout)
            {
                if (userCount <= 0)
                {
                    SmallTextRenderer.Draw(canvas, "BRAK KONT", layout.UserRowX, layout.UserRowY + 6, Danger);
                    SmallTextRenderer.Draw(canvas, "WPISZ LOGIN RECZNIE",
                        layout.UserRowX, layout.UserRowY + 27, Muted);
                    return;
                }

                int iconSize = layout.UserRowHeight >= 40 ? 22 : 20;
                int textOffsetY = (layout.UserRowHeight - 7) / 2;
                int iconOffsetY = (layout.UserRowHeight - iconSize) / 2;
                int labelMaxWidth = System.Math.Max(40, layout.UserRowWidth - 54);

                for (int row = 0; row < VisibleUsers; row++)
                {
                    int index = userOffset + row;
                    if (index >= userCount)
                        break;

                    int rowY = layout.UserRowY + row * layout.UserRowStep;
                    bool selected = index == selectedUser;
                    UiPainter.RoundedPanel(canvas, layout.UserRowX, rowY,
                        layout.UserRowWidth, layout.UserRowHeight,
                        UiMetrics.ButtonRadius, selected ? SystemTheme.AccentSoft : CardAlt,
                        selected ? SystemTheme.AccentBorder : Border);
                    IconManager.DrawScaled(canvas, IconType.Start,
                        layout.UserRowX + 11, rowY + iconOffsetY, iconSize, iconSize);
                    SmallTextRenderer.DrawClipped(canvas, users[index],
                        layout.UserRowX + 44, rowY + textOffsetY, labelMaxWidth,
                        selected ? Text : Muted);
                }
            }

            private void RenderInput(Canvas canvas, int x, int y, int width, int height, bool passwordInput)
            {
                bool active = passwordField == passwordInput;
                UiPainter.RoundedPanel(canvas, x, y, width, height, UiMetrics.ButtonRadius,
                    active ? Color.FromArgb(31, 42, 51) : CardAlt,
                    active ? SystemTheme.AccentBorder : Border);
                if (active)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x + 12, y + 2, width - 24, 2);

                int textY = y + (height - 7) / 2;
                if (!passwordInput)
                {
                    if (string.IsNullOrEmpty(username))
                        SmallTextRenderer.Draw(canvas, "nazwa uzytkownika", x + 15, textY, Muted);
                    else
                        SmallTextRenderer.DrawClipped(canvas, username, x + 15, textY, width - 30, Text);
                }
                else if (passwordLength == 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", x + 15, textY, Muted);
                }
                else
                {
                    int maxVisible = System.Math.Max(1, (width - 30) / 6);
                    int visible = System.Math.Min(passwordLength, System.Math.Min(maxVisible, 48));
                    SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, x + 15, textY, Text);
                }
            }

            private static void RenderPowerButton(Canvas canvas, int x, int y, int width, int height,
                string label, IconType icon, bool danger)
            {
                Color bg = danger ? Color.FromArgb(57, 38, 43) : Color.FromArgb(27, 35, 43);
                Color border = danger ? Color.FromArgb(112, 61, 69) : Border;
                UiPainter.RoundedPanel(canvas, x, y, width, height,
                    UiMetrics.ButtonRadius, bg, border);
                int iconSize = height >= 40 ? 20 : 18;
                int iconY = y + (height - iconSize) / 2;
                int textY = y + (height - 7) / 2;
                IconManager.DrawScaled(canvas, icon, x + 9, iconY, iconSize, iconSize);
                SmallTextRenderer.DrawClipped(canvas, label, x + 36, textY, width - 42, Text);
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

                bool credentialsValid = false;
                try
                {
                    credentialsValid = UserManager.ValidateCredentialsChars(username, password, passwordLength);
                }
                catch
                {
                    credentialsValid = false;
                }
                finally
                {
                    ClearPassword();
                }

                bool ok = false;
                if (credentialsValid)
                {
                    ok = UserManager.ActivateSession(username);
                    if (!ok)
                        SecurityLogger.LogEvent("ERR", "Authenticated user '" + username + "' is missing from passwd database.");
                }
                else
                {
                    SecurityLogger.LogEvent("WARN", "Failed login attempt for '" + (username ?? "?") + "'.");
                }

                if (ok)
                {
                    AuthenticationGuard.RecordSuccess(username);
                    SecurityLogger.LogEvent("INFO", "User '" + username + "' logged in.");
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
                Array.Clear(password, 0, password.Length);
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
