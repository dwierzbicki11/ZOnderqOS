using System;
using System.Drawing;
using System.Threading;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Input;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Full-screen session lock. Open applications stay alive only while the same user
    /// unlocks the session. Choosing another user ends the session first, preventing the
    /// next account from inheriting windows or data from the previous desktop.
    /// </summary>
    public static class LockScreenManager
    {
        private static CursorDamageRenderer cursorDamageRenderer;
        private static Canvas cursorDamageCanvas;
        private static readonly LockScreen ReusableScreen = new LockScreen();

        internal static void ClearSessionResources()
        {
            cursorDamageRenderer = null;
            cursorDamageCanvas = null;
        }

        public static bool Run(Canvas canvas)
        {
            if (canvas == null || !SecurityContext.IsAuthenticated ||
                string.IsNullOrEmpty(SecurityContext.CurrentUser))
                return false;

            try
            {
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                ReusableScreen.Reset((int)canvas.Width, (int)canvas.Height,
                    SecurityContext.CurrentUser);
                LockScreen screen = ReusableScreen;

                if (cursorDamageRenderer == null || !ReferenceEquals(cursorDamageCanvas, canvas))
                {
                    cursorDamageCanvas = canvas;
                    cursorDamageRenderer = new CursorDamageRenderer(canvas);
                }
                else
                {
                    cursorDamageRenderer.Reset();
                }

                bool previousLeft = MouseManager.LeftButton;
                int previousMouseX = (int)MouseManager.X;
                int previousMouseY = (int)MouseManager.Y;

                screen.Render(canvas);
                cursorDamageRenderer.DrawAfterScene(previousMouseX, previousMouseY);
                canvas.Display();

                while (!screen.Unlocked && SecurityContext.IsAuthenticated)
                {
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

#if !ZONDERQ_ARCH_ARM64
                    global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.SampleIfDue(
                        System.Diagnostics.Stopwatch.GetTimestamp(), System.Diagnostics.Stopwatch.Frequency);
#endif

                    if (!screen.Unlocked && SecurityContext.IsAuthenticated)
                    {
                        bool cursorOnly = pointerMoved && !keyboardActivity && !buttonChanged && !currentLeft;
                        if (cursorOnly && cursorDamageRenderer != null &&
                            cursorDamageRenderer.TryMove(mouseX, mouseY))
                        {
                            // Only the two 24x28 cursor damage rectangles were repainted.
                        }
                        else if (keyboardActivity || pointerMoved || buttonChanged)
                        {
                            cursorDamageRenderer?.Reset();
                            screen.Render(canvas);
                            if (cursorDamageRenderer != null)
                                cursorDamageRenderer.DrawAfterScene(mouseX, mouseY);
                            else
                                Cursor.Draw(canvas, mouseX, mouseY);
                            canvas.Display();
                        }
                    }

                    if (keyboardActivity || pointerMoved || buttonChanged)
                        Thread.Sleep(15);
                    else
                        Thread.Sleep(25);
                }

                screen.ClearSensitiveData();
                return screen.Unlocked && SecurityContext.IsAuthenticated;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Lock screen failed: " + ex.Message, "AUTH");
                return false;
            }
            finally
            {
                ReusableScreen.ClearSensitiveData();
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
            private static readonly Color Warning = Color.FromArgb(224, 174, 76);
            private static readonly Color Danger = Color.FromArgb(215, 86, 91);

            private int screenWidth;
            private int screenHeight;
            private string username = string.Empty;
            private readonly char[] password = new char[MaxPasswordLength];

            private int passwordLength;
            private string status = "SESJA ZABLOKOWANA";
            private Color statusColor = Muted;
            private string cachedUserForLogs = string.Empty;
            private string cachedSwitchLog = "Switch-user requested from lock screen.";
            private string cachedFailedUnlockLog = "Failed graphical unlock attempt.";
            private int cachedWaitSeconds = -1;
            private string cachedWaitStatus = string.Empty;
            private int cachedLockSeconds = -1;
            private string cachedLockStatus = string.Empty;

            public bool Unlocked { get; private set; }

            public LockScreen()
            {
            }

            public void Reset(int width, int height, string currentUser)
            {
                screenWidth = width;
                screenHeight = height;
                username = currentUser ?? string.Empty;
                ClearPassword();
                Unlocked = false;
                status = "SESJA ZABLOKOWANA";
                statusColor = Muted;
            }

            public void ClearSensitiveData()
            {
                ClearPassword();
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
                public int HeaderIconX;
                public int HeaderIconY;
                public int HeaderIconSize;
                public int HeaderTitleX;
                public int HeaderTitleY;
                public int HeaderSubX;
                public int HeaderSubY;

                public int FieldX;
                public int FieldWidth;
                public int UserLabelY;
                public int UserBoxY;
                public int UserBoxHeight;
                public int PassLabelY;
                public int PassBoxY;
                public int PassBoxHeight;
                public int UnlockBtnY;
                public int UnlockBtnHeight;
                public int SwitchBtnY;
                public int SwitchBtnHeight;
                public int StatusBoxY;
                public int StatusBoxHeight;

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
                bool compact = screenHeight < 560 || screenWidth < 560;
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
                    l.HintX = 20;
                    l.HintY = l.PowerBtnY + (l.PowerBtnHeight - 7) / 2;
                    l.HintMaxWidth = System.Math.Max(60, l.RebootX - l.HintX - 10);

                    l.CardWidth = System.Math.Min(520, System.Math.Max(300, screenWidth - 24));
                    l.CardHeight = 336;
                    int availTop = l.TopBandHeight;
                    int availHeight = System.Math.Max(l.CardHeight, l.PowerBtnY - availTop);
                    l.CardX = (screenWidth - l.CardWidth) / 2;
                    l.CardY = availTop + (availHeight - l.CardHeight) / 2;

                    l.HeaderIconX = l.CardX + 30;
                    l.HeaderIconY = l.CardY + 18;
                    l.HeaderIconSize = 40;
                    l.HeaderTitleX = l.CardX + 84;
                    l.HeaderTitleY = l.CardY + 17;
                    l.HeaderSubX = l.CardX + 86;
                    l.HeaderSubY = l.CardY + 46;

                    l.FieldX = l.CardX + 40;
                    l.FieldWidth = l.CardWidth - 80;
                    l.UserLabelY = l.CardY + 72;
                    l.UserBoxY = l.CardY + 84;
                    l.UserBoxHeight = 36;
                    l.PassLabelY = l.CardY + 130;
                    l.PassBoxY = l.CardY + 142;
                    l.PassBoxHeight = 38;
                    l.UnlockBtnY = l.CardY + 194;
                    l.UnlockBtnHeight = 40;
                    l.SwitchBtnY = l.CardY + 244;
                    l.SwitchBtnHeight = 36;
                    l.StatusBoxY = l.CardY + 290;
                    l.StatusBoxHeight = 24;
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

                l.CardWidth = 520;
                l.CardHeight = 420;
                l.CardX = (screenWidth - l.CardWidth) / 2;
                l.CardY = (screenHeight - l.CardHeight) / 2;

                l.HeaderIconX = l.CardX + 36;
                l.HeaderIconY = l.CardY + 35;
                l.HeaderIconSize = 54;
                l.HeaderTitleX = l.CardX + 112;
                l.HeaderTitleY = l.CardY + 34;
                l.HeaderSubX = l.CardX + 114;
                l.HeaderSubY = l.CardY + 75;

                l.FieldX = l.CardX + 48;
                l.FieldWidth = 424;
                l.UserLabelY = l.CardY + 123;
                l.UserBoxY = l.CardY + 143;
                l.UserBoxHeight = 46;
                l.PassLabelY = l.CardY + 202;
                l.PassBoxY = l.CardY + 222;
                l.PassBoxHeight = 46;
                l.UnlockBtnY = l.CardY + 286;
                l.UnlockBtnHeight = 50;
                l.SwitchBtnY = l.CardY + 344;
                l.SwitchBtnHeight = 42;
                l.StatusBoxY = l.CardY + 393;
                l.StatusBoxHeight = 22;
                return l;
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (Unlocked || !leftClicked || leftWasClicked)
                    return;

                ScreenLayout layout = GetLayout();

                if (Hit(mouseX, mouseY, layout.FieldX, layout.UnlockBtnY, layout.FieldWidth, layout.UnlockBtnHeight))
                {
                    Authenticate();
                    return;
                }

                if (Hit(mouseX, mouseY, layout.FieldX, layout.SwitchBtnY, layout.FieldWidth, layout.SwitchBtnHeight))
                {
                    ClearPassword();
                    EnsureCachedUserLogs();
                    SecurityLogger.LogEvent("INFO", cachedSwitchLog);
                    UserProfileManager.RememberLastUser(username);
                    UserManager.EndSession();
                    return;
                }

                if (Hit(mouseX, mouseY, layout.RebootX, layout.PowerBtnY, layout.RebootWidth, layout.PowerBtnHeight))
                {
                    ClearPassword();
                    global::ZonderqOS.SystemCore.SystemPower.Reboot("gui-reboot");
                    return;
                }

                if (Hit(mouseX, mouseY, layout.ShutdownX, layout.PowerBtnY, layout.ShutdownWidth, layout.PowerBtnHeight))
                {
                    ClearPassword();
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
                SmallTextRenderer.Draw(canvas, "SECURE SESSION  /  LOCKED", layout.TopSubX, layout.TopSubY, Muted);

                canvas.DrawFilledRectangle(Color.FromArgb(5, 9, 14),
                    layout.CardX + 8, layout.CardY + 9, layout.CardWidth, layout.CardHeight);
                canvas.DrawFilledRectangle(Card, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight);
                canvas.DrawRectangle(Border, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, layout.CardX, layout.CardY, 5, layout.CardHeight);

                IconManager.DrawScaled(canvas, IconType.Start,
                    layout.HeaderIconX, layout.HeaderIconY, layout.HeaderIconSize, layout.HeaderIconSize);
                canvas.DrawString("Sesja zablokowana", PCScreenFont.DefaultFont, Text,
                    layout.HeaderTitleX, layout.HeaderTitleY);
                SmallTextRenderer.DrawClipped(canvas, "OTWARTE APLIKACJE POZOSTAJA W PAMIECI",
                    layout.HeaderSubX, layout.HeaderSubY,
                    layout.CardX + layout.CardWidth - 20 - layout.HeaderSubX, Muted);

                SmallTextRenderer.Draw(canvas, "UZYTKOWNIK", layout.FieldX, layout.UserLabelY, Muted);
                canvas.DrawFilledRectangle(CardAlt, layout.FieldX, layout.UserBoxY, layout.FieldWidth, layout.UserBoxHeight);
                canvas.DrawRectangle(Border, layout.FieldX, layout.UserBoxY, layout.FieldWidth, layout.UserBoxHeight);
                int userTextY = layout.UserBoxY + (layout.UserBoxHeight - 7) / 2;
                SmallTextRenderer.DrawClipped(canvas, username,
                    layout.FieldX + 15, userTextY, layout.FieldWidth - 30, Text);

                SmallTextRenderer.Draw(canvas, "HASLO", layout.FieldX, layout.PassLabelY, Muted);
                canvas.DrawFilledRectangle(Color.FromArgb(31, 42, 51),
                    layout.FieldX, layout.PassBoxY, layout.FieldWidth, layout.PassBoxHeight);
                canvas.DrawRectangle(SystemTheme.AccentBorder,
                    layout.FieldX, layout.PassBoxY, layout.FieldWidth, layout.PassBoxHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent,
                    layout.FieldX, layout.PassBoxY, 3, layout.PassBoxHeight);

                int passTextY = layout.PassBoxY + (layout.PassBoxHeight - 7) / 2;
                if (passwordLength == 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", layout.FieldX + 15, passTextY, Muted);
                }
                else
                {
                    int maxVisible = System.Math.Max(1, (layout.FieldWidth - 30) / 6);
                    int visible = System.Math.Min(passwordLength, System.Math.Min(maxVisible, 58));
                    SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible,
                        layout.FieldX + 15, passTextY, Text);
                }

                Color unlockColor = passwordLength > 0 ? SystemTheme.Accent : Color.FromArgb(54, 67, 79);
                canvas.DrawFilledRectangle(unlockColor, layout.FieldX, layout.UnlockBtnY,
                    layout.FieldWidth, layout.UnlockBtnHeight);
                canvas.DrawRectangle(SystemTheme.AccentBorder, layout.FieldX, layout.UnlockBtnY,
                    layout.FieldWidth, layout.UnlockBtnHeight);
                int unlockIconSize = layout.UnlockBtnHeight >= 46 ? 22 : 20;
                IconManager.DrawScaled(canvas, IconType.Play,
                    layout.FieldX + 16, layout.UnlockBtnY + (layout.UnlockBtnHeight - unlockIconSize) / 2,
                    unlockIconSize, unlockIconSize);
                SmallTextRenderer.DrawCentered(canvas, "ODBLOKUJ",
                    layout.FieldX + 50, layout.UnlockBtnY + (layout.UnlockBtnHeight - 7) / 2,
                    layout.FieldWidth - 70, Text);

                canvas.DrawFilledRectangle(CardAlt, layout.FieldX, layout.SwitchBtnY,
                    layout.FieldWidth, layout.SwitchBtnHeight);
                canvas.DrawRectangle(Border, layout.FieldX, layout.SwitchBtnY,
                    layout.FieldWidth, layout.SwitchBtnHeight);
                int switchIconSize = layout.SwitchBtnHeight >= 40 ? 20 : 18;
                IconManager.DrawScaled(canvas, IconType.Start,
                    layout.FieldX + 15, layout.SwitchBtnY + (layout.SwitchBtnHeight - switchIconSize) / 2,
                    switchIconSize, switchIconSize);
                SmallTextRenderer.Draw(canvas, "ZMIEN UZYTKOWNIKA",
                    layout.FieldX + 50, layout.SwitchBtnY + (layout.SwitchBtnHeight - 7) / 2, Warning);

                canvas.DrawFilledRectangle(CardAlt, layout.FieldX, layout.StatusBoxY,
                    layout.FieldWidth, layout.StatusBoxHeight);
                int statusTextY = layout.StatusBoxY + (layout.StatusBoxHeight - 7) / 2;
                canvas.DrawFilledRectangle(statusColor, layout.FieldX + 13, statusTextY, 5, 5);
                SmallTextRenderer.DrawClipped(canvas, status,
                    layout.FieldX + 29, statusTextY, layout.FieldWidth - 44, statusColor);

                SmallTextRenderer.DrawClipped(canvas,
                    "Ctrl+Alt+L blokuje pulpit bez zamykania uruchomionych aplikacji.",
                    layout.HintX, layout.HintY, layout.HintMaxWidth, Muted);
                RenderPowerButton(canvas, layout.RebootX, layout.PowerBtnY,
                    layout.RebootWidth, layout.PowerBtnHeight, "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, layout.ShutdownX, layout.PowerBtnY,
                    layout.ShutdownWidth, layout.PowerBtnHeight, "WYLACZ", IconType.Shutdown, true);
            }

            private void Authenticate()
            {
                if (passwordLength <= 0)
                {
                    SetStatus("HASLO JEST WYMAGANE", Danger);
                    return;
                }

                int retryAfter;
                if (!AuthenticationGuard.CanAttempt(username, out retryAfter))
                {
                    ClearPassword();
                    SetStatus(GetWaitStatus(retryAfter), Danger);
                    return;
                }

                bool ok;
                try
                {
                    ok = UserManager.ValidateCredentialsChars(username, password, passwordLength);
                }
                catch
                {
                    ok = false;
                }
                finally
                {
                    ClearPassword();
                }

                if (ok)
                {
                    AuthenticationGuard.RecordSuccess(username);
                    Unlocked = true;
                    SetStatus("ODBLOKOWANO", Good);
                    return;
                }

                AuthenticationGuard.RecordFailure(username);
                retryAfter = AuthenticationGuard.GetRetryAfterSeconds(username);
                EnsureCachedUserLogs();
                SecurityLogger.LogSessionUnlockFailure(cachedFailedUnlockLog, retryAfter > 0);
                if (retryAfter > 0)
                    SetStatus(GetLockStatus(retryAfter), Danger);
                else
                    SetStatus("NIEPRAWIDLOWE HASLO", Danger);
            }

            private void EnsureCachedUserLogs()
            {
                string user = username ?? string.Empty;
                if (!string.Equals(cachedUserForLogs, user, StringComparison.Ordinal))
                {
                    cachedUserForLogs = user;
                    cachedSwitchLog = "Switch-user requested from lock screen by '" + user + "'.";
                    cachedFailedUnlockLog = "Failed graphical unlock attempt for user '" + user + "'.";
                }
            }

            private string GetWaitStatus(int retryAfter)
            {
                if (retryAfter != cachedWaitSeconds || string.IsNullOrEmpty(cachedWaitStatus))
                {
                    cachedWaitSeconds = retryAfter;
                    cachedWaitStatus = "ZA DUZO PROB - ODCZEKAJ " + retryAfter + " S";
                }
                return cachedWaitStatus;
            }

            private string GetLockStatus(int retryAfter)
            {
                if (retryAfter != cachedLockSeconds || string.IsNullOrEmpty(cachedLockStatus))
                {
                    cachedLockSeconds = retryAfter;
                    cachedLockStatus = "NIEPRAWIDLOWE HASLO - BLOKADA " + retryAfter + " S";
                }
                return cachedLockStatus;
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

            private static void RenderPowerButton(Canvas canvas, int x, int y, int width, int height,
                string label, IconType icon, bool danger)
            {
                Color bg = danger ? Color.FromArgb(57, 38, 43) : Color.FromArgb(27, 35, 43);
                Color border = danger ? Color.FromArgb(112, 61, 69) : Border;
                canvas.DrawFilledRectangle(bg, x, y, width, height);
                canvas.DrawRectangle(border, x, y, width, height);
                int iconSize = height >= 40 ? 20 : 18;
                int iconY = y + (height - iconSize) / 2;
                int textY = y + (height - 7) / 2;
                IconManager.DrawScaled(canvas, icon, x + 9, iconY, iconSize, iconSize);
                SmallTextRenderer.DrawClipped(canvas, label, x + 36, textY, width - 42, Text);
            }

            private static bool Hit(int px, int py, int x, int y, int width, int height)
            {
                return px >= x && px < x + width && py >= y && py < y + height;
            }
        }
    }
}
