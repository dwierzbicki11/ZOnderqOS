using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class UserAccountsApp : SettingsToolWindow
    {
        private readonly string[] users = new string[32];
        private readonly int[] uids = new int[32];
        private readonly string[] homes = new string[32];
        private int userCount;
        private int selectedUser;

        // 0 none, 1 new username, 2 new-user password, 3 switch password,
        // 4 authorize password change, 5 new password, 6 confirm new password.
        private int inputMode;
        private string inputText = string.Empty;
        private string pendingUsername = string.Empty;
        private string pendingAuthorizationPassword = string.Empty;
        private string pendingNewPassword = string.Empty;

        public UserAccountsApp(int x, int y)
            : base("Konta lokalne", "Konta i sesje użytkowników - ZOnderqOS", x, y, 900, 724)
        {
            RefreshData();
        }

        protected override void RenderContent(Canvas canvas)
        {
            string selectedName = userCount > 0 ? users[selectedUser] : "BRAK";
            DrawRow(canvas, 0, IconType.About,
                "WYBRANE KONTO", "Kliknij aby przejsc do nastepnego lokalnego konta",
                selectedName, Accent);
            DrawNumericRow(canvas, 1, IconType.About,
                "UID", "Identyfikator wybranego konta",
                userCount > 0 ? (ulong)System.Math.Max(0, uids[selectedUser]) : 0UL, "");
            DrawRow(canvas, 2, IconType.Folder,
                "KATALOG DOMOWY", "Home wybranego uzytkownika",
                userCount > 0 ? homes[selectedUser] : "BRAK", Text);
            DrawRow(canvas, 3, IconType.Settings,
                "DODAJ UZYTKOWNIKA", "Dostepne dla sesji root; kreator pyta o nazwe i haslo",
                inputMode == 1 || inputMode == 2 ? "WPROWADZANIE" : "UTWORZ",
                global::ZonderqOS.SecurityContext.CurrentUser == "root" ? Good : Muted);
            DrawRow(canvas, 4, IconType.Start,
                "PRZELACZ SESJE", "Uwierzytelnij wybrane konto i ustaw USER/HOME",
                inputMode == 3 ? "HASLO..." : "ZALOGUJ", Warning);
            DrawRow(canvas, 5, IconType.Settings,
                "ZMIEN HASLO", "Wlasne konto lub reset przez root po potwierdzeniu hasla sesji",
                inputMode >= 4 ? "WPROWADZANIE" : CanChangeSelectedPassword() ? "ZMIEN" : "NIEDOSTEPNE",
                CanChangeSelectedPassword() ? Good : Muted);
            DrawRow(canvas, 6, IconType.Settings,
                "AUTO BLOKADA", "Blokuj pulpit po okresie bezczynnosci; kliknij aby zmienic",
                global::ZonderqOS.SystemSettings.AutoLockName,
                global::ZonderqOS.SystemSettings.AutoLockMinutes > 0 ? Good : Warning);
            DrawNumericRow(canvas, 7, IconType.About,
                "CZAS SESJI", "Minuty od ostatniego pomyslnego logowania lub przelaczenia konta",
                global::ZonderqOS.SessionManager.ElapsedSeconds / 60UL, " MIN");

            if (inputMode != 0)
                RenderInputOverlay(canvas);
        }

        private void RenderInputOverlay(Canvas canvas)
        {
            int width = System.Math.Min(520, Window.Width - 80);
            int height = 112;
            int x = Window.X + (Window.Width - width) / 2;
            int y = Window.Y + 230;
            canvas.DrawFilledRectangle(Color.FromArgb(18, 23, 29), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);

            string title;
            if (inputMode == 1)
                title = "NOWY UZYTKOWNIK - NAZWA";
            else if (inputMode == 2)
                title = "NOWY UZYTKOWNIK - HASLO";
            else if (inputMode == 3)
                title = "HASLO DLA WYBRANEGO KONTA";
            else if (inputMode == 4)
                title = "POTWIERDZ HASLO BIEZACEJ SESJI";
            else if (inputMode == 5)
                title = "NOWE HASLO";
            else
                title = "POWTORZ NOWE HASLO";

            SmallTextRenderer.Draw(canvas, title, x + 16, y + 16, Text);
            SmallTextRenderer.Draw(canvas, "ENTER DALEJ  |  ESC ANULUJ", x + 16, y + 36, Muted);

            canvas.DrawFilledRectangle(Color.FromArgb(27, 34, 41), x + 16, y + 58, width - 32, 34);
            canvas.DrawRectangle(Border, x + 16, y + 58, width - 32, 34);
            if (inputMode == 1)
                SmallTextRenderer.DrawClipped(canvas, inputText, x + 26, y + 72, width - 54, Text);
            else
                DrawMasked(canvas, inputText.Length, x + 26, y + 72, width - 54);
        }

        private static void DrawMasked(Canvas canvas, int count, int x, int y, int maxWidth)
        {
            int max = System.Math.Min(count, System.Math.Max(0, maxWidth / 6));
            for (int i = 0; i < max; i++)
                SmallTextRenderer.Draw(canvas, "*", x + i * 6, y, Text);
        }

        protected override void RefreshData()
        {
            for (int i = 0; i < users.Length; i++)
            {
                users[i] = null;
                homes[i] = null;
                uids[i] = 0;
            }

            userCount = 0;
            try
            {
                const string path = "/etc/passwd";
                if (!File.Exists(path))
                    return;

                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length && userCount < users.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    string[] parts = line.Split(':');
                    if (parts.Length < 4 || string.IsNullOrEmpty(parts[0]))
                        continue;

                    users[userCount] = parts[0];
                    int uid;
                    uids[userCount] = int.TryParse(parts[2], out uid) ? uid : 0;
                    homes[userCount] = string.IsNullOrEmpty(parts[3]) ? "/" : parts[3];
                    userCount++;
                }
            }
            catch
            {
                userCount = 0;
            }

            if (selectedUser >= userCount)
                selectedUser = userCount > 0 ? userCount - 1 : 0;
        }

        protected override void OnClick(int mouseX, int mouseY)
        {
            if (inputMode != 0)
                return;

            int row = HitRow(mouseX, mouseY, 8);
            if (row < 0)
                return;

            if (row == 0)
            {
                if (userCount > 0)
                {
                    selectedUser++;
                    if (selectedUser >= userCount)
                        selectedUser = 0;
                    SetStatus("WYBRANO KONTO", Accent);
                }
                return;
            }

            if (row == 3)
            {
                if (global::ZonderqOS.SecurityContext.CurrentUser != "root")
                {
                    SetStatus("TYLKO ROOT MOZE TWORZYC KONTA", Danger);
                    return;
                }
                ClearInputState();
                inputMode = 1;
                SetStatus("WPISZ NAZWE NOWEGO UZYTKOWNIKA", Warning);
                return;
            }

            if (row == 4)
            {
                if (userCount <= 0)
                    return;
                ClearInputState();
                inputMode = 3;
                SetStatus("WPISZ HASLO WYBRANEGO KONTA", Warning);
                return;
            }

            if (row == 5)
            {
                if (!CanChangeSelectedPassword())
                {
                    SetStatus("BRAK UPRAWNIEN DO ZMIANY HASLA TEGO KONTA", Danger);
                    return;
                }

                ClearInputState();
                inputMode = 4;
                SetStatus("POTWIERDZ HASLO BIEZACEJ SESJI", Warning);
                return;
            }

            if (row == 6)
            {
                global::ZonderqOS.SystemSettings.CycleAutoLockTimeout();
                SetStatus("ZMIENIONO CZAS AUTOMATYCZNEJ BLOKADY", Good);
                return;
            }

            if (row == 7)
                SetStatus("CZAS BIEZACEJ UWIERZYTELNIONEJ SESJI", Accent);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (inputMode == 0)
            {
                base.HandleKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                ClearInputState();
                SetStatus("ANULOWANO", Muted);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (inputText.Length > 0)
                    inputText = inputText.Substring(0, inputText.Length - 1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Delete)
            {
                inputText = string.Empty;
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                AcceptInput();
                return;
            }

            char ch = key.KeyChar;
            if (inputMode == 1)
            {
                bool allowed = (ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z') ||
                               (ch >= '0' && ch <= '9') || ch == '_' || ch == '-';
                if (allowed && inputText.Length < 32)
                    inputText += ch;
            }
            else if (ch >= 32 && ch <= 126 && inputText.Length < 128)
            {
                inputText += ch;
            }
        }

        private void AcceptInput()
        {
            if (inputMode == 1)
            {
                if (string.IsNullOrEmpty(inputText) || global::ZonderqOS.UserManager.UserExists(inputText))
                {
                    SetStatus("NAZWA PUSTA LUB KONTO JUZ ISTNIEJE", Danger);
                    return;
                }

                pendingUsername = inputText;
                inputText = string.Empty;
                inputMode = 2;
                SetStatus("WPISZ HASLO DLA NOWEGO KONTA", Warning);
                return;
            }

            if (inputMode == 2)
            {
                if (inputText.Length == 0)
                {
                    SetStatus("HASLO NIE MOZE BYC PUSTE", Danger);
                    return;
                }

                bool ok = global::ZonderqOS.UserManager.CreateUser(pendingUsername, inputText);
                ClearInputState();
                RefreshData();
                SetStatus(ok ? "UTWORZONO KONTO" : "NIE UDALO SIE UTWORZYC KONTA", ok ? Good : Danger);
                return;
            }

            if (inputMode == 3)
            {
                string user = userCount > 0 ? users[selectedUser] : null;
                bool ok = !string.IsNullOrEmpty(user) &&
                          global::ZonderqOS.UserManager.ValidateCredentials(user, inputText);
                inputText = string.Empty;
                inputMode = 0;
                if (!ok)
                {
                    SetStatus("NIEPRAWIDLOWE HASLO", Danger);
                    return;
                }

                string oldUser = global::ZonderqOS.SecurityContext.CurrentUser;
                ok = global::ZonderqOS.UserManager.ActivateSession(user);
                if (ok)
                {
                    global::ZonderqOS.SecurityLogger.LogEvent("INFO",
                        "Session switched from '" + oldUser + "' to '" + user + "' from Settings GUI.");
                    SetStatus("SESJA UZYTKOWNIKA PRZELACZONA", Good);
                }
                else
                {
                    SetStatus("NIE UDALO SIE PRZELACZYC SESJI", Danger);
                }
                return;
            }

            if (inputMode == 4)
            {
                string actor = global::ZonderqOS.SecurityContext.CurrentUser;
                if (string.IsNullOrEmpty(inputText) ||
                    !global::ZonderqOS.UserManager.ValidateCredentials(actor, inputText))
                {
                    inputText = string.Empty;
                    SetStatus("NIEPRAWIDLOWE HASLO BIEZACEJ SESJI", Danger);
                    return;
                }

                pendingAuthorizationPassword = inputText;
                inputText = string.Empty;
                inputMode = 5;
                SetStatus("WPISZ NOWE HASLO", Warning);
                return;
            }

            if (inputMode == 5)
            {
                if (inputText.Length == 0)
                {
                    SetStatus("NOWE HASLO NIE MOZE BYC PUSTE", Danger);
                    return;
                }

                pendingNewPassword = inputText;
                inputText = string.Empty;
                inputMode = 6;
                SetStatus("POWTORZ NOWE HASLO", Warning);
                return;
            }

            if (inputMode == 6)
            {
                if (inputText != pendingNewPassword)
                {
                    inputText = string.Empty;
                    pendingNewPassword = string.Empty;
                    inputMode = 5;
                    SetStatus("HASLA NIE SA IDENTYCZNE - WPISZ NOWE HASLO PONOWNIE", Danger);
                    return;
                }

                string user = userCount > 0 ? users[selectedUser] : null;
                bool ok = !string.IsNullOrEmpty(user) &&
                          global::ZonderqOS.UserManager.ChangePassword(
                              user, pendingAuthorizationPassword, pendingNewPassword);
                ClearInputState();
                SetStatus(ok ? "HASLO ZOSTALO ZMIENIONE" : "NIE UDALO SIE ZMIENIC HASLA",
                    ok ? Good : Danger);
            }
        }

        private bool CanChangeSelectedPassword()
        {
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated || userCount <= 0)
                return false;

            string selected = users[selectedUser];
            string current = global::ZonderqOS.SecurityContext.CurrentUser;
            return !string.IsNullOrEmpty(selected) &&
                   (current == "root" || current == selected);
        }

        private void ClearInputState()
        {
            inputMode = 0;
            inputText = string.Empty;
            pendingUsername = string.Empty;
            pendingAuthorizationPassword = string.Empty;
            pendingNewPassword = string.Empty;
        }
    }
}
