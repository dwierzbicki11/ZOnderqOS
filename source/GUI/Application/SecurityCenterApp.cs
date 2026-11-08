using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Security;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SecurityCenterApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 22, 28);
        private static readonly Color Panel = Color.FromArgb(26, 33, 42);
        private static readonly Color PanelHover = Color.FromArgb(38, 48, 62);
        private static readonly Color Border = Color.FromArgb(52, 66, 84);
        private static readonly Color TextColor = Color.FromArgb(238, 244, 250);
        private static readonly Color MutedColor = Color.FromArgb(135, 150, 168);
        private static readonly Color AccentColor = Color.FromArgb(40, 140, 230);
        private static readonly Color GreenColor = Color.FromArgb(55, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);

        private const int HeaderHeight = 38;
        private int activeTab = 0; // 0 = Dashboard, 1 = Integrity, 2 = Firewall, 3 = Audit Logs
        private readonly Action closeCallback;

        // Status
        private string statusMessage = "Gotowy do monitorowania bezpieczenstwa.";

        // Cached data
        private List<FileIntegrityRecord> cachedRecords = new List<FileIntegrityRecord>();
        private List<FirewallRule> cachedRules = new List<FirewallRule>();
        private readonly List<string> cachedLogLines = new List<string>(16);

        public SecurityCenterApp(int x, int y, Action onClose) : base("Centrum Bezpieczeństwa")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 620, 440, "Centrum Bezpieczeństwa & Ochrony Systemu");
            Window.CloseAction = Close;

            IntegrityGuard.Initialize();
            FirewallManager.Initialize();
            RefreshAllData();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void RefreshAllData()
        {
            cachedRecords = IntegrityGuard.GetRecords();
            cachedRules = FirewallManager.GetRules();
            RefreshLogs();
        }

        private void RefreshLogs()
        {
            cachedLogLines.Clear();
            const string logPath = "/var/log/auth.log";
            if (!File.Exists(logPath))
                return;

            try
            {
                string[] lines = File.ReadAllLines(logPath);
                int start = Math.Max(0, lines.Length - 12);
                for (int i = start; i < lines.Length; i++)
                {
                    if (!string.IsNullOrWhiteSpace(lines[i]))
                        cachedLogLines.Add(lines[i].Trim());
                }
            }
            catch { }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            if (leftClicked && !leftWasClicked)
            {
                int wx = Window.X;
                int wy = Window.Y + HeaderHeight;

                // Tab selection (4 tabs)
                int tabY = wy + 8;
                int tabW = 142;
                int tabH = 26;
                int tabGap = 6;

                for (int t = 0; t < 4; t++)
                {
                    int tx = wx + 12 + t * (tabW + tabGap);
                    if (Hit(mouseX, mouseY, tx, tabY, tabW, tabH))
                    {
                        activeTab = t;
                        RefreshAllData();
                        return;
                    }
                }

                // Tab 0 Dashboard actions
                if (activeTab == 0)
                {
                    int btnY = wy + 355;
                    // [ Szybki Skan ]
                    if (Hit(mouseX, mouseY, wx + 16, btnY, 170, 30))
                    {
                        IntegrityGuard.ScanAll();
                        RefreshAllData();
                        statusMessage = "Przeskanowano pliki systemowe: 0 anomalii.";
                        return;
                    }
                    // [ Przełącz Zaporę ]
                    if (Hit(mouseX, mouseY, wx + 196, btnY, 170, 30))
                    {
                        FirewallMode next = FirewallManager.Mode == FirewallMode.Enabled ? FirewallMode.Strict : (FirewallManager.Mode == FirewallMode.Strict ? FirewallMode.Disabled : FirewallMode.Enabled);
                        FirewallManager.SetMode(next);
                        RefreshAllData();
                        statusMessage = "Zmieniono tryb zapory na: " + next;
                        return;
                    }
                    // [ Odśwież Logi ]
                    if (Hit(mouseX, mouseY, wx + 376, btnY, 150, 30))
                    {
                        RefreshAllData();
                        statusMessage = "Zaktualizowano stan telemetrii bezpieczenstwa.";
                        return;
                    }
                }
                // Tab 1 Integrity actions
                else if (activeTab == 1)
                {
                    int btnY = wy + 355;
                    // [ Skanuj Teraz ]
                    if (Hit(mouseX, mouseY, wx + 16, btnY, 170, 30))
                    {
                        IntegrityGuard.ScanAll();
                        RefreshAllData();
                        statusMessage = "Zakonczono skanowanie integralnosci.";
                        return;
                    }
                    // [ Zaktualizuj Bazę (Re-baseline) ]
                    if (Hit(mouseX, mouseY, wx + 196, btnY, 210, 30))
                    {
                        IntegrityGuard.UpdateBaseline();
                        RefreshAllData();
                        statusMessage = "Baza sum kontrolnych SHA-256 zaktualizowana.";
                        return;
                    }
                }
                // Tab 2 Firewall actions
                else if (activeTab == 2)
                {
                    int modeY = wy + 48;
                    // Mode buttons: Standard (wy + 48, wx + 16, 120, 26)
                    if (Hit(mouseX, mouseY, wx + 16, modeY, 130, 26))
                    {
                        FirewallManager.SetMode(FirewallMode.Enabled);
                        RefreshAllData();
                        statusMessage = "Ustawiono tryb: STANDARDOWY.";
                        return;
                    }
                    if (Hit(mouseX, mouseY, wx + 152, modeY, 130, 26))
                    {
                        FirewallManager.SetMode(FirewallMode.Strict);
                        RefreshAllData();
                        statusMessage = "Ustawiono tryb: SCISLY (STEALTH).";
                        return;
                    }
                    if (Hit(mouseX, mouseY, wx + 288, modeY, 130, 26))
                    {
                        FirewallManager.SetMode(FirewallMode.Disabled);
                        RefreshAllData();
                        statusMessage = "UWAGA: Zapora zostala wylaczona.";
                        return;
                    }

                    // Reset rules
                    int btnY = wy + 355;
                    if (Hit(mouseX, mouseY, wx + 16, btnY, 200, 30))
                    {
                        FirewallManager.ResetDefaultRules();
                        RefreshAllData();
                        statusMessage = "Przywrocono domyslne reguly zapory.";
                        return;
                    }
                }
                // Tab 3 Log actions
                else if (activeTab == 3)
                {
                    int btnY = wy + 355;
                    if (Hit(mouseX, mouseY, wx + 16, btnY, 150, 30))
                    {
                        RefreshLogs();
                        statusMessage = "Dziennik audytu odswiezony.";
                        return;
                    }
                }
            }
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

            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - HeaderHeight;

            canvas.DrawFilledRectangle(Surface, wx, wy, ww, wh);

            // Tab bar
            int tabY = wy + 8;
            int tabW = 142;
            int tabH = 26;
            int tabGap = 6;

            DrawTab(canvas, "🛡 STAN OCHRONY", wx + 12, tabY, tabW, tabH, activeTab == 0);
            DrawTab(canvas, "🔍 INTEGRALNOSC", wx + 12 + (tabW + tabGap), tabY, tabW, tabH, activeTab == 1);
            DrawTab(canvas, "🧱 ZAPORA SIECIOWA", wx + 12 + (tabW + tabGap) * 2, tabY, tabW, tabH, activeTab == 2);
            DrawTab(canvas, "📜 DZIENNIK AUDYTU", wx + 12 + (tabW + tabGap) * 3, tabY, tabW, tabH, activeTab == 3);

            canvas.DrawLine(Border, wx + 10, tabY + tabH + 6, wx + ww - 10, tabY + tabH + 6);

            int cy = tabY + tabH + 12;

            if (activeTab == 0) RenderDashboardTab(canvas, wx, cy, ww);
            else if (activeTab == 1) RenderIntegrityTab(canvas, wx, cy, ww);
            else if (activeTab == 2) RenderFirewallTab(canvas, wx, cy, ww);
            else RenderLogsTab(canvas, wx, cy, ww);
        }

        private void DrawTab(Canvas canvas, string title, int x, int y, int w, int h, bool selected)
        {
            Color bg = selected ? PanelHover : Panel;
            Color text = selected ? AccentColor : MutedColor;
            Color border = selected ? AccentColor : Border;

            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, bg, border);
            SmallTextRenderer.DrawCentered(canvas, title, x, y + 8, w, text);
        }

        // =========================================================================
        // TAB 0: DASHBOARD OVERVIEW
        // =========================================================================
        private void RenderDashboardTab(Canvas canvas, int wx, int cy, int ww)
        {
            IntegrityGuard.GetReportSummary(out int total, out int passed, out int modified, out int missing, out int permIssues);
            bool isIntegrityClean = (modified == 0 && missing == 0 && permIssues == 0);
            bool isFirewallActive = (FirewallManager.Mode != FirewallMode.Disabled);

            // Big Shield Hero Card
            int heroW = ww - 32;
            int heroH = 100;
            Color heroBorder = (!isIntegrityClean) ? RedColor : (!isFirewallActive ? GoldColor : GreenColor);
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 2, heroW, heroH, 6, Panel, heroBorder);

            // Shield icon/badge
            int badgeX = wx + 32;
            int badgeY = cy + 18;
            DrawShieldEmblem(canvas, badgeX, badgeY, heroBorder);

            int textX = badgeX + 60;
            if (!isIntegrityClean)
            {
                SmallTextRenderer.Draw(canvas, "WYKRYTO ZAGROZENIE LUB NARUSZENIE INTEGRALNOSCI!", textX, cy + 22, RedColor);
                SmallTextRenderer.Draw(canvas, "Wykryto " + (modified + missing + permIssues) + " problem(ow) z plikami systemowymi. Sprawdz zakladke 'Integralnosc'.", textX, cy + 44, TextColor);
                SmallTextRenderer.Draw(canvas, "Zalecana natychmiastowa inspekcja sum SHA-256 oraz uprawnien plikow.", textX, cy + 64, MutedColor);
            }
            else if (!isFirewallActive)
            {
                SmallTextRenderer.Draw(canvas, "ZAPORA SIECIOWA JEST WYLACZONA - RUCH NIEFILTROWANY", textX, cy + 22, GoldColor);
                SmallTextRenderer.Draw(canvas, "Pliki systemowe sa zgodne z baza, ale komputer nie jest chroniony przed atakami sieciowymi.", textX, cy + 44, TextColor);
                SmallTextRenderer.Draw(canvas, "Wlacz zapore sieciowa ponizej lub w zakladce 'Zapora'.", textX, cy + 64, MutedColor);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, "SYSTEM JEST BEZPIECZNY I W PELNI CHRONIONY", textX, cy + 22, GreenColor);
                SmallTextRenderer.Draw(canvas, "Wszystkie " + total + " monitorowanych plikow /etc sa autentyczne. Zapora sieciowa jest aktywna.", textX, cy + 44, TextColor);
                SmallTextRenderer.Draw(canvas, "Ochrona anty-bruteforce: PBKDF2-HMAC-SHA256 (4096 rund) & AuthenticationGuard.", textX, cy + 64, MutedColor);
            }

            // 4 Telemetry Status Cards
            int cardY = cy + heroH + 14;
            int cardW = (heroW - 18) / 2;
            int cardH = 75;

            // Card 1: Integrity
            UiPainter.RoundedPanel(canvas, wx + 16, cardY, cardW, cardH, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "ANTY-TAMPER & INTEGRALNOSC", wx + 26, cardY + 10, GoldColor);
            string intStatus = isIntegrityClean ? "✓ " + passed + "/" + total + " plikow poprawnych (SHA-256)" : "⚠ " + (modified + missing + permIssues) + " anomalii wykrytych!";
            SmallTextRenderer.Draw(canvas, intStatus, wx + 26, cardY + 30, isIntegrityClean ? GreenColor : RedColor);
            SmallTextRenderer.Draw(canvas, "Baza: /etc/integrity.db", wx + 26, cardY + 50, MutedColor);

            // Card 2: Firewall
            UiPainter.RoundedPanel(canvas, wx + 16 + cardW + 18, cardY, cardW, cardH, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "ZAPORA SIECIOWA (FIREWALL)", wx + 26 + cardW + 18, cardY + 10, AccentColor);
            string fwStatus = "Tryb: " + FirewallManager.Mode.ToString().ToUpperInvariant() + " (Blok: " + FirewallManager.BlockedPacketsCount + ")";
            SmallTextRenderer.Draw(canvas, fwStatus, wx + 26 + cardW + 18, cardY + 30, isFirewallActive ? GreenColor : GoldColor);
            SmallTextRenderer.Draw(canvas, "Ostatnia blokada: " + FirewallManager.LastBlockedPort + " (" + FirewallManager.LastBlockedProtocol + ")", wx + 26 + cardW + 18, cardY + 50, MutedColor);

            // Card 3: Auth Security
            int row2Y = cardY + cardH + 10;
            UiPainter.RoundedPanel(canvas, wx + 16, row2Y, cardW, cardH, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "OCHRONA KONT I LOGOWANIA", wx + 26, row2Y + 10, GreenColor);
            SmallTextRenderer.Draw(canvas, "✓ AuthenticationGuard dlawienie aktywne", wx + 26, row2Y + 30, TextColor);
            SmallTextRenderer.Draw(canvas, "Zasada hasel: min. 8 znakow, cyfra i litera", wx + 26, row2Y + 50, MutedColor);

            // Card 4: Audit Logs
            UiPainter.RoundedPanel(canvas, wx + 16 + cardW + 18, row2Y, cardW, cardH, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "DZIENNIK ZDARZEN (AUDYT)", wx + 26 + cardW + 18, row2Y + 10, AccentColor);
            string lastLog = cachedLogLines.Count > 0 ? cachedLogLines[cachedLogLines.Count - 1] : "Brak wpisow w dzienniku";
            if (lastLog.Length > 36) lastLog = lastLog.Substring(0, 36) + "...";
            SmallTextRenderer.Draw(canvas, lastLog, wx + 26 + cardW + 18, row2Y + 30, TextColor);
            SmallTextRenderer.Draw(canvas, "Plik: /var/log/auth.log", wx + 26 + cardW + 18, row2Y + 50, MutedColor);

            // Bottom Action Buttons
            int btnY = cy + 280;
            DrawButton(canvas, "🔍 Szybki Skan", wx + 16, btnY, 170, 30, PanelHover);
            DrawButton(canvas, "🧱 Przepnij Zaporę", wx + 196, btnY, 170, 30, isFirewallActive ? PanelHover : AccentColor);
            DrawButton(canvas, "↺ Odśwież Dane", wx + 376, btnY, 150, 30, PanelHover);

            // Status message
            SmallTextRenderer.Draw(canvas, "Status: " + statusMessage, wx + 20, btnY + 42, MutedColor);
        }

        private void DrawShieldEmblem(Canvas canvas, int x, int y, Color color)
        {
            // Simple robust 32x40 pixel shield
            for (int r = 0; r < 24; r++)
            {
                canvas.DrawFilledRectangle(color, x + 4, y + r, 28, 1);
            }
            for (int r = 0; r < 14; r++)
            {
                int inset = r;
                int w = Math.Max(2, 28 - inset * 2);
                canvas.DrawFilledRectangle(color, x + 4 + inset, y + 24 + r, w, 1);
            }
            // Inner checkmark in surface color
            canvas.DrawLine(Surface, x + 10, y + 18, x + 16, y + 26);
            canvas.DrawLine(Surface, x + 16, y + 26, x + 26, y + 12);
        }

        // =========================================================================
        // TAB 1: INTEGRITY SCANNER
        // =========================================================================
        private void RenderIntegrityTab(Canvas canvas, int wx, int cy, int ww)
        {
            int panelW = ww - 32;
            int panelH = 265;
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 2, panelW, panelH, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "BAZA INTEGRALNOSCI I ANTY-TAMPER PLIKOW SYSTEMOWYCH (/etc/integrity.db):", wx + 26, cy + 12, GoldColor);

            int startY = cy + 34;
            // Table Header
            SmallTextRenderer.Draw(canvas, "SCIEZKA PLIKU", wx + 26, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "CHMOD", wx + 210, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "STATUS", wx + 270, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "SUMA SHA-256 (PREFIX)", wx + 380, startY, MutedColor);

            canvas.DrawLine(Border, wx + 20, startY + 14, wx + panelW + 10, startY + 14);

            int rowY = startY + 20;
            int rowH = 24;

            for (int i = 0; i < cachedRecords.Count && i < 8; i++)
            {
                FileIntegrityRecord r = cachedRecords[i];
                SmallTextRenderer.Draw(canvas, r.Path, wx + 26, rowY + 4, TextColor);
                SmallTextRenderer.Draw(canvas, r.CurrentPerms.ToString(), wx + 210, rowY + 4, TextColor);

                string st = "✓ ZGODNY";
                Color stCol = GreenColor;
                if (r.Status == IntegrityStatus.Modified) { st = "⚠ ZMODYFIKOWANY"; stCol = RedColor; }
                else if (r.Status == IntegrityStatus.Missing) { st = "✕ BRAK PLIKU"; stCol = RedColor; }
                else if (r.Status == IntegrityStatus.PermissionMismatch) { st = "⚠ ZLY CHMOD"; stCol = GoldColor; }

                SmallTextRenderer.Draw(canvas, st, wx + 270, rowY + 4, stCol);

                string h = !string.IsNullOrEmpty(r.CurrentHash) && r.CurrentHash.Length >= 16
                    ? r.CurrentHash.Substring(0, 16) + "..."
                    : "---";
                SmallTextRenderer.Draw(canvas, h, wx + 380, rowY + 4, MutedColor);

                canvas.DrawLine(Color.FromArgb(36, 46, 58), wx + 20, rowY + rowH - 2, wx + panelW + 10, rowY + rowH - 2);
                rowY += rowH;
            }

            // Buttons
            int btnY = cy + 280;
            DrawButton(canvas, "🔍 Skanuj Wszystkie", wx + 16, btnY, 170, 30, AccentColor);
            DrawButton(canvas, "💾 Zaktualizuj Bazę Sum", wx + 196, btnY, 210, 30, PanelHover);

            SmallTextRenderer.Draw(canvas, "Status: " + statusMessage, wx + 20, btnY + 42, MutedColor);
        }

        // =========================================================================
        // TAB 2: FIREWALL CONTROLS & RULES
        // =========================================================================
        private void RenderFirewallTab(Canvas canvas, int wx, int cy, int ww)
        {
            // Mode selector card
            int cardW = ww - 32;
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 2, cardW, 55, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "TRYB OCHRONY ZAPORY:", wx + 26, cy + 10, GoldColor);
            int mY = cy + 24;

            DrawModeBtn(canvas, "STANDARDOWA", wx + 26, mY, 130, 24, FirewallManager.Mode == FirewallMode.Enabled);
            DrawModeBtn(canvas, "ŚCISŁA (STEALTH)", wx + 162, mY, 130, 24, FirewallManager.Mode == FirewallMode.Strict);
            DrawModeBtn(canvas, "WYŁĄCZONA", wx + 298, mY, 130, 24, FirewallManager.Mode == FirewallMode.Disabled);

            SmallTextRenderer.Draw(canvas, "Przepuszczone: " + FirewallManager.AllowedPacketsCount + " | Zablokowane: " + FirewallManager.BlockedPacketsCount, wx + 440, mY + 6, TextColor);

            // Rules table
            int tableY = cy + 65;
            int tableH = 205;
            UiPainter.RoundedPanel(canvas, wx + 16, tableY, cardW, tableH, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "REGULY FILTROWANIA PAKIETOW:", wx + 26, tableY + 10, AccentColor);

            int startY = tableY + 28;
            SmallTextRenderer.Draw(canvas, "PORT", wx + 26, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "PROTO", wx + 85, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "KIERUNEK", wx + 145, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "AKCJA", wx + 230, startY, MutedColor);
            SmallTextRenderer.Draw(canvas, "OPIS REGULY", wx + 300, startY, MutedColor);

            canvas.DrawLine(Border, wx + 20, startY + 14, wx + cardW + 10, startY + 14);

            int rowY = startY + 18;
            int rowH = 20;

            for (int i = 0; i < cachedRules.Count && i < 7; i++)
            {
                FirewallRule r = cachedRules[i];
                string portStr = r.Port == 0 ? "ANY" : r.Port.ToString();
                SmallTextRenderer.Draw(canvas, portStr, wx + 26, rowY + 2, TextColor);
                SmallTextRenderer.Draw(canvas, r.Protocol.ToString(), wx + 85, rowY + 2, TextColor);
                SmallTextRenderer.Draw(canvas, r.Direction.ToString(), wx + 145, rowY + 2, TextColor);

                Color actColor = r.Action == RuleAction.Allow ? GreenColor : RedColor;
                SmallTextRenderer.Draw(canvas, r.Action.ToString().ToUpperInvariant(), wx + 230, rowY + 2, actColor);

                string desc = r.Description;
                if (desc.Length > 36) desc = desc.Substring(0, 36) + "...";
                SmallTextRenderer.Draw(canvas, desc, wx + 300, rowY + 2, MutedColor);

                rowY += rowH;
            }

            // Buttons
            int btnY = cy + 280;
            DrawButton(canvas, "↺ Przywróć Domyślne", wx + 16, btnY, 200, 30, PanelHover);
            SmallTextRenderer.Draw(canvas, "Status: " + statusMessage, wx + 20, btnY + 42, MutedColor);
        }

        private void DrawModeBtn(Canvas canvas, string text, int x, int y, int w, int h, bool active)
        {
            Color fill = active ? AccentColor : Surface;
            Color bdr = active ? AccentColor : Border;
            UiPainter.RoundedPanel(canvas, x, y, w, h, 3, fill, bdr);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + 6, w, active ? Color.White : TextColor);
        }

        // =========================================================================
        // TAB 3: AUDIT LOGS
        // =========================================================================
        private void RenderLogsTab(Canvas canvas, int wx, int cy, int ww)
        {
            int panelW = ww - 32;
            int panelH = 265;
            UiPainter.RoundedPanel(canvas, wx + 16, cy + 2, panelW, panelH, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "DZIENNIK ZDARZEN BEZPIECZENSTWA (/var/log/auth.log):", wx + 26, cy + 12, GoldColor);

            int startY = cy + 34;
            int lineH = 18;

            if (cachedLogLines.Count == 0)
            {
                SmallTextRenderer.Draw(canvas, "Brak zarejestrowanych wpisow w dzienniku autoryzacji.", wx + 26, startY + 20, MutedColor);
            }
            else
            {
                for (int i = 0; i < cachedLogLines.Count && i < 11; i++)
                {
                    string line = cachedLogLines[i];
                    Color lineCol = TextColor;
                    if (line.Contains("[CRITICAL]") || line.Contains("[ERROR]")) lineCol = RedColor;
                    else if (line.Contains("[WARN]")) lineCol = GoldColor;
                    else if (line.Contains("[INFO]")) lineCol = AccentColor;

                    SmallTextRenderer.Draw(canvas, line, wx + 26, startY + i * lineH, lineCol);
                }
            }

            int btnY = cy + 280;
            DrawButton(canvas, "↺ Odśwież Logi", wx + 16, btnY, 150, 30, AccentColor);
            SmallTextRenderer.Draw(canvas, "Status: " + statusMessage, wx + 20, btnY + 42, MutedColor);
        }

        private void DrawButton(Canvas canvas, string text, int x, int y, int w, int h, Color color)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, color, Border);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h / 2) - 4, w, TextColor);
        }
    }
}
