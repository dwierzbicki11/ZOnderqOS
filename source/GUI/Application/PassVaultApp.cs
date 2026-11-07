using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class PassVaultApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 22, 28);
        private static readonly Color Panel = Color.FromArgb(25, 32, 40);
        private static readonly Color PanelHover = Color.FromArgb(34, 44, 56);
        private static readonly Color PanelSelected = Color.FromArgb(42, 56, 72);
        private static readonly Color Border = Color.FromArgb(50, 64, 78);
        private static readonly Color TextColor = Color.FromArgb(235, 240, 245);
        private static readonly Color MutedColor = Color.FromArgb(130, 145, 160);
        private static readonly Color AccentColor = Color.FromArgb(60, 150, 220);
        private static readonly Color GreenColor = Color.FromArgb(55, 195, 95);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);
        private static readonly Color YellowColor = Color.FromArgb(240, 195, 45);

        private const string VaultPath = "/root/.zonderq-vault.dat";

        private struct VaultEntry
        {
            public string Title;
            public string Username;
            public string Password;
            public string Category;
            public string Notes;
        }

        private readonly List<VaultEntry> entries = new List<VaultEntry>(16);
        private int selectedIndex = 0;
        private bool isUnlocked = false;
        private string pinInput = "";
        private bool showPassword = false;
        private string statusMessage = "Wpisz PIN: 1234 aby odblokowac sejf";
        private Color statusColor = AccentColor;

        // Generator state
        private string generatedPassword = "";
        private readonly Action closeCallback;

        public PassVaultApp(int x, int y, Action onClose) : base("Sejf Haseł")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 560, 410, "Sejf Haseł i Bezpieczne Notatki");
            Window.CloseAction = Close;

            LoadDefaultEntries();
        }

        private void LoadDefaultEntries()
        {
            entries.Clear();
            entries.Add(new VaultEntry { Title = "Konto Lokalne ZOnderq", Username = "zonderq", Password = "password123", Category = "System", Notes = "Konto administratora systemu" });
            entries.Add(new VaultEntry { Title = "Router Domowy Wi-Fi", Username = "admin", Password = "WPA3-SecureKey#2026", Category = "Sieć", Notes = "Adres IP: 192.168.1.1" });
            entries.Add(new VaultEntry { Title = "Serwer SSH Backup", Username = "root", Password = "Zonderq-SuperSsh!99", Category = "Serwer", Notes = "Port: 2222, klucz ed25519" });
            entries.Add(new VaultEntry { Title = "Portfel Crypto / Baza", Username = "dwierzbicki", Password = "vault-k7m9-p2x4-w8q1", Category = "Finanse", Notes = "Klucz zapasowy offline" });
            selectedIndex = 0;
        }

        public override bool NeedsContinuousRedraw => false;

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (!isUnlocked)
            {
                // Entering master PIN
                if (key.Key == ConsoleKeyEx.Backspace && pinInput.Length > 0)
                {
                    pinInput = pinInput.Substring(0, pinInput.Length - 1);
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    UnlockVault();
                    return;
                }

                if (key.KeyChar >= '0' && key.KeyChar <= '9' && pinInput.Length < 8)
                {
                    pinInput += key.KeyChar;
                    if (pinInput.Length == 4)
                    {
                        UnlockVault();
                    }
                }
                return;
            }

            // Navigation when unlocked
            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (selectedIndex > 0) selectedIndex--;
                return;
            }
            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (selectedIndex < entries.Count - 1) selectedIndex++;
                return;
            }
        }

        private void UnlockVault()
        {
            // Master PIN is 1234 or any 4+ digit PIN
            if (pinInput == "1234" || pinInput.Length >= 4)
            {
                isUnlocked = true;
                statusMessage = "Sejf odblokowany pomyślnie";
                statusColor = GreenColor;
            }
            else
            {
                pinInput = "";
                statusMessage = "Błędny PIN! Domyślny PIN to: 1234";
                statusColor = RedColor;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;

            if (leftClicked && !leftWasClicked)
            {
                // Lock screen PIN clicks
                if (!isUnlocked)
                {
                    // Number pad 0-9
                    int padX = contentX + 210;
                    int padY = contentY + 120;
                    int bW = 42;
                    int bH = 34;

                    for (int n = 1; n <= 9; n++)
                    {
                        int r = (n - 1) / 3;
                        int c = (n - 1) % 3;
                        int bx = padX + c * (bW + 8);
                        int by = padY + r * (bH + 8);

                        if (Hit(mouseX, mouseY, bx, by, bW, bH))
                        {
                            if (pinInput.Length < 8) pinInput += n.ToString();
                            if (pinInput.Length == 4) UnlockVault();
                            return;
                        }
                    }

                    // Button '0'
                    if (Hit(mouseX, mouseY, padX + bW + 8, padY + 3 * (bH + 8), bW, bH))
                    {
                        if (pinInput.Length < 8) pinInput += "0";
                        if (pinInput.Length == 4) UnlockVault();
                        return;
                    }

                    // Button 'C' (Clear)
                    if (Hit(mouseX, mouseY, padX, padY + 3 * (bH + 8), bW, bH))
                    {
                        pinInput = "";
                        return;
                    }

                    // Button 'OK'
                    if (Hit(mouseX, mouseY, padX + 2 * (bW + 8), padY + 3 * (bH + 8), bW, bH))
                    {
                        UnlockVault();
                        return;
                    }
                    return;
                }

                // Top Toolbar Buttons
                int tbY = contentY + 6;
                // [+ NOWY WPIS]
                if (Hit(mouseX, mouseY, contentX + 6, tbY, 95, 26))
                {
                    AddNewEntry();
                    return;
                }
                // [GENERUJ HASŁO]
                if (Hit(mouseX, mouseY, contentX + 107, tbY, 125, 26))
                {
                    GenerateNewPassword();
                    return;
                }
                // [BLOKUJ SEJF]
                if (Hit(mouseX, mouseY, contentX + 238, tbY, 95, 26))
                {
                    isUnlocked = false;
                    pinInput = "";
                    statusMessage = "Sejf zablokowany";
                    statusColor = MutedColor;
                    return;
                }

                // Left list item selection
                int listX = contentX + 6;
                int listY = contentY + 38;
                int itemH = 32;

                for (int i = 0; i < entries.Count; i++)
                {
                    int iy = listY + i * (itemH + 4);
                    if (Hit(mouseX, mouseY, listX, iy, 190, itemH))
                    {
                        selectedIndex = i;
                        showPassword = false;
                        return;
                    }
                }

                // Right panel interactions
                int rightX = contentX + 210;
                int detailsY = contentY + 38;

                // [POKAŻ / UKRYJ] hasło
                if (Hit(mouseX, mouseY, rightX + 230, detailsY + 76, 80, 24))
                {
                    showPassword = !showPassword;
                    return;
                }

                // [KOPIUJ] do schowka
                if (Hit(mouseX, mouseY, rightX + 145, detailsY + 76, 75, 24))
                {
                    if (selectedIndex >= 0 && selectedIndex < entries.Count)
                    {
                        statusMessage = "Skopiowano hasło wpisu: " + entries[selectedIndex].Title;
                        statusColor = GreenColor;
                    }
                    return;
                }

                // [USUŃ] wpis
                if (Hit(mouseX, mouseY, rightX + 230, detailsY + 280, 80, 26))
                {
                    if (entries.Count > 1 && selectedIndex >= 0 && selectedIndex < entries.Count)
                    {
                        entries.RemoveAt(selectedIndex);
                        selectedIndex = Math.Max(0, selectedIndex - 1);
                        statusMessage = "Usunięto wybrany wpis";
                        statusColor = YellowColor;
                    }
                    return;
                }
            }
        }

        private void AddNewEntry()
        {
            VaultEntry newEntry;
            newEntry.Title = "Nowe Konto " + (entries.Count + 1);
            newEntry.Username = "uzytkownik";
            newEntry.Password = GenerateRandomPassword(14);
            newEntry.Category = "Ogólne";
            newEntry.Notes = "Utworzono " + DateTime.UtcNow.ToString("dd.MM.yyyy");

            entries.Add(newEntry);
            selectedIndex = entries.Count - 1;
            showPassword = true;
            statusMessage = "Dodano nowy wpis: " + newEntry.Title;
            statusColor = AccentColor;
        }

        private void GenerateNewPassword()
        {
            generatedPassword = GenerateRandomPassword(16);
            if (selectedIndex >= 0 && selectedIndex < entries.Count)
            {
                VaultEntry cur = entries[selectedIndex];
                cur.Password = generatedPassword;
                entries[selectedIndex] = cur;
                showPassword = true;
                statusMessage = "Wygenerowano i przypisano nowe silne hasło!";
                statusColor = GreenColor;
            }
        }

        private static string GenerateRandomPassword(int length)
        {
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%&*";
            long seed = Stopwatch.GetTimestamp();
            char[] result = new char[length];
            for (int i = 0; i < length; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                result[i] = chars[(int)(seed % (ulong)chars.Length)];
            }
            return new string(result);
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // App background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            if (!isUnlocked)
            {
                RenderLockScreen(canvas, contentX, contentY, contentWidth, contentHeight);
                return;
            }

            // Top Toolbar
            int tbY = contentY + 6;
            canvas.DrawFilledRectangle(Panel, contentX + 4, tbY, contentWidth - 8, 28);
            canvas.DrawRectangle(Border, contentX + 4, tbY, contentWidth - 8, 28);

            DrawBtn(canvas, "+ NOWY WPIS", contentX + 6, tbY + 2, 95, 24, AccentColor);
            DrawBtn(canvas, "GENERUJ HASLO", contentX + 107, tbY + 2, 125, 24, GreenColor);
            DrawBtn(canvas, "ZABLOKUJ", contentX + 238, tbY + 2, 95, 24, MutedColor);

            // Status message
            SmallTextRenderer.Draw(canvas, statusMessage, contentX + 345, tbY + 10, statusColor);

            // Split Layout: Left list (200px), Right Details (rest)
            int bodyY = contentY + 38;
            int bodyH = contentHeight - 44;
            int listW = 195;
            int detailsX = contentX + listW + 8;
            int detailsW = contentWidth - listW - 14;

            // Render Left List
            RenderEntriesList(canvas, contentX + 4, bodyY, listW, bodyH);

            // Render Right Details
            RenderEntryDetails(canvas, detailsX, bodyY, detailsW, bodyH);
        }

        private void RenderLockScreen(Canvas canvas, int x, int y, int w, int h)
        {
            int centerX = x + w / 2;

            SmallTextRenderer.DrawCentered(canvas, "SEJF HASEL ZONDERQ", x, y + 35, w, TextColor);
            SmallTextRenderer.DrawCentered(canvas, "Wprowadz 4-cyfrowy PIN glowny aby odblokowac", x, y + 55, w, MutedColor);

            // PIN Display Box
            int pinBoxW = 140;
            int pinBoxX = centerX - pinBoxW / 2;
            int pinBoxY = y + 80;
            canvas.DrawFilledRectangle(Panel, pinBoxX, pinBoxY, pinBoxW, 30);
            canvas.DrawRectangle(AccentColor, pinBoxX, pinBoxY, pinBoxW, 30);

            // Display bullet dots for PIN
            string dots = "";
            for (int i = 0; i < pinInput.Length; i++) dots += " * ";
            SmallTextRenderer.DrawCentered(canvas, string.IsNullOrEmpty(dots) ? "----" : dots, pinBoxX, pinBoxY + 11, pinBoxW, TextColor);

            // Status banner below
            SmallTextRenderer.DrawCentered(canvas, statusMessage, x, pinBoxY + 38, w, statusColor);

            // Numeric Keypad (1..9, C, 0, OK)
            int padX = centerX - 70;
            int padY = y + 140;
            int bW = 42;
            int bH = 32;

            for (int n = 1; n <= 9; n++)
            {
                int r = (n - 1) / 3;
                int c = (n - 1) % 3;
                int bx = padX + c * (bW + 8);
                int by = padY + r * (bH + 6);
                DrawBtn(canvas, n.ToString(), bx, by, bW, bH, TextColor);
            }

            // Bottom row: C, 0, OK
            int lastY = padY + 3 * (bH + 6);
            DrawBtn(canvas, "C", padX, lastY, bW, bH, RedColor);
            DrawBtn(canvas, "0", padX + bW + 8, lastY, bW, bH, TextColor);
            DrawBtn(canvas, "OK", padX + 2 * (bW + 8), lastY, bW, bH, GreenColor);
        }

        private void RenderEntriesList(Canvas canvas, int x, int y, int w, int h)
        {
            canvas.DrawFilledRectangle(Panel, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            int itemH = 34;
            for (int i = 0; i < entries.Count; i++)
            {
                int iy = y + 4 + i * (itemH + 4);
                if (iy + itemH > y + h) break;

                bool isSel = (i == selectedIndex);
                Color bg = isSel ? PanelSelected : Panel;
                Color brd = isSel ? AccentColor : Border;

                canvas.DrawFilledRectangle(bg, x + 3, iy, w - 6, itemH);
                canvas.DrawRectangle(brd, x + 3, iy, w - 6, itemH);

                if (isSel)
                    canvas.DrawFilledRectangle(AccentColor, x + 3, iy, 3, itemH);

                SmallTextRenderer.Draw(canvas, entries[i].Title, x + 10, iy + 6, isSel ? AccentColor : TextColor);
                SmallTextRenderer.Draw(canvas, entries[i].Username, x + 10, iy + 19, MutedColor);
            }
        }

        private void RenderEntryDetails(Canvas canvas, int x, int y, int w, int h)
        {
            canvas.DrawFilledRectangle(Panel, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            if (selectedIndex < 0 || selectedIndex >= entries.Count)
                return;

            VaultEntry entry = entries[selectedIndex];

            // Header title
            SmallTextRenderer.Draw(canvas, entry.Title, x + 14, y + 14, AccentColor);
            canvas.DrawLine(Border, x + 14, y + 32, x + w - 14, y + 32);

            // Username Field
            SmallTextRenderer.Draw(canvas, "UZYTKOWNIK / LOGIN:", x + 14, y + 42, MutedColor);
            SmallTextRenderer.Draw(canvas, entry.Username, x + 14, y + 56, TextColor);

            // Password Field
            SmallTextRenderer.Draw(canvas, "HASLO:", x + 14, y + 78, MutedColor);
            string maskedPass = showPassword ? entry.Password : "••••••••••••";
            SmallTextRenderer.Draw(canvas, maskedPass, x + 14, y + 92, TextColor);

            // Buttons: [KOPIUJ], [POKAZ / UKRYJ]
            DrawBtn(canvas, "KOPIUJ", x + 145, y + 86, 75, 24, AccentColor);
            DrawBtn(canvas, showPassword ? "UKRYJ" : "POKAZ", x + 230, y + 86, 80, 24, TextColor);

            // Password Strength Indicator
            int strengthScore = CalculateStrength(entry.Password);
            string strengthLabel = strengthScore > 80 ? "BARDZO SILNE" : (strengthScore > 50 ? "DOBRE" : "SLABE");
            Color strengthColor = strengthScore > 80 ? GreenColor : (strengthScore > 50 ? YellowColor : RedColor);

            SmallTextRenderer.Draw(canvas, "SILA HASLA: " + strengthLabel, x + 14, y + 120, strengthColor);
            canvas.DrawFilledRectangle(Color.FromArgb(20, 26, 32), x + 14, y + 134, 180, 6);
            canvas.DrawFilledRectangle(strengthColor, x + 14, y + 134, (180 * strengthScore) / 100, 6);

            // Category & Notes
            SmallTextRenderer.Draw(canvas, "KATEGORIA: " + entry.Category, x + 14, y + 155, MutedColor);
            SmallTextRenderer.Draw(canvas, "NOTATKI / OPIS:", x + 14, y + 180, MutedColor);
            SmallTextRenderer.Draw(canvas, entry.Notes, x + 14, y + 196, TextColor);

            // Delete button at bottom
            DrawBtn(canvas, "USUN WPIS", x + w - 100, y + h - 36, 85, 26, RedColor);
        }

        private static int CalculateStrength(string pass)
        {
            if (string.IsNullOrEmpty(pass)) return 0;
            int score = pass.Length * 5;
            bool hasUpper = false, hasDigit = false, hasSpecial = false;
            for (int i = 0; i < pass.Length; i++)
            {
                if (char.IsUpper(pass[i])) hasUpper = true;
                if (char.IsDigit(pass[i])) hasDigit = true;
                if (!char.IsLetterOrDigit(pass[i])) hasSpecial = true;
            }
            if (hasUpper) score += 15;
            if (hasDigit) score += 15;
            if (hasSpecial) score += 20;
            return Math.Min(100, score);
        }

        private static void DrawBtn(Canvas canvas, string text, int x, int y, int w, int h, Color color)
        {
            canvas.DrawFilledRectangle(Panel, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 7) / 2, w, color);
        }
    }
}
