using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Security;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SystemUpdaterApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color Panel = Color.FromArgb(28, 35, 45);
        private static readonly Color PanelHover = Color.FromArgb(38, 48, 62);
        private static readonly Color Border = Color.FromArgb(52, 66, 84);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 250);
        private static readonly Color MutedColor = Color.FromArgb(135, 150, 168);
        private static readonly Color AccentColor = Color.FromArgb(45, 145, 235);
        private static readonly Color GreenColor = Color.FromArgb(55, 215, 105);
        private static readonly Color GoldColor = Color.FromArgb(245, 195, 45);
        private static readonly Color RedColor = Color.FromArgb(235, 75, 75);

        private const int HeaderHeight = 38;
        private int activeTab = 0; // 0 = Changelog, 1 = Components, 2 = History
        private readonly Action closeCallback;

        private string statusMessage = "Gotowy do sprawdzania i instalowania aktualizacji.";
        private Color statusColor = MutedColor;

        // Progress bar simulation state
        private bool isBusy = false;
        private int currentProgress = 0;
        private string currentStepText = "";

        public SystemUpdaterApp(int x, int y, Action onClose) : base("Aktualizacje Systemu")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 620, 440, "Centrum Aktualizacji ZonderqOS");
            Window.CloseAction = Close;

            UpdateManager.Initialize();
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

            if (key.Key == ConsoleKeyEx.F5)
            {
                CheckUpdatesNow(true);
            }
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

                // 1. Channel Selector Pill (wx + 16, wy + 76, 120, 24)
                if (Hit(mouseX, mouseY, wx + 16, wy + 76, 120, 24))
                {
                    CycleChannel();
                    return;
                }

                // 2. [SPRAWDŹ TERAZ] (wx + 360, wy + 76, 115, 24)
                if (Hit(mouseX, mouseY, wx + 360, wy + 76, 115, 24))
                {
                    CheckUpdatesNow(false);
                    return;
                }

                // 3. [PRZYWRÓĆ (ROLLBACK)] (wx + 482, wy + 76, 122, 24)
                if (Hit(mouseX, mouseY, wx + 482, wy + 76, 122, 24))
                {
                    PerformRollback();
                    return;
                }

                // 4. Action Banner Button (Install or Reboot)
                if (UpdateManager.Status == UpdateStatus.Available && UpdateManager.AvailablePackage != null)
                {
                    if (Hit(mouseX, mouseY, wx + 450, wy + 110, 154, 28))
                    {
                        PerformInstall();
                        return;
                    }
                }
                else if (UpdateManager.IsRebootRequired)
                {
                    if (Hit(mouseX, mouseY, wx + 450, wy + 110, 154, 28))
                    {
                        PerformReboot();
                        return;
                    }
                }

                // 5. Tabs (wy + 150, 4 tabs)
                int tabY = wy + 150;
                int tabW = (Window.Width - 24 - 18) / 4;
                int tabH = 26;
                for (int t = 0; t < 4; t++)
                {
                    int tx = wx + 12 + t * (tabW + 6);
                    if (Hit(mouseX, mouseY, tx, tabY, tabW, tabH))
                    {
                        activeTab = t;
                        return;
                    }
                }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private void CycleChannel()
        {
            UpdateChannel cur = UpdateManager.Channel;
            UpdateChannel next = cur switch
            {
                UpdateChannel.Stable => UpdateChannel.Lts,
                UpdateChannel.Lts => UpdateChannel.Beta,
                UpdateChannel.Beta => UpdateChannel.Testing,
                _ => UpdateChannel.Stable
            };

            UpdateManager.SetChannel(next);
            statusMessage = "Przestawiono kanal na: " + next.ToString().ToUpperInvariant();
            statusColor = AccentColor;
        }

        private void CheckUpdatesNow(bool force)
        {
            isBusy = true;
            currentProgress = 100;
            currentStepText = "Sprawdzanie serwera pakietow...";

            UpdatePackageInfo pkg;
            string msg;
            bool found = UpdateManager.CheckForUpdates(force, out pkg, out msg);

            isBusy = false;
            statusMessage = msg;
            statusColor = found ? GreenColor : MutedColor;
        }

        private void PerformInstall()
        {
            UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
            if (pkg == null) return;

            isBusy = true;
            string error;
            bool success = UpdateManager.ApplyUpdate(pkg, (pct, txt) =>
            {
                currentProgress = pct;
                currentStepText = txt;
            }, out error);

            isBusy = false;
            if (success)
            {
                statusMessage = "Zaktualizowano pomyslnie do " + pkg.Version + "! Wymagany restart.";
                statusColor = GreenColor;
            }
            else
            {
                statusMessage = "Blad instalacji: " + error;
                statusColor = RedColor;
            }
        }

        private void PerformRollback()
        {
            if (!UpdateManager.HasRollbackSnapshot())
            {
                statusMessage = "Brak dostepnej migawki do wycofania zmian.";
                statusColor = RedColor;
                return;
            }

            isBusy = true;
            string error;
            bool success = UpdateManager.Rollback((pct, txt) =>
            {
                currentProgress = pct;
                currentStepText = txt;
            }, out error);

            isBusy = false;
            if (success)
            {
                statusMessage = "Przywrocono poprzedni stan systemu. Wymagany restart.";
                statusColor = GoldColor;
            }
            else
            {
                statusMessage = "Blad wycofywania: " + error;
                statusColor = RedColor;
            }
        }

        private void PerformReboot()
        {
            statusMessage = "Restartowanie systemu ZOnderqOS...";
            statusColor = GoldColor;
            Cosmos.Kernel.System.Power.Reboot();
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);

            int wx = Window.X;
            int wy = Window.Y + HeaderHeight;
            int ww = Window.Width;

            // Background
            canvas.DrawFilledRectangle(Surface, wx, wy, ww, Window.Height - HeaderHeight);

            // 1. Top Banner Card (wy + 8, h: 62)
            RenderTopBanner(canvas, wx + 12, wy + 8, ww - 24, 62);

            // 2. Channel & Actions Row (wy + 76, h: 26)
            RenderChannelRow(canvas, wx + 12, wy + 76, ww - 24, 26);

            // 3. Action / Alert Banner (wy + 108, h: 36)
            RenderActionBanner(canvas, wx + 12, wy + 108, ww - 24, 36);

            // 4. Tabs (wy + 150, h: 26)
            RenderTabs(canvas, wx + 12, wy + 150, ww - 24, 26);

            // 5. Active Tab Content (wy + 180, h: 218)
            RenderTabContent(canvas, wx + 12, wy + 180, ww - 24, 218);

            // 6. Footer (wy + 404, h: 26)
            UiPainter.RoundedPanel(canvas, wx + 12, wy + 404, ww - 24, 26, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, statusMessage, wx + 20, wy + 411, statusColor);
        }

        private void RenderTopBanner(Canvas canvas, int x, int y, int w, int h)
        {
            Color bannerBg = Panel;
            Color bannerBorder = Border;
            string title = "System ZonderqOS jest aktualny";
            Color titleCol = GreenColor;
            string subtitle = "Biezaca wersja: " + UpdateManager.CurrentVersion + " (Build " + UpdateManager.CurrentBuild + ") | Kernel Cosmos 3.0.95";

            if (UpdateManager.IsRebootRequired)
            {
                title = "Wymagany restart komputera";
                titleCol = GoldColor;
                subtitle = "Nowe moduly i pliki systemowe oczekuja na zaladowanie.";
                bannerBorder = GoldColor;
            }
            else if (UpdateManager.Status == UpdateStatus.Available && UpdateManager.AvailablePackage != null)
            {
                title = "Dostepna nowa aktualizacja: " + UpdateManager.AvailablePackage.Version;
                titleCol = AccentColor;
                subtitle = UpdateManager.AvailablePackage.Title + " (" + UpdateManager.AvailablePackage.Severity + ")";
                bannerBorder = AccentColor;
            }

            UiPainter.RoundedPanel(canvas, x, y, w, h, 6, bannerBg, bannerBorder);

            // Status Icon Circle
            int iconX = x + 12;
            int iconY = y + 15;
            canvas.DrawFilledRectangle(titleCol, iconX, iconY, 32, 32);
            SmallTextRenderer.DrawCentered(canvas, UpdateManager.IsRebootRequired ? "!" : (UpdateManager.Status == UpdateStatus.Available ? "↓" : "✓"),
                iconX, iconY + 8, 32, Color.Black);

            // Text
            SmallTextRenderer.Draw(canvas, title, x + 54, y + 14, titleCol);
            SmallTextRenderer.Draw(canvas, subtitle, x + 54, y + 34, MutedColor);
        }

        private void RenderChannelRow(Canvas canvas, int x, int y, int w, int h)
        {
            // Channel Pill Button
            string chText = "KANAL: " + UpdateManager.Channel.ToString().ToUpperInvariant();
            DrawButton(canvas, chText, x + 4, y, 120, h, Panel, Border, AccentColor);

            // Last Check Info
            string checkInfo = "Ostatnie sprawdzenie: " + UpdateManager.LastCheckTime;
            SmallTextRenderer.Draw(canvas, checkInfo, x + 134, y + 7, MutedColor);

            // [SPRAWDŹ TERAZ] Button
            DrawButton(canvas, "SPRAWDZ TERAZ", x + w - 245, y, 115, h, AccentColor, AccentColor, TextColor);

            // [PRZYWRÓĆ] Button
            bool hasBackup = UpdateManager.HasRollbackSnapshot();
            DrawButton(canvas, "PRZYWROC MIGAWE", x + w - 124, y, 120, h, hasBackup ? RedColor : Panel, Border, hasBackup ? TextColor : MutedColor);
        }

        private void RenderActionBanner(Canvas canvas, int x, int y, int w, int h)
        {
            if (isBusy)
            {
                // Progress view
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
                SmallTextRenderer.Draw(canvas, currentStepText + " (" + currentProgress + "%)", x + 12, y + 5, TextColor);

                // Progress Bar
                int barX = x + 12;
                int barY = y + 20;
                int barW = w - 24;
                int barH = 8;
                canvas.DrawFilledRectangle(Surface, barX, barY, barW, barH);
                int fillW = (barW * currentProgress) / 100;
                if (fillW > 0)
                {
                    canvas.DrawFilledRectangle(AccentColor, barX, barY, fillW, barH);
                }
                return;
            }

            if (UpdateManager.Status == UpdateStatus.Available && UpdateManager.AvailablePackage != null)
            {
                UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, AccentColor);
                SmallTextRenderer.Draw(canvas, "Pobrano metadane: " + pkg.Version + " - " + pkg.Title, x + 12, y + 11, TextColor);
                DrawButton(canvas, "ZAINSTALUJ TERAZ", x + w - 158, y + 4, 150, 28, AccentColor, AccentColor, TextColor);
            }
            else if (UpdateManager.IsRebootRequired)
            {
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, GoldColor);
                SmallTextRenderer.Draw(canvas, "Wszystkie komponenty zostaly zaktualizowane. Wymagany restart.", x + 12, y + 11, GoldColor);
                DrawButton(canvas, "ZRESTARTUJ TERAZ", x + w - 158, y + 4, 150, 28, GoldColor, GoldColor, Color.Black);
            }
            else
            {
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
                SmallTextRenderer.Draw(canvas, "Brak oczekujacych aktualizacji. Wciskaj F5 lub [SPRAWDZ TERAZ] aby odswiezyc.", x + 12, y + 11, MutedColor);
            }
        }

        private void RenderTabs(Canvas canvas, int x, int y, int w, int h)
        {
            string[] tabs = new string[] { "DZIENNIK ZMIAN", "PLIKI I LADUNEK", "NOSNIKI I USB", "HISTORIA I MIGAWKI" };
            int tabW = (w - 18) / 4;

            for (int i = 0; i < 4; i++)
            {
                int tx = x + i * (tabW + 6);
                bool isSel = (i == activeTab);
                Color fill = isSel ? AccentColor : Panel;
                Color txt = isSel ? TextColor : MutedColor;

                UiPainter.RoundedPanel(canvas, tx, y, tabW, h, 4, fill, Border);
                SmallTextRenderer.DrawCentered(canvas, tabs[i], tx, y + 7, tabW, txt);
            }
        }

        private void RenderTabContent(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);

            if (activeTab == 0)
            {
                // Tab 0: Changelog
                UpdatePackageInfo pkg = UpdateManager.AvailablePackage ?? new UpdatePackageInfo();
                SmallTextRenderer.Draw(canvas, "Wydanie docelowe: " + pkg.Version + " - " + pkg.Title, x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Data wydania: " + pkg.ReleaseDate + " | Waznosc: " + pkg.Severity + " | Motyw: " + pkg.TargetThemeName, x + 14, y + 26, MutedColor);

                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                int lineY = y + 50;
                for (int i = 0; i < pkg.Changelog.Length && i < 6; i++)
                {
                    canvas.DrawFilledRectangle(AccentColor, x + 16, lineY + 4, 6, 6);
                    SmallTextRenderer.Draw(canvas, pkg.Changelog[i], x + 30, lineY, TextColor);
                    lineY += 23;
                }

                SmallTextRenderer.Draw(canvas, "Suma kontrolna SHA-256: " + pkg.ExpectedSha256, x + 14, y + h - 20, MutedColor);
            }
            else if (activeTab == 1)
            {
                // Tab 1: Components and Payload Files
                UpdatePackageInfo pkg = UpdateManager.AvailablePackage ?? new UpdatePackageInfo();
                string payloadDir = UpdateManager.FindPayloadDirectory(pkg);
                string pDirText = string.IsNullOrEmpty(payloadDir) ? "Brak fizycznego katalogu (instalacja ze standardowej paczki)" : payloadDir;

                SmallTextRenderer.Draw(canvas, "Komponenty systemowe i ladunek aktualizacji:", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Lokalizacja ladunku: " + pDirText, x + 14, y + 26, MutedColor);
                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                int lineY = y + 50;
                for (int i = 0; i < pkg.Components.Length && i < 5; i++)
                {
                    canvas.DrawFilledRectangle(GreenColor, x + 16, lineY + 4, 6, 6);
                    SmallTextRenderer.Draw(canvas, pkg.Components[i], x + 30, lineY, TextColor);
                    lineY += 22;
                }

                lineY += 6;
                canvas.DrawFilledRectangle(GoldColor, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Aplikacje w wydaniu: Chiptune Studio, Szybkie Notatki, PassVault v2", x + 30, lineY, GoldColor);
                lineY += 22;

                canvas.DrawFilledRectangle(AccentColor, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Konfiguracja wygladu: Automatyczny przelacznik motywu (Accent & Wallpaper)", x + 30, lineY, AccentColor);
            }
            else if (activeTab == 2)
            {
                // Tab 2: Storage & USB Environment
                StorageEnvironmentInfo env = UpdateManager.GetStorageEnvironmentInfo();
                SmallTextRenderer.Draw(canvas, "Stan nosnikow i partycji ZonderqOS:", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Partycja glowna: " + env.PartitionLabel + " (zamontowana jako '/') | Typ: " + env.DeviceType, x + 14, y + 26, MutedColor);
                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                int lineY = y + 50;

                // Status 1: Updates directory
                Color c1 = env.HasUpdatesDir ? GreenColor : MutedColor;
                canvas.DrawFilledRectangle(c1, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Katalog /updates na partycji USB: " + (env.HasUpdatesDir ? "DOSTEPNY" : "NIEWYKRYTY (utworz folder /updates)"), x + 30, lineY, TextColor);
                lineY += 23;

                // Status 2: Manifest file
                Color c2 = env.HasManifest ? GreenColor : GoldColor;
                canvas.DrawFilledRectangle(c2, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Plik manifestu (update.manifest): " + (env.HasManifest ? "WYKRYTO (" + env.ManifestPath + ")" : "Brak (uzywany kanal online/offline)"), x + 30, lineY, TextColor);
                lineY += 23;

                // Status 3: Payload directory
                Color c3 = env.HasPayload ? GreenColor : MutedColor;
                canvas.DrawFilledRectangle(c3, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Katalog plikow ladunku (/payload): " + (env.HasPayload ? "WYKRYTO (" + env.PayloadPath + ", " + env.PayloadFileCount + " plikow)" : "Brak oddzielnego katalogu"), x + 30, lineY, TextColor);
                lineY += 23;

                // Status 4: Kernel / ISO Image
                Color c4 = env.HasKernelIso ? GreenColor : MutedColor;
                canvas.DrawFilledRectangle(c4, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Obraz rozruchowy jadra (update.iso): " + (env.HasKernelIso ? "WYKRYTO (" + env.KernelIsoPath + ")" : "Brak obrazu jądra (aktualizacja live)"), x + 30, lineY, TextColor);
                lineY += 26;

                // Info footer
                SmallTextRenderer.Draw(canvas, "Wskazowka: Wrzucenie pliku update.manifest na USB automatycznie aktywuje instalacje.", x + 14, y + h - 20, AccentColor);
            }
            else if (activeTab == 3)
            {
                // Tab 3: History & Snapshots
                SmallTextRenderer.Draw(canvas, "Historia instalacji i wycofywania aktualizacji (/var/log/update.log):", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Punkt przywrocenia: " + (UpdateManager.HasRollbackSnapshot() ? "AKTYWNY (Zapisano migawke /var/updates/backup)" : "BRAK"), x + 14, y + 26, MutedColor);
                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                List<string> history = UpdateManager.GetHistory();
                if (history.Count == 0)
                {
                    SmallTextRenderer.Draw(canvas, "Brak zarejestrowanych wpisow historii aktualizacji w systemie.", x + 16, y + 60, MutedColor);
                }
                else
                {
                    int lineY = y + 50;
                    int start = Math.Max(0, history.Count - 6);
                    for (int i = start; i < history.Count; i++)
                    {
                        Color lineCol = history[i].Contains("ROLLBACK") ? GoldColor : TextColor;
                        SmallTextRenderer.Draw(canvas, history[i], x + 16, lineY, lineCol);
                        lineY += 22;
                    }
                }
            }
        }

        private static void DrawButton(Canvas canvas, string text, int x, int y, int w, int h, Color fill, Color border, Color textColor)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, fill, border);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h - 10) / 2, w, textColor);
        }
    }
}
