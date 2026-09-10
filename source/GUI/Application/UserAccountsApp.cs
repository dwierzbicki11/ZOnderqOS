using System;
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

        // 0 none, 1 new username, 2 new password, 3 switch password.
        private int inputMode;
        private string inputText = string.Empty;
        private string pendingUsername = string.Empty;

        public UserAccountsApp(int x, int y)
            : base("Konta lokalne", "Konta i sesje użytkowników - ZOnderqOS", x, y, 900, 600)
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
            DrawRow(canvas, 5, IconType.Refresh,
                "ODSWIEZ LISTE", "Ponownie odczytaj /etc/passwd", "ODSWIEZ", Good);

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

            string title = inputMode == 1 ? "NOWY UZYTKOWNIK - NAZWA" :
                           inputMode == 2 ? "NOWY UZYTKOWNIK - HASLO" :
                           "HASLO DLA WYBRANEGO KONTA";
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

            int row = HitRow(mouseX, mouseY, 6);
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
                inputMode = 1;
                inputText = string.Empty;
                pendingUsername = string.Empty;
                SetStatus("WPISZ NAZWE NOWEGO UZYTKOWNIKA", Warning);
                return;
            }

            if (row == 4)
            {
                if (userCount <= 0)
                    return;
                inputMode = 3;
                inputText = string.Empty;
                SetStatus("WPISZ HASLO WYBRANEGO KONTA", Warning);
                return;
            }

            if (row == 5)
            {
                RefreshData();
                SetStatus("LISTA KONT ODSWIEZONA", Good);
            }
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
                inputMode = 0;
                inputText = string.Empty;
                pendingUsername = string.Empty;
                SetStatus("ANULOWANO", Muted);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (inputText.Length > 0)
                    inputText = inputText.Substring(0, inputText.Length - 1);
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
            else if (ch >= 32 && ch <= 126 && inputText.Length < 64)
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
                inputMode = 0;
                inputText = string.Empty;
                pendingUsername = string.Empty;
                RefreshData();
                SetStatus(ok ? "UTWORZONO KONTO" : "NIE UDALO SIE UTWORZYC KONTA", ok ? Good : Danger);
                return;
            }

            if (inputMode == 3)
            {
                string user = userCount > 0 ? users[selectedUser] : null;
                bool ok = !string.IsNullOrEmpty(user) && global::ZonderqOS.UserManager.ValidateCredentials(user, inputText);
                inputMode = 0;
                inputText = string.Empty;
                if (!ok)
                {
                    SetStatus("NIEPRAWIDLOWE HASLO", Danger);
                    return;
                }

                global::ZonderqOS.SecurityContext.CurrentUser = user;
                global::ZonderqOS.SecurityContext.CurrentHome = homes[selectedUser];
                global::ZonderqOS.SecurityContext.CurrentUid = uids[selectedUser];
                global::ZonderqOS.EnvironmentManager.Set("USER", user);
                global::ZonderqOS.EnvironmentManager.Set("HOME", homes[selectedUser]);
                global::ZonderqOS.SecurityLogger.LogEvent("INFO", "Session switched from Settings GUI.");
                SetStatus("SESJA UZYTKOWNIKA PRZELACZONA", Good);
            }
        }
    }
}
