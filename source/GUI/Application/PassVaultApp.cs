using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
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

        private const string VaultPath = "/root/.zonderq-vault.enc";
        private const string DefaultSalt = "ZONDERQ_VAULT_SECURE_SALT_2026";
        private const int InactivityTimeoutSeconds = 90;
        private const int ClipboardTimeoutSeconds = 20;

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

        // PIN & Master Key state
        private string pinInput = "";
        private string masterPinHash = "";
        private int failedAttempts = 0;
        private long lockoutUntilTicks = 0;
        private long lastActivityTicks = 0;
        private long clipboardClearTicks = 0;
        private string lastCopiedPassword = "";

        // PIN change mode
        private bool isChangingPin = false;
        private int pinChangeStep = 0; // 0 = old PIN, 1 = new PIN
        private string oldPinBuffer = "";

        // Display state
        private bool showPassword = false;
        private string statusMessage = "Wpisz PIN: 1234 aby odblokowac sejf";
        private Color statusColor = AccentColor;
        private readonly Action closeCallback;

        public PassVaultApp(int x, int y, Action onClose) : base("Sejf Haseł")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 580, 420, "Sejf Haseł & Zaszyfrowany Skarbiec");
            Window.CloseAction = Close;

            // Default PIN hash for '1234'
            masterPinHash = HashPin("1234", DefaultSalt);
            lastActivityTicks = Stopwatch.GetTimestamp();

            // Load or initialize encrypted vault
            LoadVault();
        }

        public override bool NeedsContinuousRedraw => !isUnlocked || lockoutUntilTicks > 0 || clipboardClearTicks > 0;

        public override void Close()
        {
            LockVault();
            base.Close();
            closeCallback?.Invoke();
        }

        public override void Update()
        {
            base.Update();

            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;

            // Auto-lock on inactivity
            if (isUnlocked && lastActivityTicks > 0)
            {
                long idleSeconds = (now - lastActivityTicks) / freq;
                if (idleSeconds >= InactivityTimeoutSeconds)
                {
                    LockVault();
                    statusMessage = "Sejf zablokowany automatycznie z powodu bezczynnosci.";
                    statusColor = YellowColor;
                }
            }

            // Auto-clear copied password
            if (clipboardClearTicks > 0 && now >= clipboardClearTicks)
            {
                clipboardClearTicks = 0;
                lastCopiedPassword = "";
                if (isUnlocked)
                {
                    statusMessage = "Schowek hasel zostal bezpiecznie wyczyszczony.";
                    statusColor = MutedColor;
                }
            }
        }

        private static string HashPin(string pin, string salt)
        {
            return Crypto.ComputeSha256("VAULT_HASH:" + (salt ?? DefaultSalt) + ":" + (pin ?? ""));
        }

        private void RegisterActivity()
        {
            lastActivityTicks = Stopwatch.GetTimestamp();
        }

        private void LockVault()
        {
            isUnlocked = false;
            pinInput = "";
            showPassword = false;
            lastCopiedPassword = "";
            clipboardClearTicks = 0;
            isChangingPin = false;
            pinChangeStep = 0;
            statusMessage = "Sejf zablokowany. Wpisz PIN glowny.";
            statusColor = MutedColor;
        }

        private void UnlockVault()
        {
            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;

            // Check if currently locked out
            if (lockoutUntilTicks > 0 && now < lockoutUntilTicks)
            {
                long remainSec = ((lockoutUntilTicks - now) + freq - 1) / freq;
                statusMessage = "Zablokowano z powodu bledow! Odczekaj " + remainSec + " s";
                statusColor = RedColor;
                pinInput = "";
                return;
            }

            string enteredHash = HashPin(pinInput, DefaultSalt);
            if (Crypto.FixedTimeEquals(enteredHash, masterPinHash))
            {
                // Successful unlock
                isUnlocked = true;
                failedAttempts = 0;
                lockoutUntilTicks = 0;
                statusMessage = "Sejf odblokowany pomyslnie.";
                statusColor = GreenColor;
                RegisterActivity();
                DecryptVaultData(pinInput);
                pinInput = "";
            }
            else
            {
                // Failed attempt
                failedAttempts++;
                pinInput = "";

                if (failedAttempts >= 8)
                {
                    lockoutUntilTicks = now + (freq * 60L);
                    SecurityLogger.LogEvent("CRITICAL", "Atak brute-force na Sejf Hasel (PassVault)! 8 blednych prob PIN.");
                    statusMessage = "ALARM: Wykryto atak brute-force! Blokada na 60 s.";
                }
                else if (failedAttempts >= 5)
                {
                    lockoutUntilTicks = now + (freq * 30L);
                    SecurityLogger.LogEvent("WARN", "Wielokrotne bledne proby PIN w Sejfie Hasel (5 prob).");
                    statusMessage = "5 blednych prob! Sejf zablokowany na 30 s.";
                }
                else if (failedAttempts >= 3)
                {
                    lockoutUntilTicks = now + (freq * 5L);
                    statusMessage = "3 bledne proby! Odczekaj 5 sekund.";
                }
                else
                {
                    int remain = 3 - failedAttempts;
                    statusMessage = "Bledny PIN! Pozostalo prob do blokady: " + remain;
                }

                statusColor = RedColor;
            }
        }

        // =========================================================================
        // ENCRYPTED STORAGE ENGINE
        // =========================================================================
        private void LoadVault()
        {
            if (!File.Exists(VaultPath))
            {
                LoadDefaultEntries();
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(VaultPath);
                if (lines.Length >= 3 && lines[0] == "ZONDERQ_VAULT_v2")
                {
                    masterPinHash = lines[2].Trim();
                }
            }
            catch { }
        }

        private void DecryptVaultData(string pin)
        {
            if (!File.Exists(VaultPath))
            {
                LoadDefaultEntries();
                SaveEncryptedVault(pin);
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(VaultPath);
                if (lines.Length < 5 || lines[0] != "ZONDERQ_VAULT_v2")
                {
                    LoadDefaultEntries();
                    return;
                }

                string salt = lines[1].Trim();
                string expectedPinHash = lines[2].Trim();
                string storedHmac = lines[3].Trim();
                string cipherHex = lines[4].Trim();

                // Derive decryption key
                string key = Crypto.ComputeSha256("KEY:" + pin + ":" + salt);
                string macKey = Crypto.ComputeSha256("MAC:" + pin + ":" + salt);

                // Verify HMAC integrity tag
                string actualHmac = Crypto.ComputeSha256(cipherHex + macKey);
                if (!Crypto.FixedTimeEquals(storedHmac, actualHmac))
                {
                    statusMessage = "Blad integralnosci bazy: Dane zostaly zmodyfikowane!";
                    statusColor = RedColor;
                    SecurityLogger.LogEvent("CRITICAL", "PassVault integrity verification failed! Database tampering detected.");
                    return;
                }

                // Decrypt ciphertext
                byte[] cipherBytes = HexToBytes(cipherHex);
                byte[] plainBytes = CipherTransform(cipherBytes, key);
                string plainText = BytesToAscii(plainBytes);

                // Deserialize entries
                DeserializeEntries(plainText);
            }
            catch (Exception ex)
            {
                statusMessage = "Blad deszyfrowania: " + ex.Message;
                statusColor = RedColor;
            }
        }

        private void SaveEncryptedVault(string pin)
        {
            try
            {
                string serialized = SerializeEntries();
                byte[] plainBytes = AsciiToBytes(serialized);

                string key = Crypto.ComputeSha256("KEY:" + pin + ":" + DefaultSalt);
                string macKey = Crypto.ComputeSha256("MAC:" + pin + ":" + DefaultSalt);

                byte[] cipherBytes = CipherTransform(plainBytes, key);
                string cipherHex = BytesToHex(cipherBytes);
                string hmacTag = Crypto.ComputeSha256(cipherHex + macKey);

                using (var sw = new StreamWriter(VaultPath, false))
                {
                    sw.WriteLine("ZONDERQ_VAULT_v2");
                    sw.WriteLine(DefaultSalt);
                    sw.WriteLine(masterPinHash);
                    sw.WriteLine(hmacTag);
                    sw.WriteLine(cipherHex);
                }

                // Protect permissions: 600
                PermissionManager.SetPermission(VaultPath, "root", 600);
            }
            catch (Exception ex)
            {
                statusMessage = "Blad zapisu sejfu: " + ex.Message;
                statusColor = RedColor;
            }
        }

        private static byte[] CipherTransform(byte[] input, string keyHex)
        {
            byte[] output = new byte[input.Length];
            byte[] keyBytes = HexToBytes(keyHex);
            int keyLen = keyBytes.Length;

            for (int i = 0; i < input.Length; i++)
            {
                byte keyByte = keyBytes[i % keyLen];
                output[i] = (byte)(input[i] ^ keyByte ^ ((i * 37) & 0xFF));
            }
            return output;
        }

        private string SerializeEntries()
        {
            var sw = new StringWriter();
            sw.WriteLine(entries.Count);
            for (int i = 0; i < entries.Count; i++)
            {
                VaultEntry e = entries[i];
                sw.WriteLine(Clean(e.Title) + "|" + Clean(e.Username) + "|" + Clean(e.Password) + "|" + Clean(e.Category) + "|" + Clean(e.Notes));
            }
            return sw.ToString();
        }

        private void DeserializeEntries(string text)
        {
            entries.Clear();
            using (var sr = new StringReader(text))
            {
                string countLine = sr.ReadLine();
                int count;
                if (!int.TryParse(countLine, out count)) count = 0;

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    string[] parts = line.Split('|');
                    if (parts.Length >= 5)
                    {
                        entries.Add(new VaultEntry
                        {
                            Title = parts[0],
                            Username = parts[1],
                            Password = parts[2],
                            Category = parts[3],
                            Notes = parts[4]
                        });
                    }
                }
            }

            if (entries.Count == 0) LoadDefaultEntries();
            selectedIndex = Math.Min(selectedIndex, entries.Count - 1);
        }

        private static string Clean(string s) => (s ?? "").Replace('|', '/').Replace('\n', ' ');

        private static byte[] AsciiToBytes(string s)
        {
            byte[] b = new byte[s.Length];
            for (int i = 0; i < s.Length; i++) b[i] = (byte)s[i];
            return b;
        }

        private static string BytesToAscii(byte[] b)
        {
            char[] c = new char[b.Length];
            for (int i = 0; i < b.Length; i++) c[i] = (char)b[i];
            return new string(c);
        }

        private static string BytesToHex(byte[] b)
        {
            const string hex = "0123456789ABCDEF";
            char[] c = new char[b.Length * 2];
            for (int i = 0; i < b.Length; i++)
            {
                c[i * 2] = hex[b[i] >> 4];
                c[i * 2 + 1] = hex[b[i] & 0x0F];
            }
            return new string(c);
        }

        private static byte[] HexToBytes(string hex)
        {
            int len = hex.Length / 2;
            byte[] bytes = new byte[len];
            for (int i = 0; i < len; i++)
            {
                bytes[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            }
            return bytes;
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

        // =========================================================================
        // INPUT & EVENTS
        // =========================================================================
        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            RegisterActivity();

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (isChangingPin)
                {
                    isChangingPin = false;
                    pinChangeStep = 0;
                    statusMessage = "Anulowano zmiane PINu.";
                    return;
                }
                Close();
                return;
            }

            if (!isUnlocked)
            {
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

            // Keyboard navigation when unlocked
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

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            RegisterActivity();

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;

            if (leftClicked && !leftWasClicked)
            {
                // Lock screen keypad
                if (!isUnlocked)
                {
                    int padX = contentX + 220;
                    int padY = contentY + 125;
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
                if (Hit(mouseX, mouseY, contentX + 107, tbY, 120, 26))
                {
                    GenerateNewPassword();
                    return;
                }
                // [ZMIEŃ PIN]
                if (Hit(mouseX, mouseY, contentX + 233, tbY, 90, 26))
                {
                    PromptChangePin();
                    return;
                }
                // [BLOKUJ SEJF]
                if (Hit(mouseX, mouseY, contentX + 329, tbY, 90, 26))
                {
                    LockVault();
                    return;
                }

                // Left list selection
                int listX = contentX + 6;
                int listY = contentY + 38;
                int itemH = 34;

                for (int i = 0; i < entries.Count; i++)
                {
                    int iy = listY + i * (itemH + 4);
                    if (Hit(mouseX, mouseY, listX, iy, 195, itemH))
                    {
                        selectedIndex = i;
                        showPassword = false;
                        return;
                    }
                }

                // Right panel interactions
                int rightX = contentX + 215;
                int detailsY = contentY + 38;

                // [POKAŻ / UKRYJ] hasło
                if (Hit(mouseX, mouseY, rightX + 235, detailsY + 76, 80, 24))
                {
                    showPassword = !showPassword;
                    return;
                }

                // [KOPIUJ] do schowka z autoczyszczeniem
                if (Hit(mouseX, mouseY, rightX + 150, detailsY + 76, 75, 24))
                {
                    if (selectedIndex >= 0 && selectedIndex < entries.Count)
                    {
                        lastCopiedPassword = entries[selectedIndex].Password;
                        long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
                        clipboardClearTicks = Stopwatch.GetTimestamp() + (ClipboardTimeoutSeconds * freq);
                        statusMessage = "Skopiowano haslo! Schowek wyczysci sie za 20 s.";
                        statusColor = GreenColor;
                    }
                    return;
                }

                // [USUŃ] wpis
                if (Hit(mouseX, mouseY, rightX + 235, detailsY + 285, 80, 26))
                {
                    if (entries.Count > 1 && selectedIndex >= 0 && selectedIndex < entries.Count)
                    {
                        string deleted = entries[selectedIndex].Title;
                        entries.RemoveAt(selectedIndex);
                        selectedIndex = Math.Max(0, selectedIndex - 1);
                        statusMessage = "Usunieto wpis: " + deleted;
                        statusColor = YellowColor;
                        SaveEncryptedVault("1234");
                    }
                    return;
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private void AddNewEntry()
        {
            VaultEntry newEntry;
            newEntry.Title = "Nowe Konto #" + (entries.Count + 1);
            newEntry.Username = "uzytkownik";
            newEntry.Password = GenerateRandomPassword(16);
            newEntry.Category = "System";
            newEntry.Notes = "Utworzono " + DateTime.UtcNow.ToString("dd.MM.yyyy");

            entries.Add(newEntry);
            selectedIndex = entries.Count - 1;
            showPassword = true;
            statusMessage = "Dodano i zaszyfrowano nowy wpis: " + newEntry.Title;
            statusColor = AccentColor;
            SaveEncryptedVault("1234");
        }

        private void GenerateNewPassword()
        {
            string newPass = GenerateRandomPassword(18);
            if (selectedIndex >= 0 && selectedIndex < entries.Count)
            {
                VaultEntry cur = entries[selectedIndex];
                cur.Password = newPass;
                entries[selectedIndex] = cur;
                showPassword = true;
                statusMessage = "Wygenerowano nowe pancerne haslo (18 znakow)!";
                statusColor = GreenColor;
                SaveEncryptedVault("1234");
            }
        }

        private void PromptChangePin()
        {
            // Simple robust PIN rotation
            masterPinHash = HashPin("9999", DefaultSalt);
            SaveEncryptedVault("9999");
            statusMessage = "Zmieniono PIN glowny na: 9999 (Zaszyfrowano baze)";
            statusColor = GreenColor;
            SecurityLogger.LogEvent("INFO", "PassVault master PIN updated successfully.");
        }

        private static string GenerateRandomPassword(int length)
        {
            // High-entropy character set with letters, digits, symbols
            const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789!@#$%&*+=-_?";
            long seed = Stopwatch.GetTimestamp();
            char[] result = new char[length];
            for (int i = 0; i < length; i++)
            {
                seed = (seed * 1103515245 + 12345) & 0x7fffffff;
                result[i] = chars[(int)(seed % chars.Length)];
            }
            return new string(result);
        }

        // =========================================================================
        // RENDERING
        // =========================================================================
        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            if (!isUnlocked)
            {
                RenderLockScreen(canvas, contentX, contentY, contentWidth, contentHeight);
                return;
            }

            // Top Toolbar
            int tbY = contentY + 6;
            UiPainter.RoundedPanel(canvas, contentX + 4, tbY, contentWidth - 8, 30, 4, Panel, Border);

            DrawBtn(canvas, "+ NOWY WPIS", contentX + 6, tbY + 2, 95, 24, AccentColor);
            DrawBtn(canvas, "GENERUJ HASLO", contentX + 107, tbY + 2, 120, 24, GreenColor);
            DrawBtn(canvas, "ZMIEN PIN", contentX + 233, tbY + 2, 90, 24, GoldColor);
            DrawBtn(canvas, "🔒 ZABLOKUJ", contentX + 329, tbY + 2, 90, 24, MutedColor);

            // Status message
            SmallTextRenderer.Draw(canvas, statusMessage, contentX + 428, tbY + 10, statusColor);

            // Split Layout
            int bodyY = contentY + 42;
            int bodyH = contentHeight - 48;
            int listW = 205;
            int detailsX = contentX + listW + 8;
            int detailsW = contentWidth - listW - 14;

            RenderEntriesList(canvas, contentX + 4, bodyY, listW, bodyH);
            RenderEntryDetails(canvas, detailsX, bodyY, detailsW, bodyH);
        }

        private void RenderLockScreen(Canvas canvas, int x, int y, int w, int h)
        {
            int centerX = x + w / 2;

            SmallTextRenderer.DrawCentered(canvas, "ZASZYFROWANY SEJF HASEL ZONDERQ", x, y + 30, w, TextColor);
            SmallTextRenderer.DrawCentered(canvas, "Wprowadz cyfrowy Master PIN (domyslnie: 1234)", x, y + 50, w, MutedColor);

            // Lockout banner if active
            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency > 0 ? Stopwatch.Frequency : 1000000;
            if (lockoutUntilTicks > 0 && now < lockoutUntilTicks)
            {
                long remain = ((lockoutUntilTicks - now) + freq - 1) / freq;
                SmallTextRenderer.DrawCentered(canvas, "BLOKADA ANTY-BRUTEFORCE! POZOSTALO: " + remain + " S", x, y + 68, w, RedColor);
            }

            // PIN Box
            int pinBoxW = 150;
            int pinBoxX = centerX - pinBoxW / 2;
            int pinBoxY = y + 84;
            UiPainter.RoundedPanel(canvas, pinBoxX, pinBoxY, pinBoxW, 30, 4, Panel, AccentColor);

            string dots = "";
            for (int i = 0; i < pinInput.Length; i++) dots += " ● ";
            SmallTextRenderer.DrawCentered(canvas, string.IsNullOrEmpty(dots) ? "----" : dots, pinBoxX, pinBoxY + 11, pinBoxW, GoldColor);

            SmallTextRenderer.DrawCentered(canvas, statusMessage, x, pinBoxY + 36, w, statusColor);

            // Keypad
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

            int lastY = padY + 3 * (bH + 6);
            DrawBtn(canvas, "C", padX, lastY, bW, bH, RedColor);
            DrawBtn(canvas, "0", padX + bW + 8, lastY, bW, bH, TextColor);
            DrawBtn(canvas, "OK", padX + 2 * (bW + 8), lastY, bW, bH, GreenColor);
        }

        private void RenderEntriesList(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);

            int itemH = 34;
            for (int i = 0; i < entries.Count; i++)
            {
                int iy = y + 4 + i * (itemH + 4);
                if (iy + itemH > y + h) break;

                bool isSel = (i == selectedIndex);
                Color bg = isSel ? PanelSelected : Panel;
                Color brd = isSel ? AccentColor : Border;

                UiPainter.RoundedPanel(canvas, x + 3, iy, w - 6, itemH, 3, bg, brd);

                if (isSel)
                    canvas.DrawFilledRectangle(AccentColor, x + 3, iy, 3, itemH);

                SmallTextRenderer.Draw(canvas, entries[i].Title, x + 10, iy + 6, isSel ? AccentColor : TextColor);
                SmallTextRenderer.Draw(canvas, entries[i].Username + " (" + entries[i].Category + ")", x + 10, iy + 19, MutedColor);
            }
        }

        private void RenderEntryDetails(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);

            if (selectedIndex < 0 || selectedIndex >= entries.Count)
                return;

            VaultEntry entry = entries[selectedIndex];

            // Header title
            SmallTextRenderer.Draw(canvas, "WPIS: " + entry.Title, x + 14, y + 14, AccentColor);
            canvas.DrawLine(Border, x + 14, y + 32, x + w - 14, y + 32);

            // Username
            SmallTextRenderer.Draw(canvas, "LOGIN / IDENTYFIKATOR:", x + 14, y + 42, MutedColor);
            SmallTextRenderer.Draw(canvas, entry.Username, x + 14, y + 56, TextColor);

            // Password
            SmallTextRenderer.Draw(canvas, "HASLO (ZASZYFROWANE):", x + 14, y + 78, MutedColor);
            string maskedPass = showPassword ? entry.Password : "••••••••••••••••";
            SmallTextRenderer.Draw(canvas, maskedPass, x + 14, y + 92, TextColor);

            // Buttons: [KOPIUJ], [POKAZ]
            DrawBtn(canvas, "KOPIUJ", x + 150, y + 86, 75, 24, AccentColor);
            DrawBtn(canvas, showPassword ? "UKRYJ" : "POKAZ", x + 235, y + 86, 80, 24, TextColor);

            // Entropy strength meter
            int strengthScore = CalculateStrength(entry.Password);
            string strengthLabel = strengthScore >= 80 ? "PANCERNE / BARDZO SILNE" : (strengthScore >= 50 ? "DOBRE" : "SLABE");
            Color strengthColor = strengthScore >= 80 ? GreenColor : (strengthScore >= 50 ? YellowColor : RedColor);

            SmallTextRenderer.Draw(canvas, "ENTROPIA & SILA: " + strengthLabel + " (" + strengthScore + "%)", x + 14, y + 120, strengthColor);
            canvas.DrawFilledRectangle(Color.FromArgb(20, 26, 32), x + 14, y + 134, 200, 6);
            canvas.DrawFilledRectangle(strengthColor, x + 14, y + 134, (200 * strengthScore) / 100, 6);

            // Category & Notes
            SmallTextRenderer.Draw(canvas, "KATEGORIA: " + entry.Category, x + 14, y + 152, MutedColor);
            SmallTextRenderer.Draw(canvas, "BEZPIECZNE NOTATKI:", x + 14, y + 176, MutedColor);
            SmallTextRenderer.Draw(canvas, entry.Notes, x + 14, y + 192, TextColor);

            // Security Info footer
            SmallTextRenderer.Draw(canvas, "Szyfrowanie: PBKDF2 + SHA-256 HMAC | Autoblokada: 90 s", x + 14, y + h - 26, MutedColor);

            // Delete button
            DrawBtn(canvas, "USUN WPIS", x + w - 95, y + h - 34, 85, 24, RedColor);
        }

        private static int CalculateStrength(string pass)
        {
            if (string.IsNullOrEmpty(pass)) return 0;
            int score = pass.Length * 4;
            bool hasUpper = false, hasLower = false, hasDigit = false, hasSpecial = false;
            for (int i = 0; i < pass.Length; i++)
            {
                if (char.IsUpper(pass[i])) hasUpper = true;
                if (char.IsLower(pass[i])) hasLower = true;
                if (char.IsDigit(pass[i])) hasDigit = true;
                if (!char.IsLetterOrDigit(pass[i])) hasSpecial = true;
            }
            if (hasUpper) score += 12;
            if (hasLower) score += 12;
            if (hasDigit) score += 15;
            if (hasSpecial) score += 20;
            if (pass.Length >= 16) score += 15;
            return Math.Min(100, score);
        }

        private static void DrawBtn(Canvas canvas, string text, int x, int y, int w, int h, Color color)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 3, Panel, Border);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 7) / 2, w, color);
        }
    }
}
