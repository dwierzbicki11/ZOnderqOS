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
    /// One-time security setup shown while the factory root/root credential is still
    /// active. No desktop or shell session can start until that credential is replaced.
    /// Password input lives in fixed buffers and is cleared immediately after submission.
    /// </summary>
    public static class InitialSetupManager
    {
        public static bool Run()
        {
            if (!UserManager.RequiresInitialRootPasswordSetup())
                return true;

            SetupScreen screen = null;
            try
            {
                Canvas canvas = DisplayModeSelector.OpenNativeOrBest();
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                IconManager.Preload();

                screen = new SetupScreen((int)canvas.Width, (int)canvas.Height);
                bool previousLeft = MouseManager.LeftButton;
                int previousMouseX = (int)MouseManager.X;
                int previousMouseY = (int)MouseManager.Y;
                int frame = 0;

                screen.Render(canvas);
                Cursor.Draw(canvas, previousMouseX, previousMouseY);
                canvas.Display();

                while (!screen.Completed)
                {
                    frame++;
                    bool keyboardActivity = false;

                    while (KeyboardManager.TryReadKey(out KeyEvent? key))
                    {
                        if (key == null)
                            continue;

                        keyboardActivity = true;
                        screen.HandleKeyboard(key);
                        if (screen.Completed)
                            break;
                    }

                    int mouseX = (int)MouseManager.X;
                    int mouseY = (int)MouseManager.Y;
                    bool currentLeft = MouseManager.LeftButton;
                    bool pointerMoved = mouseX != previousMouseX || mouseY != previousMouseY;
                    bool buttonChanged = currentLeft != previousLeft;

                    if (!screen.Completed)
                        screen.HandleMouse(mouseX, mouseY, currentLeft, previousLeft);

                    previousLeft = currentLeft;
                    previousMouseX = mouseX;
                    previousMouseY = mouseY;

                    bool heartbeat = frame % 134 == 0;
                    if (!screen.Completed && (keyboardActivity || pointerMoved || buttonChanged || heartbeat))
                    {
                        screen.Render(canvas);
                        Cursor.Draw(canvas, mouseX, mouseY);
                        canvas.Display();
                    }

                    Thread.Sleep(15);
                }

                screen.ClearSensitiveData();
                return !UserManager.RequiresInitialRootPasswordSetup();
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("Initial graphical security setup failed: " + ex.Message, "AUTH");
                return false;
            }
            finally
            {
                screen?.ClearSensitiveData();
            }
        }

        private sealed class SetupScreen
        {
            private const int MaxPasswordLength = PasswordPolicy.MaxLength;
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

            private readonly int screenWidth;
            private readonly int screenHeight;
            private readonly char[] password = new char[MaxPasswordLength];
            private readonly char[] confirmation = new char[MaxPasswordLength];

            private int passwordLength;
            private int confirmationLength;
            private int activeField;
            private string status = "USTAW NOWE HASLO DLA KONTA ROOT";
            private Color statusColor = Warning;

            public bool Completed { get; private set; }

            public SetupScreen(int width, int height)
            {
                screenWidth = width;
                screenHeight = height;
            }

            public void ClearSensitiveData()
            {
                ClearBuffer(password, ref passwordLength);
                ClearBuffer(confirmation, ref confirmationLength);
            }

            public void HandleKeyboard(KeyEvent key)
            {
                if (key == null || Completed)
                    return;

                if (key.Key == ConsoleKeyEx.UpArrow)
                {
                    activeField = 0;
                    return;
                }

                if (key.Key == ConsoleKeyEx.DownArrow)
                {
                    activeField = 1;
                    return;
                }

                if (key.Key == ConsoleKeyEx.Escape || key.Key == ConsoleKeyEx.Delete)
                {
                    ClearActiveField();
                    SetStatus("POLE WYCZYSZCZONE", Muted);
                    return;
                }

                if (key.Key == ConsoleKeyEx.Backspace)
                {
                    RemoveLastCharacter();
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    if (activeField == 0)
                    {
                        activeField = 1;
                        SetStatus("POWTORZ NOWE HASLO", Muted);
                    }
                    else
                    {
                        Submit();
                    }
                    return;
                }

                char ch = key.KeyChar;
                if (ch < 32 || ch > 126)
                    return;

                if (activeField == 0)
                {
                    if (passwordLength < MaxPasswordLength)
                        password[passwordLength++] = ch;
                }
                else if (confirmationLength < MaxPasswordLength)
                {
                    confirmation[confirmationLength++] = ch;
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

                public int InnerX;
                public int InnerWidth;
                public int AccountBoxY;
                public int AccountBoxHeight;
                public int AccountLabelX;
                public int AccountUserX;
                public int AccountPolicyX;

                public int PassLabelY;
                public int PassInputY;
                public int ConfirmLabelY;
                public int ConfirmInputY;
                public int InputHeight;
                public int SubmitBtnY;
                public int SubmitBtnHeight;
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
                    l.HintX = 20;
                    l.HintY = l.PowerBtnY + (l.PowerBtnHeight - 7) / 2;
                    l.HintMaxWidth = System.Math.Max(60, l.RebootX - l.HintX - 12);

                    l.CardWidth = System.Math.Min(612, System.Math.Max(360, screenWidth - 28));
                    l.CardHeight = 348;
                    int availTop = l.TopBandHeight;
                    int availHeight = System.Math.Max(l.CardHeight, l.PowerBtnY - availTop);
                    l.CardX = (screenWidth - l.CardWidth) / 2;
                    l.CardY = availTop + (availHeight - l.CardHeight) / 2;

                    l.HeaderIconX = l.CardX + 28;
                    l.HeaderIconY = l.CardY + 14;
                    l.HeaderIconSize = 38;
                    l.HeaderTitleX = l.CardX + 78;
                    l.HeaderTitleY = l.CardY + 12;
                    l.HeaderSubX = l.CardX + 80;
                    l.HeaderSubY = l.CardY + 38;

                    l.InnerX = l.CardX + 28;
                    l.InnerWidth = l.CardWidth - 56;
                    l.AccountBoxY = l.CardY + 60;
                    l.AccountBoxHeight = 34;
                    l.AccountLabelX = l.InnerX + 14;
                    l.AccountUserX = l.InnerX + 76;
                    l.AccountPolicyX = l.InnerX + 148;

                    l.PassLabelY = l.CardY + 104;
                    l.PassInputY = l.CardY + 116;
                    l.ConfirmLabelY = l.CardY + 164;
                    l.ConfirmInputY = l.CardY + 176;
                    l.InputHeight = 38;
                    l.SubmitBtnY = l.CardY + 228;
                    l.SubmitBtnHeight = 42;
                    l.StatusY = l.CardY + 282;
                    l.StatusHeight = 40;
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

                l.CardWidth = 650;
                l.CardX = (screenWidth - l.CardWidth) / 2;
                l.InnerX = l.CardX + 48;
                l.InnerWidth = 554;
                l.AccountLabelX = l.InnerX + 16;
                l.AccountUserX = l.InnerX + 102;
                l.AccountPolicyX = l.InnerX + 212;

                if (mediumHeight)
                {
                    l.CardHeight = 416;
                    int availTop = l.TopBandHeight;
                    int availHeight = System.Math.Max(l.CardHeight, l.PowerBtnY - availTop);
                    l.CardY = availTop + (availHeight - l.CardHeight) / 2;

                    l.HeaderIconX = l.CardX + 36;
                    l.HeaderIconY = l.CardY + 22;
                    l.HeaderIconSize = 44;
                    l.HeaderTitleX = l.CardX + 98;
                    l.HeaderTitleY = l.CardY + 20;
                    l.HeaderSubX = l.CardX + 100;
                    l.HeaderSubY = l.CardY + 56;

                    l.AccountBoxY = l.CardY + 90;
                    l.AccountBoxHeight = 42;
                    l.PassLabelY = l.CardY + 142;
                    l.PassInputY = l.CardY + 156;
                    l.ConfirmLabelY = l.CardY + 210;
                    l.ConfirmInputY = l.CardY + 224;
                    l.InputHeight = 44;
                    l.SubmitBtnY = l.CardY + 284;
                    l.SubmitBtnHeight = 48;
                    l.StatusY = l.CardY + 346;
                    l.StatusHeight = 44;
                    return l;
                }

                l.CardHeight = 480;
                l.CardY = (screenHeight - l.CardHeight) / 2;
                l.HeaderIconX = l.CardX + 36;
                l.HeaderIconY = l.CardY + 34;
                l.HeaderIconSize = 48;
                l.HeaderTitleX = l.CardX + 104;
                l.HeaderTitleY = l.CardY + 31;
                l.HeaderSubX = l.CardX + 106;
                l.HeaderSubY = l.CardY + 73;

                l.AccountBoxY = l.CardY + 112;
                l.AccountBoxHeight = 48;
                l.PassLabelY = l.CardY + 169;
                l.PassInputY = l.CardY + 184;
                l.ConfirmLabelY = l.CardY + 249;
                l.ConfirmInputY = l.CardY + 264;
                l.InputHeight = 48;
                l.SubmitBtnY = l.CardY + 338;
                l.SubmitBtnHeight = 52;
                l.StatusY = l.CardY + 405;
                l.StatusHeight = 44;
                return l;
            }

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (Completed || !leftClicked || leftWasClicked)
                    return;

                ScreenLayout layout = GetLayout();

                if (Hit(mouseX, mouseY, layout.InnerX, layout.PassInputY, layout.InnerWidth, layout.InputHeight))
                {
                    activeField = 0;
                    return;
                }

                if (Hit(mouseX, mouseY, layout.InnerX, layout.ConfirmInputY, layout.InnerWidth, layout.InputHeight))
                {
                    activeField = 1;
                    return;
                }

                if (Hit(mouseX, mouseY, layout.InnerX, layout.SubmitBtnY, layout.InnerWidth, layout.SubmitBtnHeight))
                {
                    Submit();
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
                SmallTextRenderer.Draw(canvas, "INITIAL SECURITY SETUP  /  COSMOS GEN3",
                    layout.TopSubX, layout.TopSubY, Muted);

                UiPainter.Shadow(canvas, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight,
                    UiMetrics.MenuRadius);
                UiPainter.RoundedPanel(canvas, layout.CardX, layout.CardY, layout.CardWidth, layout.CardHeight,
                    UiMetrics.MenuRadius, Card, Border);
                canvas.DrawFilledRectangle(SystemTheme.Accent, layout.CardX + 16, layout.CardY + 2,
                    layout.CardWidth - 32, 2);

                IconManager.DrawScaled(canvas, IconType.Settings,
                    layout.HeaderIconX, layout.HeaderIconY, layout.HeaderIconSize, layout.HeaderIconSize);
                canvas.DrawString("Pierwsza konfiguracja", PCScreenFont.DefaultFont, Text,
                    layout.HeaderTitleX, layout.HeaderTitleY);
                SmallTextRenderer.DrawClipped(canvas,
                    "FABRYCZNE HASLO ROOT MUSI ZOSTAC ZMIENIONE PRZED URUCHOMIENIEM PULPITU",
                    layout.HeaderSubX, layout.HeaderSubY,
                    layout.CardX + layout.CardWidth - 20 - layout.HeaderSubX, Warning);

                UiPainter.RoundedPanel(canvas, layout.InnerX, layout.AccountBoxY,
                    layout.InnerWidth, layout.AccountBoxHeight,
                    UiMetrics.ButtonRadius, CardAlt, Border);
                int accountTextY = layout.AccountBoxY + (layout.AccountBoxHeight - 7) / 2;
                SmallTextRenderer.Draw(canvas, "KONTO", layout.AccountLabelX, accountTextY, Muted);
                SmallTextRenderer.Draw(canvas, "root", layout.AccountUserX, accountTextY, Text);
                SmallTextRenderer.DrawClipped(canvas, PasswordPolicy.Summary,
                    layout.AccountPolicyX, accountTextY,
                    layout.InnerX + layout.InnerWidth - 12 - layout.AccountPolicyX, Muted);

                SmallTextRenderer.Draw(canvas, "NOWE HASLO", layout.InnerX, layout.PassLabelY, Muted);
                RenderPasswordField(canvas, layout.InnerX, layout.PassInputY,
                    layout.InnerWidth, layout.InputHeight, 0, password, passwordLength);

                SmallTextRenderer.Draw(canvas, "POWTORZ HASLO", layout.InnerX, layout.ConfirmLabelY, Muted);
                RenderPasswordField(canvas, layout.InnerX, layout.ConfirmInputY,
                    layout.InnerWidth, layout.InputHeight, 1, confirmation, confirmationLength);

                bool ready = passwordLength >= PasswordPolicy.MinLength && confirmationLength > 0;
                Color applyColor = ready ? SystemTheme.Accent : Color.FromArgb(54, 67, 79);
                UiPainter.RoundedPanel(canvas, layout.InnerX, layout.SubmitBtnY,
                    layout.InnerWidth, layout.SubmitBtnHeight,
                    UiMetrics.ButtonRadius, applyColor, SystemTheme.AccentBorder);
                int submitIconSize = layout.SubmitBtnHeight >= 48 ? 22 : 20;
                IconManager.DrawScaled(canvas, IconType.Save,
                    layout.InnerX + 18, layout.SubmitBtnY + (layout.SubmitBtnHeight - submitIconSize) / 2,
                    submitIconSize, submitIconSize);
                SmallTextRenderer.DrawCentered(canvas, "ZAPISZ HASLO I PRZEJDZ DO LOGOWANIA",
                    layout.InnerX + 56, layout.SubmitBtnY + (layout.SubmitBtnHeight - 7) / 2,
                    layout.InnerWidth - 86, Text);

                UiPainter.RoundedPanel(canvas, layout.InnerX, layout.StatusY,
                    layout.InnerWidth, layout.StatusHeight,
                    UiMetrics.ButtonRadius, CardAlt, Border);
                int statusTextY = layout.StatusY + (layout.StatusHeight - 7) / 2;
                canvas.DrawFilledRectangle(statusColor, layout.InnerX + 15, statusTextY + 1, 6, 6);
                SmallTextRenderer.DrawClipped(canvas, status,
                    layout.InnerX + 33, statusTextY, layout.InnerWidth - 54, statusColor);

                string hintText = layout.Compact
                    ? "GORA/DOL ZMIENIA POLE | ENTER DALEJ/ZAPISZ | ESC CZYSCI POLE"
                    : "GORA/DOL ZMIENIA POLE  |  ENTER DALEJ/ZAPISZ  |  ESC CZYSCI POLE";
                SmallTextRenderer.DrawClipped(canvas, hintText,
                    layout.HintX, layout.HintY, layout.HintMaxWidth, Muted);
                RenderPowerButton(canvas, layout.RebootX, layout.PowerBtnY,
                    layout.RebootWidth, layout.PowerBtnHeight, "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, layout.ShutdownX, layout.PowerBtnY,
                    layout.ShutdownWidth, layout.PowerBtnHeight, "WYLACZ", IconType.Shutdown, true);
            }

            private void RenderPasswordField(Canvas canvas, int x, int y, int width, int height,
                int fieldIndex, char[] buffer, int length)
            {
                bool active = activeField == fieldIndex;
                UiPainter.RoundedPanel(canvas, x, y, width, height, UiMetrics.ButtonRadius,
                    active ? Color.FromArgb(31, 42, 51) : CardAlt,
                    active ? SystemTheme.AccentBorder : Border);
                if (active)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x + 12, y + 2, width - 24, 2);

                int textY = y + (height - 7) / 2;
                if (length <= 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", x + 15, textY, Muted);
                    return;
                }

                int maxVisible = System.Math.Max(1, (width - 30) / 6);
                int visible = System.Math.Min(length, System.Math.Min(maxVisible, PasswordMask.Length));
                SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, x + 15, textY, Text);
            }

            private void Submit()
            {
                if (passwordLength <= 0 || confirmationLength <= 0)
                {
                    SetStatus("UZUPELNIJ OBA POLA HASLA", Danger);
                    return;
                }

                if (passwordLength != confirmationLength)
                {
                    ClearBuffer(confirmation, ref confirmationLength);
                    activeField = 1;
                    SetStatus("HASLA NIE SA IDENTYCZNE", Danger);
                    return;
                }

                int mismatch = 0;
                for (int i = 0; i < passwordLength; i++)
                    mismatch |= password[i] ^ confirmation[i];

                if (mismatch != 0)
                {
                    ClearBuffer(confirmation, ref confirmationLength);
                    activeField = 1;
                    SetStatus("HASLA NIE SA IDENTYCZNE", Danger);
                    return;
                }

                string reason;
                if (!ValidatePasswordChars(password, passwordLength, out reason))
                {
                    ClearSensitiveData();
                    activeField = 0;
                    SetStatus(reason, Danger);
                    return;
                }

                bool ok = false;
                string newPassword = null;
                try
                {
                    newPassword = new string(password, 0, passwordLength);
                    ok = UserManager.CompleteInitialRootPasswordSetup(newPassword);
                }
                finally
                {
                    newPassword = null;
                    ClearSensitiveData();
                }

                if (!ok)
                {
                    activeField = 0;
                    SetStatus("NIE UDALO SIE ZAPISAC NOWEGO HASLA ROOT", Danger);
                    return;
                }

                SetStatus("HASLO ROOT ZMIENIONE - URUCHAMIAM LOGOWANIE", Good);
                Completed = true;
            }

            private static bool ValidatePasswordChars(char[] buffer, int length, out string reason)
            {
                if (buffer == null || length <= 0)
                {
                    reason = "HASLO JEST WYMAGANE";
                    return false;
                }

                if (length < PasswordPolicy.MinLength)
                {
                    reason = "HASLO MUSI MIEC MINIMUM 8 ZNAKOW";
                    return false;
                }

                if (length > PasswordPolicy.MaxLength || length > buffer.Length)
                {
                    reason = "HASLO JEST ZA DLUGIE";
                    return false;
                }

                bool hasLetter = false;
                bool hasDigit = false;

                for (int i = 0; i < length; i++)
                {
                    char ch = buffer[i];
                    if (ch < 32 || ch > 126)
                    {
                        reason = "HASLO MOZE UZYWAC TYLKO DRUKOWALNYCH ZNAKOW ASCII";
                        return false;
                    }

                    if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                        hasLetter = true;
                    else if (ch >= '0' && ch <= '9')
                        hasDigit = true;
                }

                if (!hasLetter || !hasDigit)
                {
                    reason = "HASLO MUSI ZAWIERAC LITERE I CYFRE";
                    return false;
                }

                reason = string.Empty;
                return true;
            }

            private void RemoveLastCharacter()
            {
                if (activeField == 0)
                {
                    if (passwordLength > 0)
                    {
                        passwordLength--;
                        password[passwordLength] = '\0';
                    }
                }
                else if (confirmationLength > 0)
                {
                    confirmationLength--;
                    confirmation[confirmationLength] = '\0';
                }
            }

            private void ClearActiveField()
            {
                if (activeField == 0)
                    ClearBuffer(password, ref passwordLength);
                else
                    ClearBuffer(confirmation, ref confirmationLength);
            }

            private static void ClearBuffer(char[] buffer, ref int length)
            {
                if (buffer != null)
                    Array.Clear(buffer, 0, buffer.Length);
                length = 0;
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
                UiPainter.RoundedPanel(canvas, x, y, width, height,
                    UiMetrics.ButtonRadius, bg, border);
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
