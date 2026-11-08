using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class WelcomeApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(18, 24, 32);
        private static readonly Color Panel = Color.FromArgb(26, 36, 48);
        private static readonly Color PanelHover = Color.FromArgb(36, 50, 68);
        private static readonly Color Border = Color.FromArgb(48, 68, 92);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color MutedColor = Color.FromArgb(130, 155, 180);
        private static readonly Color AccentColor = Color.FromArgb(40, 150, 240);
        private static readonly Color GreenColor = Color.FromArgb(50, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);

        private const int HeaderHeight = 38;
        private int activeTab = 0; // 0 = Przegląd, 1 = Nowości Alfa, 2 = Skróty, 3 = O Systemie
        private readonly Action closeCallback;
        private readonly Action<string> launchAppCallback;

        public WelcomeApp(int x, int y, Action onClose, Action<string> onLaunchApp = null)
            : base("Centrum Powitalne")
        {
            closeCallback = onClose;
            launchAppCallback = onLaunchApp;
            Window = new Window(x, y, 620, 420, "Witamy w ZOnderqOS 1.0 Alpha");
            Window.CloseAction = Close;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            base.HandleKeyboard(key);
            if (key == null) return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Tab || key.Key == ConsoleKeyEx.RightArrow)
            {
                activeTab = (activeTab + 1) % 4;
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                activeTab = (activeTab + 3) % 4;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            base.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
            if (Window == null || !Window.Visible || Window.IsMinimized) return;

            int relX = mouseX - Window.X;
            int relY = mouseY - Window.Y;

            if (!isClicked || wasClicked) return;

            // Tab bar clicks (Y: 34 to 62)
            if (relY >= 34 && relY <= 62)
            {
                int tabWidth = 145;
                if (relX >= 12 && relX < 12 + tabWidth) activeTab = 0;
                else if (relX >= 12 + tabWidth && relX < 12 + tabWidth * 2) activeTab = 1;
                else if (relX >= 12 + tabWidth * 2 && relX < 12 + tabWidth * 3) activeTab = 2;
                else if (relX >= 12 + tabWidth * 3 && relX < 12 + tabWidth * 4) activeTab = 3;
                return;
            }

            // Tab 0 Quick Action cards
            if (activeTab == 0 && relY >= 180 && relY <= 340)
            {
                int col1Left = 24, col1Right = 290;
                int col2Left = 310, col2Right = 580;

                // Row 1: Statki & Chiptune
                if (relY >= 180 && relY <= 226)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.Battleship); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Chiptune); Close(); }
                }
                // Row 2: Aktualizacje & Ustawienia
                else if (relY >= 234 && relY <= 280)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.SystemUpdater); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Settings); Close(); }
                }
                // Row 3: Menedżer plików & Terminal
                else if (relY >= 288 && relY <= 334)
                {
                    if (relX >= col1Left && relX <= col1Right) { launchAppCallback?.Invoke(AppIds.FileManager); Close(); }
                    else if (relX >= col2Left && relX <= col2Right) { launchAppCallback?.Invoke(AppIds.Terminal); Close(); }
                }
            }

            // Close button in bottom-right (Y: 365 to 395, X: 470 to 590)
            if (relY >= 365 && relY <= 395 && relX >= 470 && relX <= 590)
            {
                Close();
            }
        }

        public override void Render(Canvas canvas)
        {
            if (Window == null || !Window.Visible || Window.IsMinimized) return;

            base.Render(canvas);

            int x = Window.X + 8;
            int y = Window.Y + 28;
            int w = Window.Width - 16;
            int h = Window.Height - 36;

            canvas.DrawFilledRectangle(Surface, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);

            RenderTabBar(canvas, x + 8, y + 8, w - 16, 26);

            int contentY = y + 42;
            int contentH = h - 86;

            switch (activeTab)
            {
                case 0:
                    RenderOverviewTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 1:
                    RenderNewFeaturesTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 2:
                    RenderHotkeysTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
                case 3:
                    RenderAboutTab(canvas, x + 8, contentY, w - 16, contentH);
                    break;
            }

            RenderFooter(canvas, x + 8, y + h - 38, w - 16, 32);
        }

        private void RenderTabBar(Canvas canvas, int x, int y, int w, int h)
        {
            string[] tabs = { "1. PRZEGLAD", "2. NOWOSCI ALFA", "3. SKROTY", "4. O SYSTEMIE" };
            int tabW = w / 4;

            for (int i = 0; i < tabs.Length; i++)
            {
                int tx = x + i * tabW;
                bool isSel = (i == activeTab);

                Color bg = isSel ? Panel : Surface;
                Color border = isSel ? AccentColor : Border;
                Color textCol = isSel ? TextColor : MutedColor;

                UiPainter.RoundedPanel(canvas, tx, y, tabW - 4, h, 4, bg, border);
                SmallTextRenderer.DrawCentered(canvas, tabs[i], tx, y + 6, tabW - 4, textCol);
            }
        }

        private void RenderOverviewTab(Canvas canvas, int x, int y, int w, int h)
        {
            // Hero Banner
            UiPainter.RoundedPanel(canvas, x, y, w, 70, 6, Panel, Border);
            IconManager.DrawScaled(canvas, IconType.Start, x + 14, y + 14, 42, 42);

            SmallTextRenderer.Draw(canvas, "ZONDERQOS 1.0 ALPHA - OFICJALNE WYDANIE", x + 66, y + 14, AccentColor);
            SmallTextRenderer.Draw(canvas, "Niezalezny polski system operacyjny x86_64 oparty na Cosmos Native Core.", x + 66, y + 30, TextColor);
            SmallTextRenderer.Draw(canvas, "Wbudowane 29 aplikacji, stala pamiec FAT32 oraz samoczynne aktualizacje OTA.", x + 66, y + 46, MutedColor);

            // Subtitle
            SmallTextRenderer.Draw(canvas, "SZYBKI START - WYBIERZ APLIKACJE DO URUCHOMIENIA:", x + 4, y + 82, GoldColor);

            // Action cards (2 columns x 3 rows)
            int cardW = (w - 16) / 2;
            int cardH = 46;

            int col1 = x + 4;
            int col2 = x + 12 + cardW;

            // Row 1
            DrawActionCard(canvas, col1, y + 104, cardW, cardH, "⚓ Bitwa Morska (Statki)", "Nowa taktyczna gra wojenna z AI i dzwiekami", GreenColor);
            DrawActionCard(canvas, col2, y + 104, cardW, cardH, "🎵 Chiptune Studio", "8-bitowy syntezator muzyczny i retro pianino", AccentColor);

            // Row 2
            DrawActionCard(canvas, col1, y + 158, cardW, cardH, "🔄 Centrum Aktualizacji", "Pobieranie poprawek OTA i instalacja z USB", GoldColor);
            DrawActionCard(canvas, col2, y + 158, cardW, cardH, "🎨 Ustawienia i Motywy", "Personalizacja pulpitu, kolorow i opcji sieci", TextColor);

            // Row 3
            DrawActionCard(canvas, col1, y + 212, cardW, cardH, "📁 Menedzer Plikow", "Przegladaj dane na partycji ZONDERQDATA", TextColor);
            DrawActionCard(canvas, col2, y + 212, cardW, cardH, "💻 Konsola Terminala", "Potezna powloka CLI, narzedzia i diagnostyka", AccentColor);
        }

        private void DrawActionCard(Canvas canvas, int x, int y, int w, int h, string title, string sub, Color accent)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
            canvas.DrawFilledRectangle(accent, x + 2, y + 4, 3, h - 8);
            SmallTextRenderer.Draw(canvas, title, x + 12, y + 8, TextColor);
            SmallTextRenderer.Draw(canvas, sub, x + 12, y + 26, MutedColor);
        }

        private void RenderNewFeaturesTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "GLOWNE NOWOSCI W WYDANIU ALFA 1.0:", x + 16, y + 14, GoldColor);

            int itemY = y + 38;
            int stride = 46;

            DrawFeatureItem(canvas, x + 16, itemY, "1. Gra Bitwa Morska (Statki)",
                "Dwie siatki 10x10, pelna flota 10 okretow, taktyczny algorytm AI (Hunt Mode), efekty dzwiekowe PC Speaker.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "2. Chiptune Studio & PC Speaker Audio Engine",
                "Retro syntezator klawiszowy, podglad oscyloskopu fali prostokatnej w czasie rzeczywistym i wbudowane melodie.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "3. Silnik Aktualizacji Windows Update Style 2.0",
                "Aktualizacje bez restartow zewnetrznych, weryfikacja SHA-256, podwojne zrodlo (OTA/USB) oraz punkt przywracania (Rollback).");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "4. Szybkie Notatki (QuickNotes)",
                "Podreczne zolte karteczki na pulpicie z automatycznym zapisem do bezpiecznego profilu uzytkownika.");
            itemY += stride;

            DrawFeatureItem(canvas, x + 16, itemY, "5. Bezpieczenstwo i Trwalosc FAT32 ZONDERQDATA",
                "Ochrona integralnosci IntegrityGuard, PassVault z kodem PIN, bezpieczny menedzer pamieci masowej.");
        }

        private void DrawFeatureItem(Canvas canvas, int x, int y, string title, string desc)
        {
            SmallTextRenderer.Draw(canvas, title, x, y, AccentColor);
            SmallTextRenderer.Draw(canvas, desc, x + 8, y + 18, TextColor);
        }

        private void RenderHotkeysTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "SKROTY KLAWISZOWE I NAWIGACJA W SYSTEMIE:", x + 16, y + 14, GoldColor);

            int itemY = y + 42;
            int stride = 34;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Super / Win]", "Otwiera lub zamyka Menu Start z lista aplikacji.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Alt + F4] / [Esc]", "Zamyka aktywne okno aplikacji lub dialog.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Ctrl + Shift + Esc]", "Natychmiast uruchamia Monitor Zadan (Task Manager).");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Tab] / [Strzalki]", "Przelacza zakladki oraz elementy w oknach dialogowych.");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[zupdate] w CLI", "Zarzadzanie aktualizacjami z wiersza polecen ('zupdate check').");
            itemY += stride;

            DrawHotkeyRow(canvas, x + 16, itemY, "[Prawy Przycisk Myszy]", "Menu kontekstowe pulpitu (odswiez, szybki start, ustawienia).");
        }

        private void DrawHotkeyRow(Canvas canvas, int x, int y, string key, string desc)
        {
            UiPainter.RoundedPanel(canvas, x, y - 2, 160, 24, 3, Surface, Border);
            SmallTextRenderer.DrawCentered(canvas, key, x, y + 3, 160, AccentColor);
            SmallTextRenderer.Draw(canvas, desc, x + 175, y + 3, TextColor);
        }

        private void RenderAboutTab(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, Panel, Border);

            SmallTextRenderer.Draw(canvas, "SPECYFIKACJA TECHNICZNA ZONDERQOS 1.0 ALPHA:", x + 16, y + 14, GoldColor);

            int itemY = y + 42;
            int stride = 28;

            DrawSpecRow(canvas, x + 16, itemY, "Jadro Systemu:", "Cosmos Kernel 3.0.95 x64 Native Core (Single-vCPU deterministic)");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Architektura:", "Native AOT x86_64, asembler IL2CPU, brak interpretacji");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Srodowisko Graficzne:", "ZOnderqOS Gen 3 Desktop, VESA/GOP 1366x768 / 32bpp");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "System Plikow:", "Dedykowana partycja FAT32 ZONDERQDATA (root '/'), fail-closed");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Wydajnosc Grafiki:", "Zero heap allocation render loop (stale bufory pamieci)");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Pakiet Oprogramowania:", "29 zintegrowanych programow, 10 gier retro, menedzer pakietow");
            itemY += stride;
            DrawSpecRow(canvas, x + 16, itemY, "Licencja i Autor:", "Projekt ZOnderqOS (2026) | Autor: ZOnderq");
        }

        private void DrawSpecRow(Canvas canvas, int x, int y, string label, string value)
        {
            SmallTextRenderer.Draw(canvas, label, x, y, AccentColor);
            SmallTextRenderer.Draw(canvas, value, x + 170, y, TextColor);
        }

        private void RenderFooter(Canvas canvas, int x, int y, int w, int h)
        {
            SmallTextRenderer.Draw(canvas, "ZOnderqOS 1.0 Alpha • Dziekujemy za testowanie pierwszego oficjalnego wydania!", x + 4, y + 10, MutedColor);

            int btnW = 120;
            int btnH = 26;
            int btnX = x + w - btnW;
            int btnY = y + 3;

            UiPainter.RoundedPanel(canvas, btnX, btnY, btnW, btnH, 4, AccentColor, AccentColor);
            SmallTextRenderer.DrawCentered(canvas, "ROZPOCZNIJ PRACE", btnX, btnY + 6, btnW, Color.White);
        }
    }
}
