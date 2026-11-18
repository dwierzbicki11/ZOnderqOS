using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Security;
using ZonderqOS.SystemCore;
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
        private int activeTab = 0; // 0 = Changelog, 1 = Files/Payload, 2 = Storage/Slots, 3 = History
        private readonly Action closeCallback;

        private string statusMessage = "Ready to install an update from USB.";
        private Color statusColor = MutedColor;

        private enum UpdateOp { None, Checking, Installing, RollingBack }
        private UpdateOp currentOp = UpdateOp.None;
        private int currentOpStep = 0;

        private bool isBusy = false;
        private int currentProgress = 0;
        private string currentStepText = string.Empty;

        private static readonly string[] TabLabels = { "CHANGELOG", "FILES AND PAYLOAD", "A/B SLOTS AND USB", "HISTORY AND SNAPSHOTS" };
        private static readonly UpdatePackageInfo EmptyPackage = new UpdatePackageInfo();

        private StorageEnvironmentInfo cachedEnvInfo = new StorageEnvironmentInfo();
        private List<string> cachedHistory = new List<string>();
        private bool cachedHasRollback = false;
        private string cachedPayloadDir = string.Empty;

        public SystemUpdaterApp(int x, int y, Action onClose) : base("System Updates")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 620, 440, "ZonderqOS Update Center");
            Window.CloseAction = Close;

            UpdateManager.Initialize();
            BootSlotManager.Initialize();
            RefreshCachedState();
        }

        private void RefreshCachedState()
        {
            UpdatePackageInfo pkg = UpdateManager.AvailablePackage ?? EmptyPackage;
            cachedPayloadDir = UpdateManager.FindPayloadDirectory(pkg);
            cachedEnvInfo = UpdateManager.GetStorageEnvironmentInfo();
            cachedHistory = UpdateManager.GetHistory();
            cachedHasRollback = UpdateManager.HasRollbackSnapshot();
        }

        public override void Close()
        {
            isBusy = false;
            currentOp = UpdateOp.None;
            currentOpStep = 0;
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            base.HandleKeyboard(key);
            if (key == null) return;

            if (isBusy)
            {
                if (key.Key == ConsoleKeyEx.Escape || key.Key == ConsoleKeyEx.F5)
                {
                    statusMessage = "Update in progress - please wait for completion.";
                    statusColor = GoldColor;
                }
                return;
            }

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

                // Tab switching remains responsive even while an operation runs.
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

                if (isBusy)
                {
                    statusMessage = "Update in progress - please wait for completion.";
                    statusColor = GoldColor;
                    return;
                }

                // 1. Channel Selector Pill (wx + 12, wy + 76, 105, 24)
                if (Hit(mouseX, mouseY, wx + 12, wy + 76, 105, 24))
                {
                    CycleChannel();
                    return;
                }

                // 1b. Source Selector Pill (wx + 122, wy + 76, 115, 24)
                if (Hit(mouseX, mouseY, wx + 122, wy + 76, 115, 24))
                {
                    CycleSourceMode();
                    return;
                }

                // 2. [SPRAWDŹ USB] (wx + 360, wy + 76, 115, 24)
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
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private static bool IsRootAuthorized()
        {
            return SecurityContext.IsAuthenticated && SecurityContext.CurrentUid == 0;
        }

        private bool RequireRootAccess(string actionDescription)
        {
            if (IsRootAuthorized())
                return true;

            statusMessage = "Permission denied: administrator (root) privileges required.";
            statusColor = RedColor;
            SecurityLogger.LogEvent("WARN",
                "Unauthorized SystemUpdater action (" + actionDescription + ") by " +
                (SecurityContext.CurrentUser ?? "?"));
            return false;
        }

        private void CycleChannel()
        {
            if (isBusy || !RequireRootAccess("change-channel"))
                return;

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

        private void CycleSourceMode()
        {
            if (isBusy || !RequireRootAccess("change-source"))
                return;

            UpdateSourceMode cur = UpdateManager.SourceMode;
            UpdateSourceMode next = cur switch
            {
                UpdateSourceMode.UsbOffline => UpdateSourceMode.CloudAuto,
                UpdateSourceMode.CloudAuto => UpdateSourceMode.CloudOnly,
                _ => UpdateSourceMode.UsbOffline
            };

            UpdateManager.SetSourceMode(next);
            statusMessage = "Update source: " + next.ToString().ToUpperInvariant();
            statusColor = AccentColor;
        }

        public override bool NeedsContinuousRedraw => isBusy;

        public override void Update()
        {
            base.Update();

            if (!isBusy || currentOp == UpdateOp.None)
                return;

            if (currentOp == UpdateOp.Checking)
            {
                UpdatePackageInfo pkg;
                string msg;
                bool found = UpdateManager.CheckForUpdates(true, out pkg, out msg);
                isBusy = false;
                currentOp = UpdateOp.None;
                statusMessage = msg;
                statusColor = found ? GreenColor : MutedColor;
                RefreshCachedState();
                return;
            }

            if (currentOp == UpdateOp.Installing)
            {
                if (!IsRootAuthorized())
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = "Permission denied: administrator (root) privileges required.";
                    statusColor = RedColor;
                    return;
                }

                UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
                if (pkg == null)
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = "No update package selected.";
                    statusColor = RedColor;
                    return;
                }

                string err;
                bool stepResult = UpdateManager.ApplyUpdateStep(pkg, ref currentOpStep, (pct, txt) =>
                {
                    currentProgress = pct;
                    currentStepText = txt;
                    GuiManager.PumpFrame();
                }, out err);

                if (!stepResult)
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = "Installation error: " + err;
                    statusColor = RedColor;
                    RefreshCachedState();
                }
                else if (currentOpStep == -1)
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = $"Installed successfully to slot {BootSlotManager.ActiveSlot}! Restart required.";
                    statusColor = GreenColor;
                    RefreshCachedState();
                }
                return;
            }

            if (currentOp == UpdateOp.RollingBack)
            {
                if (!IsRootAuthorized())
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = "Permission denied: administrator (root) privileges required.";
                    statusColor = RedColor;
                    return;
                }

                string err;
                bool stepResult = UpdateManager.RollbackStep(ref currentOpStep, (pct, txt) =>
                {
                    currentProgress = pct;
                    currentStepText = txt;
                    GuiManager.PumpFrame();
                }, out err);

                if (!stepResult)
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = "Rollback error: " + err;
                    statusColor = RedColor;
                    RefreshCachedState();
                }
                else if (currentOpStep == -1)
                {
                    isBusy = false;
                    currentOp = UpdateOp.None;
                    statusMessage = $"Restored slot {BootSlotManager.ActiveSlot} and system state. Restart required.";
                    statusColor = GoldColor;
                    RefreshCachedState();
                }
                return;
            }
        }

        private void CheckUpdatesNow(bool force)
        {
            if (isBusy)
                return;

            isBusy = true;
            currentOp = UpdateOp.Checking;
            currentProgress = 100;
            currentStepText = "Scanning USB drive for update packages...";
            statusMessage = "Checking USB for updates...";
            statusColor = AccentColor;
        }

        private void PerformInstall()
        {
            if (isBusy || !RequireRootAccess("install"))
                return;

            UpdatePackageInfo pkg = UpdateManager.AvailablePackage;
            if (pkg == null) return;

            isBusy = true;
            currentOp = UpdateOp.Installing;
            currentOpStep = 0;
            currentProgress = 5;
            currentStepText = "Initializing update installer...";
            statusMessage = "Installing update package...";
            statusColor = AccentColor;
        }

        private void PerformRollback()
        {
            if (isBusy || !RequireRootAccess("rollback"))
                return;

            if (!UpdateManager.HasRollbackSnapshot())
            {
                statusMessage = "No rollback snapshot is available.";
                statusColor = RedColor;
                return;
            }

            isBusy = true;
            currentOp = UpdateOp.RollingBack;
            currentOpStep = 0;
            currentProgress = 10;
            currentStepText = "Initializing rollback...";
            statusMessage = "Restoring system snapshot...";
            statusColor = GoldColor;
        }

        private void PerformReboot()
        {
            if (isBusy || !RequireRootAccess("reboot"))
                return;

            statusMessage = "Restarting ZOnderqOS...";
            statusColor = GoldColor;
            global::ZonderqOS.SystemCore.SystemPower.Reboot("gui-update-reboot");
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
            string title = "ZonderqOS kernel is up to date";
            Color titleCol = GreenColor;
            string subtitle = $"Kernel: {KernelBuildInfo.KernelVersion} (Build {KernelBuildInfo.KernelBuild}) | Slot: {BootSlotManager.ActiveSlot} | Cosmos 3.0.95";

            if (UpdateManager.IsRebootRequired)
            {
                title = "Restart required";
                titleCol = GoldColor;
                subtitle = $"New kernel is ready in slot {BootSlotManager.ActiveSlot}. Restart the system.";
                bannerBorder = GoldColor;
            }
            else if (UpdateManager.Status == UpdateStatus.Available && UpdateManager.AvailablePackage != null)
            {
                title = "USB update available: " + UpdateManager.AvailablePackage.Version;
                titleCol = AccentColor;
                subtitle = UpdateManager.AvailablePackage.Title + " (" + UpdateManager.AvailablePackage.Severity + ")";
                bannerBorder = AccentColor;
            }
            else if (!string.Equals(KernelBuildInfo.KernelVersion, UpdateManager.CurrentVersion, StringComparison.OrdinalIgnoreCase))
            {
                title = "Kernel and disk image mismatch detected!";
                titleCol = GoldColor;
                subtitle = $"Running: {KernelBuildInfo.KernelVersion} | On disk: {UpdateManager.CurrentVersion}. Restart required.";
                bannerBorder = GoldColor;
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
            string chText = "KANAL: " + UpdateManager.Channel.ToString().ToUpperInvariant();
            DrawButton(canvas, chText, x, y, 105, h, Panel, Border, AccentColor);

            string srcText = UpdateManager.SourceMode switch
            {
                UpdateSourceMode.CloudOnly => "CLOUD (OTA)",
                UpdateSourceMode.CloudAuto => "AUTO (USB/CLOUD)",
                _ => "USB (OFFLINE)"
            };
            DrawButton(canvas, srcText, x + 110, y, 115, h, Panel, Border, GoldColor);

            DrawButton(canvas, "CHECK USB", x + w - 245, y, 115, h, AccentColor, AccentColor, TextColor);

            bool hasBackup = cachedHasRollback;
            DrawButton(canvas, "RESTORE SNAPSHOT", x + w - 124, y, 120, h, hasBackup ? RedColor : Panel, Border, hasBackup ? TextColor : MutedColor);
        }

        private void RenderActionBanner(Canvas canvas, int x, int y, int w, int h)
        {
            if (isBusy)
            {
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
                SmallTextRenderer.Draw(canvas, currentStepText + " (" + currentProgress + "%)", x + 12, y + 5, TextColor);

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
                string signText = pkg.IsSigned ? (pkg.IsSignatureValid ? " [SIGNATURE OK]" : " [INVALID SIGNATURE]") : "";
                SmallTextRenderer.Draw(canvas, "USB package: " + pkg.Version + " - " + pkg.Title + signText, x + 12, y + 11, TextColor);
                DrawButton(canvas, "INSTALL NOW", x + w - 158, y + 4, 150, 28, AccentColor, AccentColor, TextColor);
            }
            else if (UpdateManager.IsRebootRequired)
            {
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, GoldColor);
                SmallTextRenderer.Draw(canvas, "Update installed. Restart will load the new kernel from slot " + BootSlotManager.ActiveSlot, x + 12, y + 11, GoldColor);
                DrawButton(canvas, "RESTART NOW", x + w - 158, y + 4, 150, 28, GoldColor, GoldColor, Color.Black);
            }
            else
            {
                UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);
                SmallTextRenderer.Draw(canvas, "No pending updates. Press F5 or [CHECK USB] to refresh.", x + 12, y + 11, MutedColor);
            }
        }

        private void RenderTabs(Canvas canvas, int x, int y, int w, int h)
        {
            int tabW = (w - 18) / 4;

            for (int i = 0; i < 4; i++)
            {
                int tx = x + i * (tabW + 6);
                bool isSel = (i == activeTab);
                Color fill = isSel ? AccentColor : Panel;
                Color txt = isSel ? TextColor : MutedColor;

                UiPainter.RoundedPanel(canvas, tx, y, tabW, h, 4, fill, Border);
                SmallTextRenderer.DrawCentered(canvas, TabLabels[i], tx, y + 7, tabW, txt);
            }
        }

        private void RenderTabContent(Canvas canvas, int x, int y, int w, int h)
        {
            UiPainter.RoundedPanel(canvas, x, y, w, h, 4, Panel, Border);

            if (activeTab == 0)
            {
                // Tab 0: Changelog
                UpdatePackageInfo pkg = UpdateManager.AvailablePackage ?? EmptyPackage;
                SmallTextRenderer.Draw(canvas, "Package version: " + pkg.Version + " - " + pkg.Title, x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Data wydania: " + pkg.ReleaseDate + " | Waznosc: " + pkg.Severity + " | Motyw: " + pkg.TargetThemeName, x + 14, y + 26, MutedColor);

                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                int lineY = y + 50;
                for (int i = 0; i < pkg.Changelog.Length && i < 6; i++)
                {
                    canvas.DrawFilledRectangle(AccentColor, x + 16, lineY + 4, 6, 6);
                    SmallTextRenderer.Draw(canvas, pkg.Changelog[i], x + 30, lineY, TextColor);
                    lineY += 23;
                }

                string hashDisplay = string.IsNullOrEmpty(pkg.KernelSha256) ? (string.IsNullOrEmpty(pkg.ExpectedSha256) ? "No checksum" : pkg.ExpectedSha256) : pkg.KernelSha256;
                SmallTextRenderer.Draw(canvas, "Kernel SHA-256: " + hashDisplay, x + 14, y + h - 20, MutedColor);
            }
            else if (activeTab == 1)
            {
                // Tab 1: Components and Payload Files
                UpdatePackageInfo pkg = UpdateManager.AvailablePackage ?? EmptyPackage;
                string payloadDir = cachedPayloadDir;
                string pDirText = string.IsNullOrEmpty(payloadDir) ? "Payload directory is missing" : payloadDir;

                SmallTextRenderer.Draw(canvas, "System components and update payload:", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Payload directory: " + pDirText + " | Kernel: " + (string.IsNullOrEmpty(pkg.KernelImagePath) ? "Missing" : pkg.KernelImagePath), x + 14, y + 26, MutedColor);
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
                string signDesc = pkg.IsSigned ? (pkg.IsSignatureValid ? "SHA-256 / Ed25519 signature: VERIFIED" : "Digital signature: INVALID!") : "Digital signature: Missing (unsigned release)";
                SmallTextRenderer.Draw(canvas, signDesc, x + 30, lineY, pkg.IsSigned && pkg.IsSignatureValid ? GreenColor : GoldColor);
                lineY += 22;

                canvas.DrawFilledRectangle(AccentColor, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, "Installing to slot: " + BootSlotManager.GetInactiveSlot() + " while preserving the previous version.", x + 30, lineY, AccentColor);
            }
            else if (activeTab == 2)
            {
                // Tab 2: Dual Boot Slots A/B & Storage Environment
                SmallTextRenderer.Draw(canvas, "A/B boot layout and Limine slot diagnostics:", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, $"Active: Slot {BootSlotManager.ActiveSlot} | Fallback: Slot {BootSlotManager.FallbackSlot} | Status: {(KernelBuildInfo.IsHealthyAndConfirmed ? "CONFIRMED (HEALTHY)" : "OCZEKUJE")}", x + 14, y + 26, MutedColor);
                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                int lineY = y + 50;

                // Slot A
                Color ca = BootSlotManager.ActiveSlot == BootSlotManager.SlotA ? GreenColor : MutedColor;
                canvas.DrawFilledRectangle(ca, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, $"SLOT A: {BootSlotManager.SlotAPath} [Version: {BootSlotManager.SlotAVersion}, Status: {BootSlotManager.SlotAStatus}]", x + 30, lineY, TextColor);
                lineY += 23;

                // Slot B
                Color cb = BootSlotManager.ActiveSlot == BootSlotManager.SlotB ? GreenColor : MutedColor;
                canvas.DrawFilledRectangle(cb, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, $"SLOT B: {BootSlotManager.SlotBPath} [Version: {BootSlotManager.SlotBVersion}, Status: {BootSlotManager.SlotBStatus}]", x + 30, lineY, TextColor);
                lineY += 23;

                // Watchdog state
                canvas.DrawFilledRectangle(AccentColor, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, $"Boot watchdog: Attempts: {BootSlotManager.BootAttempts}/{BootSlotManager.MaxBootAttempts} (automatic failover after 3 failed boots)", x + 30, lineY, TextColor);
                lineY += 23;

                // Storage environment
                StorageEnvironmentInfo env = cachedEnvInfo;
                Color c4 = env.HasUpdatesDir ? GreenColor : GoldColor;
                canvas.DrawFilledRectangle(c4, x + 16, lineY + 4, 6, 6);
                SmallTextRenderer.Draw(canvas, $"Folder /updates na USB: {(env.HasUpdatesDir ? "AVAILABLE" : "MISSING")} | Manifest: {(env.HasManifest ? "DETECTED" : "MISSING")} | Kernel: {(env.HasKernelBinary ? "DETECTED" : "MISSING")}", x + 30, lineY, TextColor);
                lineY += 26;

                SmallTextRenderer.Draw(canvas, "Target slot for next update: Slot " + BootSlotManager.GetInactiveSlot(), x + 14, y + h - 20, AccentColor);
            }
            else if (activeTab == 3)
            {
                // Tab 3: History & Snapshots
                SmallTextRenderer.Draw(canvas, "Update and rollback history (/var/log/update.log):", x + 14, y + 10, AccentColor);
                SmallTextRenderer.Draw(canvas, "Restore point: " + (cachedHasRollback ? "ACTIVE (snapshot saved to /var/updates/backup)" : "MISSING"), x + 14, y + 26, MutedColor);
                canvas.DrawLine(Border, x + 14, y + 42, x + w - 14, y + 42);

                List<string> history = cachedHistory;
                if (history.Count == 0)
                {
                    SmallTextRenderer.Draw(canvas, "No update history entries have been recorded.", x + 16, y + 60, MutedColor);
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
