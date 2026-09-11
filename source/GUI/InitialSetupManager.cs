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

            try
            {
                Canvas canvas = Canvas.GetFullScreen();
                MouseManager.SetScreenSize(canvas.Width, canvas.Height);
                IconManager.Preload();

                SetupScreen screen = new SetupScreen((int)canvas.Width, (int)canvas.Height);
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

            public void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked)
            {
                if (Completed || !leftClicked || leftWasClicked)
                    return;

                int cardX = (screenWidth - 650) / 2;
                int cardY = (screenHeight - 480) / 2;

                if (Hit(mouseX, mouseY, cardX + 48, cardY + 184, 554, 48))
                {
                    activeField = 0;
                    return;
                }

                if (Hit(mouseX, mouseY, cardX + 48, cardY + 264, 554, 48))
                {
                    activeField = 1;
                    return;
                }

                if (Hit(mouseX, mouseY, cardX + 48, cardY + 338, 554, 52))
                {
                    Submit();
                    return;
                }

                if (Hit(mouseX, mouseY, screenWidth - 226, screenHeight - 66, 92, 42))
                {
                    ClearSensitiveData();
                    Cosmos.Kernel.System.Power.Reboot();
                    return;
                }

                if (Hit(mouseX, mouseY, screenWidth - 124, screenHeight - 66, 100, 42))
                {
                    ClearSensitiveData();
                    Cosmos.Kernel.System.Power.Shutdown();
                }
            }

            public void Render(Canvas canvas)
            {
                canvas.Clear(Background);
                canvas.DrawFilledRectangle(TopBand, 0, 0, screenWidth, 86);
                canvas.DrawFilledRectangle(SystemTheme.Accent, 0, 84, screenWidth, 2);

                IconManager.DrawScaled(canvas, IconType.Start, 28, 22, 40, 40);
                canvas.DrawString("ZOnderqOS", PCScreenFont.DefaultFont, Text, 84, 18);
                SmallTextRenderer.Draw(canvas, "INITIAL SECURITY SETUP  /  COSMOS GEN3", 86, 56, Muted);

                int cardX = (screenWidth - 650) / 2;
                int cardY = (screenHeight - 480) / 2;
                const int cardWidth = 650;
                const int cardHeight = 480;

                canvas.DrawFilledRectangle(Color.FromArgb(5, 9, 14), cardX + 8, cardY + 9, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(Card, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawRectangle(Border, cardX, cardY, cardWidth, cardHeight);
                canvas.DrawFilledRectangle(SystemTheme.Accent, cardX, cardY, 5, cardHeight);

                IconManager.DrawScaled(canvas, IconType.Settings, cardX + 36, cardY + 34, 48, 48);
                canvas.DrawString("Pierwsza konfiguracja", PCScreenFont.DefaultFont, Text,
                    cardX + 104, cardY + 31);
                SmallTextRenderer.Draw(canvas,
                    "FABRYCZNE HASLO ROOT MUSI ZOSTAC ZMIENIONE PRZED URUCHOMIENIEM PULPITU",
                    cardX + 106, cardY + 73, Warning);

                canvas.DrawFilledRectangle(CardAlt, cardX + 48, cardY + 112, 554, 48);
                canvas.DrawRectangle(Border, cardX + 48, cardY + 112, 554, 48);
                SmallTextRenderer.Draw(canvas, "KONTO", cardX + 64, cardY + 132, Muted);
                SmallTextRenderer.Draw(canvas, "root", cardX + 150, cardY + 132, Text);
                SmallTextRenderer.Draw(canvas, PasswordPolicy.Summary, cardX + 260, cardY + 132, Muted);

                SmallTextRenderer.Draw(canvas, "NOWE HASLO", cardX + 48, cardY + 169, Muted);
                RenderPasswordField(canvas, cardX + 48, cardY + 184, 554, 48, 0,
                    password, passwordLength);

                SmallTextRenderer.Draw(canvas, "POWTORZ HASLO", cardX + 48, cardY + 249, Muted);
                RenderPasswordField(canvas, cardX + 48, cardY + 264, 554, 48, 1,
                    confirmation, confirmationLength);

                bool ready = passwordLength >= PasswordPolicy.MinLength && confirmationLength > 0;
                Color applyColor = ready ? SystemTheme.Accent : Color.FromArgb(54, 67, 79);
                canvas.DrawFilledRectangle(applyColor, cardX + 48, cardY + 338, 554, 52);
                canvas.DrawRectangle(SystemTheme.AccentBorder, cardX + 48, cardY + 338, 554, 52);
                IconManager.DrawScaled(canvas, IconType.Save, cardX + 66, cardY + 353, 22, 22);
                SmallTextRenderer.DrawCentered(canvas, "ZAPISZ HASLO I PRZEJDZ DO LOGOWANIA",
                    cardX + 104, cardY + 360, 468, Text);

                canvas.DrawFilledRectangle(CardAlt, cardX + 48, cardY + 405, 554, 44);
                canvas.DrawRectangle(Border, cardX + 48, cardY + 405, 554, 44);
                canvas.DrawFilledRectangle(statusColor, cardX + 63, cardY + 423, 6, 6);
                SmallTextRenderer.DrawClipped(canvas, status, cardX + 81, cardY + 422, 500, statusColor);

                SmallTextRenderer.Draw(canvas,
                    "GORA/DOL ZMIENIA POLE  |  ENTER DALEJ/ZAPISZ  |  ESC CZYSCI POLE",
                    28, screenHeight - 38, Muted);
                RenderPowerButton(canvas, screenWidth - 226, screenHeight - 66, 92,
                    "REBOOT", IconType.Reboot, false);
                RenderPowerButton(canvas, screenWidth - 124, screenHeight - 66, 100,
                    "WYLACZ", IconType.Shutdown, true);
            }

            private void RenderPasswordField(Canvas canvas, int x, int y, int width, int height,
                int fieldIndex, char[] buffer, int length)
            {
                bool active = activeField == fieldIndex;
                canvas.DrawFilledRectangle(active ? Color.FromArgb(31, 42, 51) : CardAlt,
                    x, y, width, height);
                canvas.DrawRectangle(active ? SystemTheme.AccentBorder : Border, x, y, width, height);
                if (active)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 3, height);

                if (length <= 0)
                {
                    SmallTextRenderer.Draw(canvas, "haslo", x + 15, y + 21, Muted);
                    return;
                }

                int maxVisible = System.Math.Max(1, (width - 30) / 6);
                int visible = System.Math.Min(length, System.Math.Min(maxVisible, PasswordMask.Length));
                SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, x + 15, y + 21, Text);
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

                string newPassword = new string(password, 0, passwordLength);
                string reason;
                if (!PasswordPolicy.Validate(newPassword, out reason))
                {
                    newPassword = null;
                    ClearSensitiveData();
                    activeField = 0;
                    SetStatus(reason, Danger);
                    return;
                }

                bool ok = UserManager.CompleteInitialRootPasswordSetup(newPassword);
                newPassword = null;
                ClearSensitiveData();

                if (!ok)
                {
                    activeField = 0;
                    SetStatus("NIE UDALO SIE ZAPISAC NOWEGO HASLA ROOT", Danger);
                    return;
                }

                SetStatus("HASLO ROOT ZMIENIONE - URUCHAMIAM LOGOWANIE", Good);
                Completed = true;
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
                int count = System.Math.Min(length, buffer.Length);
                for (int i = 0; i < count; i++)
                    buffer[i] = '\0';
                length = 0;
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
