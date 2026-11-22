using System;
using System.Collections.Generic;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Input;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Storage;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Complete 7-screen graphical OS installer for ZonderqOS Gen3.
    /// Uses fixed buffers and cached hardware/storage snapshots so rendering
    /// stays allocation-free in the GUI loop.
    /// </summary>
    public sealed class ZonderqInstallerApp : Application
    {
        public const int ScreenWelcome = 0;
        public const int ScreenRequirements = 1;
        public const int ScreenSelectDisk = 2;
        public const int ScreenInstallType = 3;
        public const int ScreenUserSetup = 4;
        public const int ScreenProgress = 5;
        public const int ScreenComplete = 6;

        private const int MaxVisibleDisks = 4;
        private const int MaxTextFieldChars = 32;
        private const int MaxPasswordChars = PasswordPolicy.MaxLength;
        private const string PasswordMask =
            "********************************************************************************************************************************";

        private static readonly string[] StepLabels =
        {
            "1. WELCOME",
            "2. HARDWARE",
            "3. TARGET DISK",
            "4. INSTALL TYPE",
            "5. USER ACCOUNT",
            "6. INSTALLING",
            "7. COMPLETE"
        };

        private static readonly Color Surface = Color.FromArgb(16, 22, 29);
        private static readonly Color HeaderBg = Color.FromArgb(22, 30, 39);
        private static readonly Color Panel = Color.FromArgb(25, 34, 44);
        private static readonly Color PanelAlt = Color.FromArgb(30, 41, 53);
        private static readonly Color PanelHover = Color.FromArgb(37, 51, 66);
        private static readonly Color PanelSelected = Color.FromArgb(34, 62, 86);
        private static readonly Color Border = Color.FromArgb(52, 68, 84);
        private static readonly Color Accent = Color.FromArgb(42, 148, 238);
        private static readonly Color TextColor = Color.FromArgb(234, 240, 246);
        private static readonly Color Muted = Color.FromArgb(136, 154, 170);
        private static readonly Color Good = Color.FromArgb(72, 192, 124);
        private static readonly Color Warning = Color.FromArgb(230, 176, 68);
        private static readonly Color Danger = Color.FromArgb(220, 82, 88);
        private static readonly Color DangerBg = Color.FromArgb(46, 22, 26);

        private readonly Action closeCallback;
        private readonly InstallerStepTracker stepTracker = new InstallerStepTracker();

        // Hardware requirements snapshot (Screen 2)
        private string cpuArchText = "Unknown";
        private string cpuBrandText = "Unknown";
        private string ramText = "Unknown";
        private string firmwareModeText = "Unknown";
        private string disksSummaryText = "0 detected";
        private string compatibilityText = "Checking...";
        private bool hardwareCompatible;

        // Discovered physical disks snapshot (Screen 3)
        private DiskCatalogEntry[] disks = Array.Empty<DiskCatalogEntry>();
        private int selectedDiskIndex = -1; // Never auto-select a disk for formatting
        private int diskScrollOffset;

        // Installation type & explicit Erase Disk confirmation modal (Screen 4)
        private int installModeIndex; // 0 = Erase Disk and Install (Automatic), 1 = Advanced Installation, 2 = Dual-Boot Alongside Existing OS
        private InstallationPartitionLayout chosenLayout = InstallationPartitionLayout.DualEfiAndSystem;
        private DualBootDiskInspection currentDualBootInfo;
        private int dualBootCandidateIndex = -1; // -1 = Use Free Unallocated Space, 0..N-1 = CandidateTargetPartitions[i]
        private ulong chosenDualBootPartitionLba;
        private bool zeroFillExtendedHeader;
        private bool showEraseWarningModal;
        private readonly char[] confirmDiskIdChars = new char[MaxTextFieldChars];
        private int confirmDiskIdLength;
        private string cachedConfirmDiskIdText = string.Empty;

        // User Setup buffers (Screen 5) — passwords live in fixed char[] and are cleared after install
        private readonly char[] usernameChars = new char[MaxTextFieldChars];
        private int usernameLength;
        private string cachedUsernameText = string.Empty;
        private readonly char[] hostnameChars = new char[MaxTextFieldChars];
        private int hostnameLength;
        private string cachedHostnameText = string.Empty;
        private readonly char[] passwordChars = new char[MaxPasswordChars];
        private int passwordLength;
        private readonly char[] confirmPasswordChars = new char[MaxPasswordChars];
        private int confirmPasswordLength;
        private int activeUserField; // 0=username, 1=hostname, 2=password, 3=confirmPassword

        private int currentScreen = ScreenWelcome;
        private bool installationStarted;
        private string statusMessage = "Welcome to ZonderqOS Setup.";
        private Color statusColor = Accent;
        private string installedDiskSummary = string.Empty;
        private string installedUserSummary = string.Empty;

        public ZonderqInstallerApp(int x, int y, Action onClose)
            : base("ZonderqOS Installer")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 960, 640, "ZonderqOS Installer");
            Window.CloseAction = Close;

            SetDefaultHostname("zonderq-pc");
            RefreshHardwareAndDisks();
        }

        public override void Close()
        {
            ClearSensitiveBuffers();
            base.Close();
            closeCallback?.Invoke();
        }

        private void ClearSensitiveBuffers()
        {
            Array.Clear(passwordChars, 0, passwordChars.Length);
            Array.Clear(confirmPasswordChars, 0, confirmPasswordChars.Length);
            passwordLength = 0;
            confirmPasswordLength = 0;
            Array.Clear(confirmDiskIdChars, 0, confirmDiskIdChars.Length);
            confirmDiskIdLength = 0;
            cachedConfirmDiskIdText = string.Empty;
        }

        private void SetDefaultHostname(string value)
        {
            Array.Clear(hostnameChars, 0, hostnameChars.Length);
            hostnameLength = 0;
            cachedHostnameText = string.Empty;
            if (string.IsNullOrEmpty(value))
                return;

            int len = Math.Min(value.Length, MaxTextFieldChars);
            for (int i = 0; i < len; i++)
                hostnameChars[i] = value[i];
            hostnameLength = len;
            cachedHostnameText = new string(hostnameChars, 0, hostnameLength);
        }

        public void RefreshHardwareAndDisks()
        {
            try
            {
                HardwareSnapshot hw = HardwareSnapshot.Capture();
                cpuArchText = string.IsNullOrWhiteSpace(hw.Architecture) || hw.Architecture == "N/A"
                    ? "x86_64"
                    : hw.Architecture;
                cpuBrandText = string.IsNullOrWhiteSpace(hw.CpuBrand) ? "Unknown" : hw.CpuBrand;

                ulong memBytes = hw.TotalMemoryBytes > 0 ? hw.TotalMemoryBytes : hw.HeapMemoryBytes;
                if (memBytes > 0)
                {
                    ulong memMb = memBytes / (1024UL * 1024UL);
                    ramText = memMb + " MB available (Minimum: 512 MB)";
                }
                else
                {
                    ramText = "Unknown";
                }

                firmwareModeText = LimineBootMediaDetector.FirmwareModeName;

                List<DiskCatalogEntry> list = DiskCatalog.DiscoverPhysicalDisks();
                disks = list.ToArray();

                int eligibleCount = 0;
                for (int i = 0; i < disks.Length; i++)
                {
                    if (disks[i].CanInstall)
                        eligibleCount++;
                }

                disksSummaryText = disks.Length + " physical disk(s) detected, " +
                                   eligibleCount + " available for installation";

                hardwareCompatible = eligibleCount > 0;
                compatibilityText = hardwareCompatible
                    ? "COMPATIBLE - System meets requirements for ZonderqOS Gen3"
                    : "WARNING - Connect an unmounted target disk (>= 68 MB) to install";

                if (selectedDiskIndex >= disks.Length)
                    selectedDiskIndex = -1;
            }
            catch (Exception ex)
            {
                hardwareCompatible = false;
                compatibilityText = "Hardware probe error: " + ex.Message;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.F5 && currentScreen != ScreenProgress)
            {
                RefreshHardwareAndDisks();
                SetStatus("Hardware and storage list refreshed.", Good);
                return;
            }

            if (showEraseWarningModal)
            {
                HandleEraseModalKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (currentScreen == ScreenProgress && !stepTracker.HasFailed)
                    return;
                Close();
                return;
            }

            switch (currentScreen)
            {
                case ScreenWelcome:
                    if (key.Key == ConsoleKeyEx.Enter)
                        GoToScreen(ScreenRequirements);
                    break;

                case ScreenRequirements:
                    if (key.Key == ConsoleKeyEx.Enter)
                        GoToScreen(ScreenSelectDisk);
                    else if (key.Key == ConsoleKeyEx.Backspace)
                        GoToScreen(ScreenWelcome);
                    break;

                case ScreenSelectDisk:
                    if (key.Key == ConsoleKeyEx.UpArrow)
                        MoveDiskSelection(-1);
                    else if (key.Key == ConsoleKeyEx.DownArrow)
                        MoveDiskSelection(1);
                    else if (key.Key == ConsoleKeyEx.Enter)
                        TryAdvanceFromSelectDisk();
                    else if (key.Key == ConsoleKeyEx.Backspace)
                        GoToScreen(ScreenRequirements);
                    break;

                case ScreenInstallType:
                    if (key.Key == ConsoleKeyEx.UpArrow || key.Key == ConsoleKeyEx.LeftArrow)
                    {
                        installModeIndex = (installModeIndex + 2) % 3;
                        ApplyInstallModeSelection();
                    }
                    else if (key.Key == ConsoleKeyEx.DownArrow || key.Key == ConsoleKeyEx.RightArrow)
                    {
                        installModeIndex = (installModeIndex + 1) % 3;
                        ApplyInstallModeSelection();
                    }
                    else if (key.Key == ConsoleKeyEx.Enter)
                        OpenEraseConfirmationModal();
                    else if (key.Key == ConsoleKeyEx.Backspace)
                        GoToScreen(ScreenSelectDisk);
                    break;

                case ScreenUserSetup:
                    HandleUserSetupKeyboard(key);
                    break;

                case ScreenComplete:
                    if (key.Key == ConsoleKeyEx.Enter)
                        SystemPower.Reboot("installer-complete-key");
                    break;
            }
        }

        private void HandleEraseModalKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
            {
                showEraseWarningModal = false;
                Array.Clear(confirmDiskIdChars, 0, confirmDiskIdChars.Length);
                confirmDiskIdLength = 0;
                cachedConfirmDiskIdText = string.Empty;
                SetStatus("Disk erase confirmation cancelled.", Warning);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (confirmDiskIdLength > 0)
                {
                    confirmDiskIdChars[--confirmDiskIdLength] = '\0';
                    cachedConfirmDiskIdText = confirmDiskIdLength > 0
                        ? new string(confirmDiskIdChars, 0, confirmDiskIdLength)
                        : string.Empty;
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                TryConfirmEraseModal();
                return;
            }

            char ch = key.KeyChar;
            if (ch >= 32 && ch <= 126 && confirmDiskIdLength < MaxTextFieldChars)
            {
                confirmDiskIdChars[confirmDiskIdLength++] = char.ToUpperInvariant(ch);
                cachedConfirmDiskIdText = new string(confirmDiskIdChars, 0, confirmDiskIdLength);
            }
        }

        private void HandleUserSetupKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Tab || key.Key == ConsoleKeyEx.DownArrow)
            {
                activeUserField = (activeUserField + 1) % 4;
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                activeUserField = (activeUserField + 3) % 4;
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                switch (activeUserField)
                {
                    case 0:
                        if (usernameLength > 0)
                        {
                            usernameChars[--usernameLength] = '\0';
                            cachedUsernameText = usernameLength > 0 ? new string(usernameChars, 0, usernameLength) : string.Empty;
                        }
                        break;
                    case 1:
                        if (hostnameLength > 0)
                        {
                            hostnameChars[--hostnameLength] = '\0';
                            cachedHostnameText = hostnameLength > 0 ? new string(hostnameChars, 0, hostnameLength) : string.Empty;
                        }
                        break;
                    case 2:
                        if (passwordLength > 0) passwordChars[--passwordLength] = '\0';
                        break;
                    case 3:
                        if (confirmPasswordLength > 0) confirmPasswordChars[--confirmPasswordLength] = '\0';
                        break;
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                if (activeUserField < 3)
                {
                    activeUserField++;
                }
                else
                {
                    TryBeginInstallation();
                }
                return;
            }

            char ch = key.KeyChar;
            if (ch < 32 || ch > 126)
                return;

            switch (activeUserField)
            {
                case 0:
                    if (usernameLength < MaxTextFieldChars)
                    {
                        usernameChars[usernameLength++] = ch;
                        cachedUsernameText = new string(usernameChars, 0, usernameLength);
                    }
                    break;
                case 1:
                    if (hostnameLength < MaxTextFieldChars)
                    {
                        hostnameChars[hostnameLength++] = ch;
                        cachedHostnameText = new string(hostnameChars, 0, hostnameLength);
                    }
                    break;
                case 2:
                    if (passwordLength < MaxPasswordChars)
                        passwordChars[passwordLength++] = ch;
                    break;
                case 3:
                    if (confirmPasswordLength < MaxPasswordChars)
                        confirmPasswordChars[confirmPasswordLength++] = ch;
                    break;
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (Window == null || !Window.Visible || Window.IsMinimized)
                return;

            if (!leftClicked || leftWasClicked)
                return;

            int cx = Window.X + 16;
            int cy = Window.Y + 46;
            int cw = Window.Width - 32;
            int ch = Window.Height - 62;

            if (showEraseWarningModal)
            {
                HandleEraseModalMouse(mouseX, mouseY, cx, cy, cw, ch);
                return;
            }

            switch (currentScreen)
            {
                case ScreenWelcome:
                    HandleWelcomeMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenRequirements:
                    HandleRequirementsMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenSelectDisk:
                    HandleSelectDiskMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenInstallType:
                    HandleInstallTypeMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenUserSetup:
                    HandleUserSetupMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenProgress:
                    HandleProgressMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
                case ScreenComplete:
                    HandleCompleteMouse(mouseX, mouseY, cx, cy, cw, ch);
                    break;
            }
        }

        private void HandleWelcomeMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int btnY = cy + 360;
            int btnW = 290;
            int btnH = 54;
            int installX = cx + 60;
            int tryX = cx + 380;

            if (Hit(mx, my, installX, btnY, btnW, btnH))
            {
                GoToScreen(ScreenRequirements);
            }
            else if (Hit(mx, my, tryX, btnY, btnW, btnH))
            {
                Close();
            }
        }

        private void HandleRequirementsMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int footerY = cy + ch - 52;
            if (Hit(mx, my, cx + 16, footerY, 130, 38))
            {
                GoToScreen(ScreenWelcome);
            }
            else if (Hit(mx, my, cx + 160, footerY, 170, 38))
            {
                RefreshHardwareAndDisks();
                SetStatus("Hardware requirements re-checked.", Good);
            }
            else if (Hit(mx, my, cx + cw - 176, footerY, 160, 38))
            {
                GoToScreen(ScreenSelectDisk);
            }
        }

        private void HandleSelectDiskMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int listX = cx + 16;
            int listY = cy + 104;
            int listW = 540;
            int rowH = 74;
            int rowGap = 8;

            for (int row = 0; row < MaxVisibleDisks; row++)
            {
                int idx = diskScrollOffset + row;
                if (idx >= disks.Length)
                    break;

                int ry = listY + row * (rowH + rowGap);
                if (Hit(mx, my, listX, ry, listW, rowH))
                {
                    selectedDiskIndex = idx;
                    DiskCatalogEntry d = disks[idx];
                    if (d.CanInstall)
                        SetStatus("Selected target disk: " + d.DiskId + " (" + d.Model + ", " + d.FormattedCapacity + ")", Good);
                    else
                        SetStatus(d.BlockReason, Danger);
                    return;
                }
            }

            int footerY = cy + ch - 52;
            if (Hit(mx, my, cx + 16, footerY, 130, 38))
            {
                GoToScreen(ScreenRequirements);
            }
            else if (Hit(mx, my, cx + 160, footerY, 170, 38))
            {
                RefreshHardwareAndDisks();
                SetStatus("Disk list refreshed.", Good);
            }
            else if (Hit(mx, my, cx + cw - 176, footerY, 160, 38))
            {
                TryAdvanceFromSelectDisk();
            }
        }

        private void RefreshDualBootInspectionForSelectedDisk()
        {
            if (selectedDiskIndex >= 0 && selectedDiskIndex < disks.Length)
            {
                currentDualBootInfo = DualBootManager.InspectDiskForDualBoot(disks[selectedDiskIndex]);
                if (currentDualBootInfo.CanAllocateFromFreeSpace)
                {
                    dualBootCandidateIndex = -1;
                    chosenDualBootPartitionLba = 0UL;
                }
                else if (currentDualBootInfo.CandidateTargetPartitions.Count > 0)
                {
                    dualBootCandidateIndex = 0;
                    chosenDualBootPartitionLba = currentDualBootInfo.CandidateTargetPartitions[0].StartLba;
                }
                else
                {
                    dualBootCandidateIndex = -1;
                    chosenDualBootPartitionLba = 0UL;
                }
            }
            else
            {
                currentDualBootInfo = null;
                dualBootCandidateIndex = -1;
                chosenDualBootPartitionLba = 0UL;
            }
        }

        private void ApplyInstallModeSelection()
        {
            if (installModeIndex == 0)
            {
                chosenLayout = InstallationPartitionLayout.DualEfiAndSystem;
                zeroFillExtendedHeader = false;
                SetStatus("Selected: Erase Disk and Install ZonderqOS (Recommended automatic layout).", Good);
            }
            else if (installModeIndex == 1)
            {
                if (chosenLayout == InstallationPartitionLayout.DualBootAlongsideExistingOs)
                    chosenLayout = InstallationPartitionLayout.DualEfiAndSystem;
                SetStatus("Selected: Advanced Installation (Customize partition layout & format options).", Accent);
            }
            else if (installModeIndex == 2)
            {
                chosenLayout = InstallationPartitionLayout.DualBootAlongsideExistingOs;
                zeroFillExtendedHeader = false;
                RefreshDualBootInspectionForSelectedDisk();
                SetStatus("Selected: Dual-Boot Alongside Existing OS (" +
                    (currentDualBootInfo != null ? currentDualBootInfo.DiagnosticNote : "Preserves existing OS partitions") + ")", Good);
            }
        }

        private void HandleInstallTypeMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int cardX = cx + 24;
            int cardW = cw - 48;
            int card1Y = cy + 100;
            int card2Y = cy + 174;
            int card3Y = cy + 248;

            if (Hit(mx, my, cardX, card1Y, cardW, 68))
            {
                installModeIndex = 0;
                ApplyInstallModeSelection();
                return;
            }

            if (Hit(mx, my, cardX, card2Y, cardW, 68))
            {
                installModeIndex = 1;
                ApplyInstallModeSelection();
                return;
            }

            if (Hit(mx, my, cardX, card3Y, cardW, 68))
            {
                installModeIndex = 2;
                ApplyInstallModeSelection();
                return;
            }

            if (installModeIndex == 1)
            {
                int optY = cy + 324;
                if (Hit(mx, my, cardX, optY, 280, 42))
                {
                    chosenLayout = InstallationPartitionLayout.DualEfiAndSystem;
                    SetStatus("Advanced Layout: Dual Partition (64 MB EFI System + ZONDERQDATA).", Accent);
                    return;
                }
                if (Hit(mx, my, cardX + 296, optY, 280, 42))
                {
                    chosenLayout = InstallationPartitionLayout.SingleSystemPartition;
                    SetStatus("Advanced Layout: Single Unified FAT32 Partition (ZONDERQDATA).", Accent);
                    return;
                }
                if (Hit(mx, my, cardX + 592, optY, 280, 42))
                {
                    zeroFillExtendedHeader = !zeroFillExtendedHeader;
                    SetStatus(zeroFillExtendedHeader
                        ? "Format Mode: Extended Header Wipe enabled (4096 sectors)."
                        : "Format Mode: Standard Quick Partition & FAT32 Initialization.", Accent);
                    return;
                }
            }
            else if (installModeIndex == 2 && currentDualBootInfo != null)
            {
                int optY = cy + 324;
                if (Hit(mx, my, cardX, optY, 420, 42))
                {
                    if (currentDualBootInfo.CanAllocateFromFreeSpace)
                    {
                        dualBootCandidateIndex = -1;
                        chosenDualBootPartitionLba = 0UL;
                        SetStatus("Dual-Boot Target: Using " + currentDualBootInfo.FreeSpaceMegabytes + " MB unallocated free space.", Good);
                    }
                    else
                    {
                        SetStatus("No unallocated free space >= 64 MB; select an existing partition on the right.", Warning);
                    }
                    return;
                }
                if (Hit(mx, my, cardX + 436, optY, 440, 42))
                {
                    int count = currentDualBootInfo.CandidateTargetPartitions.Count;
                    if (count > 0)
                    {
                        dualBootCandidateIndex = (dualBootCandidateIndex + 1) % count;
                        DualBootCandidatePartition cand = currentDualBootInfo.CandidateTargetPartitions[dualBootCandidateIndex];
                        chosenDualBootPartitionLba = cand.StartLba;
                        SetStatus("Dual-Boot Target Partition: Part #" + cand.PartitionNumber + " (LBA " + cand.StartLba + ", " + cand.SizeMegabytes + " MB, " + cand.FilesystemType + ").", Accent);
                    }
                    else
                    {
                        SetStatus("No eligible existing non-ESP partitions found on this disk.", Warning);
                    }
                    return;
                }
            }

            int footerY = cy + ch - 52;
            if (Hit(mx, my, cx + 16, footerY, 130, 38))
            {
                GoToScreen(ScreenSelectDisk);
            }
            else if (Hit(mx, my, cx + cw - 226, footerY, 210, 38))
            {
                OpenEraseConfirmationModal();
            }
        }

        private void HandleEraseModalMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int modalW = 680;
            int modalH = 420;
            int modalX = cx + (cw - modalW) / 2;
            int modalY = cy + (ch - modalH) / 2;
            int btnY = modalY + modalH - 58;

            // Cancel button
            if (Hit(mx, my, modalX + 28, btnY, 180, 40))
            {
                showEraseWarningModal = false;
                Array.Clear(confirmDiskIdChars, 0, confirmDiskIdChars.Length);
                confirmDiskIdLength = 0;
                cachedConfirmDiskIdText = string.Empty;
                SetStatus("Disk erase confirmation cancelled. No changes were made.", Warning);
                return;
            }

            // Erase Disk and Install button
            if (Hit(mx, my, modalX + modalW - 268, btnY, 240, 40))
            {
                TryConfirmEraseModal();
            }
        }

        private void HandleUserSetupMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int fieldX = cx + 240;
            int fieldW = 460;
            int fieldH = 38;

            if (Hit(mx, my, fieldX, cy + 130, fieldW, fieldH))
            {
                activeUserField = 0;
                return;
            }
            if (Hit(mx, my, fieldX, cy + 196, fieldW, fieldH))
            {
                activeUserField = 1;
                return;
            }
            if (Hit(mx, my, fieldX, cy + 262, fieldW, fieldH))
            {
                activeUserField = 2;
                return;
            }
            if (Hit(mx, my, fieldX, cy + 328, fieldW, fieldH))
            {
                activeUserField = 3;
                return;
            }

            int footerY = cy + ch - 52;
            if (Hit(mx, my, cx + 16, footerY, 130, 38))
            {
                GoToScreen(ScreenInstallType);
            }
            else if (Hit(mx, my, cx + cw - 226, footerY, 210, 38))
            {
                TryBeginInstallation();
            }
        }

        private void HandleProgressMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            if (!stepTracker.HasFailed)
                return;

            int footerY = cy + ch - 52;
            if (Hit(mx, my, cx + 16, footerY, 180, 38))
            {
                installationStarted = false;
                GoToScreen(ScreenSelectDisk);
            }
            else if (Hit(mx, my, cx + cw - 156, footerY, 140, 38))
            {
                Close();
            }
        }

        private void HandleCompleteMouse(int mx, int my, int cx, int cy, int cw, int ch)
        {
            int btnY = cy + 390;
            int restartX = cx + (cw / 2) - 230;
            int shutdownX = cx + (cw / 2) + 20;

            if (Hit(mx, my, restartX, btnY, 210, 48))
            {
                SystemPower.Reboot("installer-restart-button");
            }
            else if (Hit(mx, my, shutdownX, btnY, 210, 48))
            {
                SystemPower.Shutdown("installer-shutdown-button");
            }
        }

        private void MoveDiskSelection(int delta)
        {
            if (disks.Length == 0)
                return;

            if (selectedDiskIndex < 0)
                selectedDiskIndex = 0;
            else
                selectedDiskIndex = Math.Max(0, Math.Min(disks.Length - 1, selectedDiskIndex + delta));

            if (selectedDiskIndex < diskScrollOffset)
                diskScrollOffset = selectedDiskIndex;
            if (selectedDiskIndex >= diskScrollOffset + MaxVisibleDisks)
                diskScrollOffset = selectedDiskIndex - MaxVisibleDisks + 1;
        }

        private void TryAdvanceFromSelectDisk()
        {
            if (selectedDiskIndex < 0 || selectedDiskIndex >= disks.Length)
            {
                SetStatus("Please select a target disk before continuing.", Warning);
                return;
            }

            DiskCatalogEntry selected = disks[selectedDiskIndex];
            if (!selected.CanInstall)
            {
                SetStatus("Cannot install on " + selected.DiskId + ": " + selected.BlockReason, Danger);
                return;
            }

            GoToScreen(ScreenInstallType);
        }

        private void OpenEraseConfirmationModal()
        {
            if (selectedDiskIndex < 0 || selectedDiskIndex >= disks.Length)
            {
                SetStatus("No target disk selected.", Danger);
                return;
            }

            DiskCatalogEntry selected = disks[selectedDiskIndex];
            if (!selected.CanInstall)
            {
                SetStatus(selected.BlockReason, Danger);
                return;
            }

            Array.Clear(confirmDiskIdChars, 0, confirmDiskIdChars.Length);
            confirmDiskIdLength = 0;
            cachedConfirmDiskIdText = string.Empty;
            showEraseWarningModal = true;
            SetStatus("Type " + selected.DiskId + " to confirm erasing all partitions on the selected disk.", Warning);
        }

        private void TryConfirmEraseModal()
        {
            if (selectedDiskIndex < 0 || selectedDiskIndex >= disks.Length)
                return;

            DiskCatalogEntry selected = disks[selectedDiskIndex];
            string typed = confirmDiskIdLength > 0 ? new string(confirmDiskIdChars, 0, confirmDiskIdLength) : string.Empty;

            if (!InstallerInputValidator.ValidateDiskConfirmation(selected.DiskId, typed))
            {
                SetStatus("Confirmation mismatch: please type '" + selected.DiskId + "' exactly to proceed.", Danger);
                return;
            }

            showEraseWarningModal = false;
            GoToScreen(ScreenUserSetup);
            SetStatus("Disk erase confirmed for " + selected.DiskId + ". Configure your user account.", Good);
        }

        private void TryBeginInstallation()
        {
            if (selectedDiskIndex < 0 || selectedDiskIndex >= disks.Length)
            {
                SetStatus("Target disk is no longer selected.", Danger);
                return;
            }

            string username = usernameLength > 0 ? new string(usernameChars, 0, usernameLength).Trim() : string.Empty;
            string hostname = hostnameLength > 0 ? new string(hostnameChars, 0, hostnameLength).Trim() : string.Empty;

            if (!InstallerInputValidator.ValidateUsername(username, out string userErr))
            {
                SetStatus(userErr, Danger);
                activeUserField = 0;
                return;
            }

            if (!InstallerInputValidator.ValidateHostname(hostname, out string hostErr))
            {
                SetStatus(hostErr, Danger);
                activeUserField = 1;
                return;
            }

            if (passwordLength <= 0)
            {
                SetStatus("Password cannot be empty.", Danger);
                activeUserField = 2;
                return;
            }

            if (confirmPasswordLength != passwordLength)
            {
                SetStatus("Password and Confirm Password do not match.", Danger);
                activeUserField = 3;
                return;
            }

            string passCheck = new string(passwordChars, 0, passwordLength);
            string confCheck = new string(confirmPasswordChars, 0, confirmPasswordLength);
            bool matches = Crypto.FixedTimeEquals(passCheck, confCheck);
            bool validPolicy = PasswordPolicy.Validate(passCheck, out string policyErr);
            passCheck = null;
            confCheck = null;

            if (!matches)
            {
                SetStatus("Password and Confirm Password do not match.", Danger);
                activeUserField = 3;
                return;
            }

            if (!validPolicy)
            {
                SetStatus(policyErr, Danger);
                activeUserField = 2;
                return;
            }

            DiskCatalogEntry targetDisk = disks[selectedDiskIndex];
            string typedConfirm = confirmDiskIdLength > 0
                ? new string(confirmDiskIdChars, 0, confirmDiskIdLength)
                : string.Empty;

            var request = new InstallationRequest
            {
                TargetDiskToken = targetDisk.IdentityToken,
                TypedDiskConfirmation = typedConfirm,
                PartitionLayout = chosenLayout,
                DualBootTargetPartitionStartLba = chosenLayout == InstallationPartitionLayout.DualBootAlongsideExistingOs
                    ? chosenDualBootPartitionLba
                    : 0UL,
                ZeroFillHeaderRegion = zeroFillExtendedHeader,
                Username = username,
                Hostname = hostname,
                PasswordBuffer = passwordChars,
                PasswordLength = passwordLength,
                ConfirmPasswordBuffer = confirmPasswordChars,
                ConfirmPasswordLength = confirmPasswordLength
            };

            installedDiskSummary = targetDisk.DiskId + " - " + targetDisk.Model + " (" + targetDisk.FormattedCapacity + ")";
            installedUserSummary = username + "@" + hostname;

            currentScreen = ScreenProgress;
            installationStarted = true;
            stepTracker.Reset();
            SetStatus("Installing ZonderqOS to " + targetDisk.DiskId + "...", Accent);
            GuiManager.PumpFrame();

            bool ok = SystemInstallerEngine.ExecuteInstallation(
                request,
                stepTracker,
                () =>
                {
                    SetStatus(stepTracker.CurrentStepName + " (" + stepTracker.ProgressPercent + "%)", Accent);
                    GuiManager.PumpFrame();
                },
                out string installError);

            ClearSensitiveBuffers();

            if (ok && stepTracker.IsCompleted && !stepTracker.HasFailed)
            {
                currentScreen = ScreenComplete;
                SetStatus("ZonderqOS installation completed and verified.", Good);
                GuiManager.PumpFrame();
            }
            else
            {
                currentScreen = ScreenProgress;
                SetStatus("Installation failed: " + (installError ?? stepTracker.ErrorMessage), Danger);
                GuiManager.PumpFrame();
            }
        }

        private void GoToScreen(int screen)
        {
            currentScreen = screen;
            showEraseWarningModal = false;
            switch (screen)
            {
                case ScreenWelcome:
                    SetStatus("Welcome to ZonderqOS Setup.", Accent);
                    break;
                case ScreenRequirements:
                    RefreshHardwareAndDisks();
                    SetStatus("Review hardware compatibility and detected disks.", Accent);
                    break;
                case ScreenSelectDisk:
                    RefreshHardwareAndDisks();
                    SetStatus("Select the destination disk for ZonderqOS installation.", Accent);
                    break;
                case ScreenInstallType:
                    RefreshDualBootInspectionForSelectedDisk();
                    SetStatus("Choose installation mode and review disk layout.", Accent);
                    break;
                case ScreenUserSetup:
                    SetStatus("Create the primary administrator account and computer name.", Accent);
                    break;
            }
        }

        private void SetStatus(string msg, Color color)
        {
            statusMessage = string.IsNullOrEmpty(msg) ? "Ready" : msg;
            statusColor = color;
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);

            int cx = Window.X + 16;
            int cy = Window.Y + 46;
            int cw = Window.Width - 32;
            int ch = Window.Height - 62;

            canvas.DrawFilledRectangle(Surface, Window.X + 1, Window.Y + 39, Window.Width - 2, Window.Height - 40);

            RenderStepHeaderBar(canvas, cx, cy, cw);

            switch (currentScreen)
            {
                case ScreenWelcome:
                    RenderWelcomeScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenRequirements:
                    RenderRequirementsScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenSelectDisk:
                    RenderSelectDiskScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenInstallType:
                    RenderInstallTypeScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenUserSetup:
                    RenderUserSetupScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenProgress:
                    RenderProgressScreen(canvas, cx, cy, cw, ch);
                    break;
                case ScreenComplete:
                    RenderCompleteScreen(canvas, cx, cy, cw, ch);
                    break;
            }

            if (showEraseWarningModal)
            {
                RenderEraseWarningModal(canvas, cx, cy, cw, ch);
            }

            RenderStatusBar(canvas, cx, cy + ch - 8, cw);
        }

        private void RenderStepHeaderBar(Canvas canvas, int cx, int cy, int cw)
        {
            canvas.DrawFilledRectangle(HeaderBg, cx, cy, cw, 50);
            canvas.DrawRectangle(Border, cx, cy, cw, 50);
            canvas.DrawFilledRectangle(Accent, cx, cy, 4, 50);

            int stepW = (cw - 24) / StepLabels.Length;
            for (int i = 0; i < StepLabels.Length; i++)
            {
                int sx = cx + 12 + i * stepW;
                bool active = i == currentScreen;
                bool done = i < currentScreen || (currentScreen == ScreenComplete && i == ScreenComplete);
                Color badgeBg = active ? PanelSelected : done ? Color.FromArgb(24, 52, 40) : Panel;
                Color badgeBorder = active ? Accent : done ? Good : Border;
                Color textCol = active ? TextColor : done ? Good : Muted;

                canvas.DrawFilledRectangle(badgeBg, sx, cy + 10, stepW - 6, 30);
                canvas.DrawRectangle(badgeBorder, sx, cy + 10, stepW - 6, 30);
                SmallTextRenderer.DrawCentered(canvas, StepLabels[i], sx + 2, cy + 18, stepW - 10, textCol);
            }
        }

        private void RenderWelcomeScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            int boxX = cx + 24;
            int boxY = cy + 68;
            int boxW = cw - 48;

            canvas.DrawFilledRectangle(Panel, boxX, boxY, boxW, 130);
            canvas.DrawRectangle(Border, boxX, boxY, boxW, 130);
            canvas.DrawFilledRectangle(Accent, boxX, boxY, 4, 130);

            IconManager.DrawScaled(canvas, IconType.Start, boxX + 24, boxY + 26, 48, 48);
            SmallTextRenderer.Draw(canvas, "Welcome to ZonderqOS Setup", boxX + 90, boxY + 24, TextColor);
            SmallTextRenderer.Draw(canvas,
                "This wizard automates disk partitioning, FAT32 formatting, system file installation,",
                boxX + 90, boxY + 52, Muted);
            SmallTextRenderer.Draw(canvas,
                "A/B dual-slot configuration, and Limine + BootGuard bootloader setup.",
                boxX + 90, boxY + 74, Muted);
            SmallTextRenderer.Draw(canvas,
                Disk.IsLiveSession
                    ? "LIVE RAM ENVIRONMENT ACTIVE: No physical disk is modified until you confirm installation."
                    : "SYSTEM SESSION ACTIVE: You can install ZonderqOS onto an additional unmounted disk.",
                boxX + 90, boxY + 100, Good);

            // Version info card
            int infoY = boxY + 148;
            canvas.DrawFilledRectangle(PanelAlt, boxX, infoY, boxW, 118);
            canvas.DrawRectangle(Border, boxX, infoY, boxW, 118);
            SmallTextRenderer.Draw(canvas, "SYSTEM RELEASE INFORMATION", boxX + 20, infoY + 14, Accent);
            SmallTextRenderer.Draw(canvas, "Operating System:  ZonderqOS " + KernelBuildInfo.KernelVersion + " (Build " + KernelBuildInfo.KernelBuild + ")", boxX + 20, infoY + 38, TextColor);
            SmallTextRenderer.Draw(canvas, "Kernel Engine:     Cosmos Kernel Gen3 3.0.99 NativeAOT (" + cpuArchText + ")", boxX + 20, infoY + 60, TextColor);
            SmallTextRenderer.Draw(canvas, "Boot Architecture: Limine v10 UEFI/BIOS + ZonderqOS EFI BootGuard (A/B Slot A & B)", boxX + 20, infoY + 82, TextColor);

            // Two main buttons: Install ZonderqOS / Try ZonderqOS without installing
            int btnY = cy + 360;
            int btnW = 290;
            int btnH = 54;
            DrawButton(canvas, cx + 60, btnY, btnW, btnH, "Install ZonderqOS", "Start guided disk installation", Accent, true);
            DrawButton(canvas, cx + 380, btnY, btnW, btnH, "Try ZonderqOS without installing", "Continue in Live desktop mode", Good, false);
        }

        private void RenderRequirementsScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            int boxX = cx + 20;
            int boxY = cy + 66;
            int boxW = cw - 40;

            SmallTextRenderer.Draw(canvas, "SYSTEM REQUIREMENTS & HARDWARE COMPATIBILITY", boxX, boxY, TextColor);
            SmallTextRenderer.Draw(canvas, "Verified hardware profile detected by the ZonderqOS kernel:", boxX, boxY + 20, Muted);

            int rowY = boxY + 50;
            int rowH = 54;
            int gap = 10;

            DrawRequirementRow(canvas, boxX, rowY, boxW, rowH, "CPU Architecture", cpuArchText + "  |  " + cpuBrandText, "SUPPORTED", Good);
            rowY += rowH + gap;
            DrawRequirementRow(canvas, boxX, rowY, boxW, rowH, "Available Memory (RAM)", ramText, "PASS", Good);
            rowY += rowH + gap;
            DrawRequirementRow(canvas, boxX, rowY, boxW, rowH, "BIOS / UEFI Firmware Mode", firmwareModeText, "DETECTED", Accent);
            rowY += rowH + gap;
            DrawRequirementRow(canvas, boxX, rowY, boxW, rowH, "Available Storage Disks", disksSummaryText,
                hardwareCompatible ? "READY" : "NO TARGET", hardwareCompatible ? Good : Warning);
            rowY += rowH + gap;
            DrawRequirementRow(canvas, boxX, rowY, boxW, rowH, "Hardware Compatibility", compatibilityText,
                hardwareCompatible ? "COMPATIBLE" : "CHECK DISK", hardwareCompatible ? Good : Warning);

            int footerY = cy + ch - 52;
            DrawButton(canvas, cx + 16, footerY, 130, 38, "Back", null, Muted, false);
            DrawButton(canvas, cx + 160, footerY, 170, 38, "Refresh (F5)", null, Accent, false);
            DrawButton(canvas, cx + cw - 176, footerY, 160, 38, "Next", null, Accent, true);
        }

        private void DrawRequirementRow(Canvas canvas, int x, int y, int w, int h, string label, string val, string badge, Color badgeCol)
        {
            canvas.DrawFilledRectangle(Panel, x, y, w, h);
            canvas.DrawRectangle(Border, x, y, w, h);
            canvas.DrawFilledRectangle(badgeCol, x, y, 4, h);
            SmallTextRenderer.Draw(canvas, label, x + 18, y + 12, Muted);
            SmallTextRenderer.DrawClipped(canvas, val ?? "Unknown", x + 18, y + 32, w - 180, TextColor);

            int bx = x + w - 140;
            canvas.DrawFilledRectangle(PanelAlt, bx, y + 13, 124, 28);
            canvas.DrawRectangle(badgeCol, bx, y + 13, 124, 28);
            SmallTextRenderer.DrawCentered(canvas, badge, bx + 4, y + 21, 116, badgeCol);
        }

        private void RenderSelectDiskScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            SmallTextRenderer.Draw(canvas, "SELECT INSTALLATION DISK", cx + 16, cy + 64, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Select the physical disk where ZonderqOS will be installed. Protected boot/system media cannot be formatted.",
                cx + 16, cy + 82, Muted);

            int listX = cx + 16;
            int listY = cy + 104;
            int listW = 540;
            int rowH = 74;
            int rowGap = 8;

            if (disks.Length == 0)
            {
                canvas.DrawFilledRectangle(Panel, listX, listY, listW, 180);
                canvas.DrawRectangle(Border, listX, listY, listW, 180);
                SmallTextRenderer.Draw(canvas, "NO PHYSICAL DISKS DETECTED", listX + 20, listY + 30, Warning);
                SmallTextRenderer.Draw(canvas, "Attach a virtual or physical SATA/AHCI, NVMe, VirtIO or USB disk", listX + 20, listY + 56, Muted);
                SmallTextRenderer.Draw(canvas, "and press F5 to rescan.", listX + 20, listY + 76, Muted);
            }
            else
            {
                for (int row = 0; row < MaxVisibleDisks; row++)
                {
                    int idx = diskScrollOffset + row;
                    if (idx >= disks.Length)
                        break;

                    DiskCatalogEntry d = disks[idx];
                    int ry = listY + row * (rowH + rowGap);
                    bool sel = idx == selectedDiskIndex;
                    Color bg = sel ? PanelSelected : Panel;
                    Color borderCol = sel ? Accent : (d.CanInstall ? Border : Warning);

                    canvas.DrawFilledRectangle(bg, listX, ry, listW, rowH);
                    canvas.DrawRectangle(borderCol, listX, ry, listW, rowH);
                    canvas.DrawFilledRectangle(d.CanInstall ? Good : Warning, listX, ry, 4, rowH);

                    IconManager.DrawScaled(canvas, IconType.DiskManager, listX + 14, ry + 16, 28, 28);
                    SmallTextRenderer.DrawClipped(canvas,
                        d.DiskId + "  |  " + d.Model + "  (" + d.FormattedCapacity + ")",
                        listX + 52, ry + 12, listW - 190, TextColor);
                    SmallTextRenderer.DrawClipped(canvas,
                        "Bus: " + d.InterfaceType + "  |  Dev: " + d.DeviceName + "  |  Table: " + d.PartitionTableType,
                        listX + 52, ry + 32, listW - 64, Muted);
                    SmallTextRenderer.DrawClipped(canvas,
                        "FS: " + d.FilesystemsSummary + "  |  Data: " + (d.HasExistingData ? "Existing Data" : "Empty"),
                        listX + 52, ry + 50, listW - 64, d.HasExistingData ? Warning : Good);

                    string statusBadge = d.IsInstallationMedia ? "INSTALL MEDIA"
                        : d.IsActiveSystemDisk ? "SYSTEM DISK"
                        : d.CanInstall ? "AVAILABLE" : "PROTECTED";
                    Color badgeCol = d.CanInstall ? Good : Warning;
                    canvas.DrawFilledRectangle(PanelAlt, listX + listW - 128, ry + 8, 118, 22);
                    canvas.DrawRectangle(badgeCol, listX + listW - 128, ry + 8, 118, 22);
                    SmallTextRenderer.DrawCentered(canvas, statusBadge, listX + listW - 126, ry + 13, 114, badgeCol);
                }
            }

            // Right details panel showing all required fields explicitly
            int detX = listX + listW + 14;
            int detW = cw - listW - 46;
            int detH = 322;
            canvas.DrawFilledRectangle(Panel, detX, listY, detW, detH);
            canvas.DrawRectangle(Border, detX, listY, detW, detH);
            SmallTextRenderer.Draw(canvas, "SELECTED DISK DETAILS", detX + 14, listY + 14, Accent);
            canvas.DrawLine(Border, detX + 14, listY + 34, detX + detW - 14, listY + 34);

            if (selectedDiskIndex >= 0 && selectedDiskIndex < disks.Length)
            {
                DiskCatalogEntry d = disks[selectedDiskIndex];
                int dy = listY + 46;
                int step = 28;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Disk ID:", d.DiskId + " (" + d.DeviceName + ")", TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Model:", d.Model, TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Capacity:", d.FormattedCapacity, TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Interface:", d.InterfaceType, TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Partitions:", d.PartitionsSummary, TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Filesystems:", d.FilesystemsSummary, TextColor);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Existing Data:", d.HasExistingData ? "Yes (Will be erased)" : "No (Empty disk)",
                    d.HasExistingData ? Warning : Good);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Install Media:", d.IsInstallationMedia ? "Yes (Protected)" : "No",
                    d.IsInstallationMedia ? Warning : Good);
                dy += step;
                DrawDetailPair(canvas, detX + 14, dy, detW - 28, "Eligibility:",
                    d.CanInstall ? "Eligible target disk" : d.BlockReason,
                    d.CanInstall ? Good : Danger);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, "Click a disk on the left to inspect", detX + 14, listY + 60, Muted);
                SmallTextRenderer.Draw(canvas, "its partitions, filesystems, and", detX + 14, listY + 80, Muted);
                SmallTextRenderer.Draw(canvas, "installation eligibility.", detX + 14, listY + 100, Muted);
            }

            int footerY = cy + ch - 52;
            DrawButton(canvas, cx + 16, footerY, 130, 38, "Back", null, Muted, false);
            DrawButton(canvas, cx + 160, footerY, 170, 38, "Rescan (F5)", null, Accent, false);
            bool canNext = selectedDiskIndex >= 0 && selectedDiskIndex < disks.Length && disks[selectedDiskIndex].CanInstall;
            DrawButton(canvas, cx + cw - 176, footerY, 160, 38, "Next", null, canNext ? Accent : Muted, canNext);
        }

        private void DrawDetailPair(Canvas canvas, int x, int y, int w, string label, string val, Color valCol)
        {
            SmallTextRenderer.Draw(canvas, label, x, y, Muted);
            SmallTextRenderer.DrawClipped(canvas, string.IsNullOrWhiteSpace(val) ? "Unknown" : val, x + 108, y, Math.Max(40, w - 110), valCol);
        }

        private void RenderInstallTypeScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            SmallTextRenderer.Draw(canvas, "INSTALLATION TYPE", cx + 24, cy + 62, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Choose how ZonderqOS should prepare and partition the selected disk:",
                cx + 24, cy + 80, Muted);

            int cardX = cx + 24;
            int cardW = cw - 48;

            // Option 1: Erase Disk and Install ZonderqOS
            bool opt1 = installModeIndex == 0;
            canvas.DrawFilledRectangle(opt1 ? PanelSelected : Panel, cardX, cy + 100, cardW, 68);
            canvas.DrawRectangle(opt1 ? Accent : Border, cardX, cy + 100, cardW, 68);
            canvas.DrawFilledRectangle(opt1 ? Accent : Muted, cardX, cy + 100, 4, 68);
            SmallTextRenderer.Draw(canvas,
                (opt1 ? "[*] " : "[ ] ") + "1. Erase Disk and Install ZonderqOS (Recommended - Automatic)",
                cardX + 18, cy + 112, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Automatically removes all existing partitions on the selected disk, creates the EFI System Partition",
                cardX + 42, cy + 132, Muted);
            SmallTextRenderer.Draw(canvas,
                "and ZonderqOS System Partition (FAT32 ZONDERQDATA), and installs Limine + BootGuard.",
                cardX + 42, cy + 148, Muted);

            // Option 2: Advanced Installation
            bool opt2 = installModeIndex == 1;
            canvas.DrawFilledRectangle(opt2 ? PanelSelected : Panel, cardX, cy + 174, cardW, 68);
            canvas.DrawRectangle(opt2 ? Accent : Border, cardX, cy + 174, cardW, 68);
            canvas.DrawFilledRectangle(opt2 ? Accent : Muted, cardX, cy + 174, 4, 68);
            SmallTextRenderer.Draw(canvas,
                (opt2 ? "[*] " : "[ ] ") + "2. Advanced Installation (Custom Partition Layout & Wipe Policy)",
                cardX + 18, cy + 186, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Configure Dual-Partition (ESP + ZONDERQDATA) vs Single-Partition layout, or enable extended",
                cardX + 42, cy + 206, Muted);
            SmallTextRenderer.Draw(canvas,
                "header zero-filling before formatting. Protected disks remain strictly locked.",
                cardX + 42, cy + 222, Muted);

            // Option 3: Dual-Boot Alongside Existing OS
            bool opt3 = installModeIndex == 2;
            canvas.DrawFilledRectangle(opt3 ? PanelSelected : Panel, cardX, cy + 248, cardW, 68);
            canvas.DrawRectangle(opt3 ? Good : Border, cardX, cy + 248, cardW, 68);
            canvas.DrawFilledRectangle(opt3 ? Good : Muted, cardX, cy + 248, 4, 68);
            SmallTextRenderer.Draw(canvas,
                (opt3 ? "[*] " : "[ ] ") + "3. Dual-Boot Alongside Existing OS (Preserve Kali Linux / Windows / ESP)",
                cardX + 18, cy + 260, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Preserves all existing OS partitions and shares the EFI System Partition. Allocates ZONDERQDATA from",
                cardX + 42, cy + 280, Muted);
            SmallTextRenderer.Draw(canvas,
                "free space (or a selected partition) and adds Kali Linux / Windows / GRUB to the Limine boot menu.",
                cardX + 42, cy + 296, Muted);

            if (opt2)
            {
                int optY = cy + 324;
                bool dual = chosenLayout == InstallationPartitionLayout.DualEfiAndSystem;
                DrawButton(canvas, cardX, optY, 280, 42,
                    dual ? "[*] Dual: ESP + ZONDERQDATA" : "[ ] Dual: ESP + ZONDERQDATA",
                    null, dual ? Accent : Muted, dual);
                DrawButton(canvas, cardX + 296, optY, 280, 42,
                    !dual ? "[*] Single: ZONDERQDATA" : "[ ] Single: ZONDERQDATA",
                    null, !dual ? Accent : Muted, !dual);
                DrawButton(canvas, cardX + 592, optY, 280, 42,
                    zeroFillExtendedHeader ? "[*] Extended Header Wipe" : "[ ] Quick Format (Default)",
                    null, zeroFillExtendedHeader ? Warning : Good, false);
            }
            else if (opt3 && currentDualBootInfo != null)
            {
                int optY = cy + 324;
                bool useFree = dualBootCandidateIndex < 0 && currentDualBootInfo.CanAllocateFromFreeSpace;
                string freeLabel = currentDualBootInfo.CanAllocateFromFreeSpace
                    ? ((useFree ? "[*] " : "[ ] ") + "Free Space (" + currentDualBootInfo.FreeSpaceMegabytes + " MB)")
                    : "[Unavailable] Free Space (< 64 MB)";
                DrawButton(canvas, cardX, optY, 420, 42,
                    freeLabel, null, useFree ? Good : Muted, useFree);

                string partLabel;
                bool usePart = dualBootCandidateIndex >= 0 && dualBootCandidateIndex < currentDualBootInfo.CandidateTargetPartitions.Count;
                if (usePart)
                {
                    DualBootCandidatePartition cand = currentDualBootInfo.CandidateTargetPartitions[dualBootCandidateIndex];
                    partLabel = "[*] Part #" + cand.PartitionNumber + " (" + cand.SizeMegabytes + " MB " + cand.FilesystemType + ") [Click to cycle]";
                }
                else if (currentDualBootInfo.CandidateTargetPartitions.Count > 0)
                {
                    partLabel = "[ ] Select Existing Partition (" + currentDualBootInfo.CandidateTargetPartitions.Count + " available)";
                }
                else
                {
                    partLabel = "[None] No Eligible Existing Partitions";
                }
                DrawButton(canvas, cardX + 436, optY, 440, 42,
                    partLabel, null, usePart ? Accent : Muted, usePart);
            }

            // Target disk summary preview
            if (selectedDiskIndex >= 0 && selectedDiskIndex < disks.Length)
            {
                DiskCatalogEntry d = disks[selectedDiskIndex];
                int sumY = cy + 378;
                canvas.DrawFilledRectangle(PanelAlt, cardX, sumY, cardW, 84);
                canvas.DrawRectangle(Border, cardX, sumY, cardW, 84);
                SmallTextRenderer.Draw(canvas,
                    "TARGET DISK: " + d.DiskId + " (" + d.Model + ", " + d.FormattedCapacity + ")",
                    cardX + 16, sumY + 14, Warning);

                string plannedDesc;
                if (chosenLayout == InstallationPartitionLayout.DualBootAlongsideExistingOs)
                {
                    string targetPartDesc = chosenDualBootPartitionLba == 0
                        ? ("New FAT32 ZONDERQDATA in unallocated space (" + (currentDualBootInfo != null ? currentDualBootInfo.FreeSpaceMegabytes : 0) + " MB)")
                        : ("Format existing partition @ LBA " + chosenDualBootPartitionLba + " as FAT32 ZONDERQDATA");
                    plannedDesc = "DUAL-BOOT: Preserve foreign OS (" + (currentDualBootInfo != null ? currentDualBootInfo.DetectedOsSummary : "Auto") + ") | " + targetPartDesc;
                }
                else if (chosenLayout == InstallationPartitionLayout.DualEfiAndSystem && d.SizeMegabytes >= DiskFormattingEngine.DualLayoutMinDiskMegabytes)
                {
                    plannedDesc = "Partition 1: 64 MB EFI System (FAT32 ZONDERQEFI)  |  Partition 2: ZonderqOS System (FAT32 ZONDERQDATA)";
                }
                else
                {
                    plannedDesc = "Partition 1: Full-Disk ZonderqOS System Partition (FAT32 ZONDERQDATA)";
                }

                SmallTextRenderer.DrawClipped(canvas,
                    "PLANNED LAYOUT: " + plannedDesc,
                    cardX + 16, sumY + 38, cardW - 32, TextColor);
                SmallTextRenderer.Draw(canvas,
                    "OTHER DISKS: All other connected disks remain untouched and protected.",
                    cardX + 16, sumY + 60, Good);
            }

            int footerY = cy + ch - 52;
            DrawButton(canvas, cx + 16, footerY, 130, 38, "Back", null, Muted, false);
            DrawButton(canvas, cx + cw - 226, footerY, 210, 38, "Continue", null, Warning, true);
        }

        private void RenderEraseWarningModal(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            int modalW = 680;
            int modalH = 420;
            int modalX = cx + (cw - modalW) / 2;
            int modalY = cy + (ch - modalH) / 2;

            canvas.DrawFilledRectangle(DangerBg, modalX, modalY, modalW, modalH);
            canvas.DrawRectangle(Danger, modalX, modalY, modalW, modalH);
            canvas.DrawFilledRectangle(Danger, modalX, modalY, modalW, 38);

            SmallTextRenderer.Draw(canvas, "WARNING: ALL DATA WILL BE DELETED", modalX + 18, modalY + 12, TextColor);

            DiskCatalogEntry d = (selectedDiskIndex >= 0 && selectedDiskIndex < disks.Length)
                ? disks[selectedDiskIndex]
                : null;

            int ty = modalY + 54;
            SmallTextRenderer.Draw(canvas, "You are about to erase the selected disk.", modalX + 24, ty, TextColor);
            ty += 22;
            SmallTextRenderer.Draw(canvas, "This operation will delete all existing partitions, operating systems,", modalX + 24, ty, TextColor);
            ty += 20;
            SmallTextRenderer.Draw(canvas, "applications, and personal files on this disk.", modalX + 24, ty, TextColor);
            ty += 22;
            SmallTextRenderer.Draw(canvas, "This action cannot be undone. Please verify that you have selected the correct disk.", modalX + 24, ty, Warning);

            ty += 30;
            canvas.DrawFilledRectangle(Panel, modalX + 24, ty, modalW - 48, 96);
            canvas.DrawRectangle(Border, modalX + 24, ty, modalW - 48, 96);
            if (d != null)
            {
                SmallTextRenderer.Draw(canvas, "Target Identifier:   " + d.DiskId + " (" + d.DeviceName + ")", modalX + 38, ty + 14, Warning);
                SmallTextRenderer.Draw(canvas, "Model & Capacity:    " + d.Model + "  |  " + d.FormattedCapacity, modalX + 38, ty + 36, TextColor);
                SmallTextRenderer.DrawClipped(canvas, "Existing Partitions: " + d.PartitionsSummary + " (" + d.FilesystemsSummary + ")",
                    modalX + 38, ty + 58, modalW - 76, TextColor);
            }

            ty += 112;
            string expectedId = d != null ? d.DiskId : "DISK-0";
            SmallTextRenderer.Draw(canvas,
                "Type '" + expectedId + "' below to confirm erasing this disk:",
                modalX + 24, ty, TextColor);

            int inputY = ty + 22;
            canvas.DrawFilledRectangle(Surface, modalX + 24, inputY, 320, 36);
            canvas.DrawRectangle(Accent, modalX + 24, inputY, 320, 36);
            string typed = cachedConfirmDiskIdText;
            SmallTextRenderer.Draw(canvas, typed, modalX + 36, inputY + 11, TextColor);
            SmallTextRenderer.Draw(canvas, "_", modalX + 36 + SmallTextRenderer.Width(typed), inputY + 11, Accent);

            bool confirmed = d != null && InstallerInputValidator.ValidateDiskConfirmation(d.DiskId, typed);
            int btnY = modalY + modalH - 58;
            DrawButton(canvas, modalX + 28, btnY, 180, 40, "Cancel", null, Muted, false);
            DrawButton(canvas, modalX + modalW - 268, btnY, 240, 40, "Erase Disk and Install", null,
                confirmed ? Danger : Muted, confirmed);
        }

        private void RenderUserSetupScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            SmallTextRenderer.Draw(canvas, "USER ACCOUNT & COMPUTER SETUP", cx + 24, cy + 66, TextColor);
            SmallTextRenderer.Draw(canvas,
                "Create your user account. Credentials are hashed with PBKDF2-HMAC-SHA256 (never stored in plaintext).",
                cx + 24, cy + 84, Muted);

            int labelX = cx + 40;
            int fieldX = cx + 240;
            int fieldW = 460;
            int fieldH = 38;

            DrawInputField(canvas, labelX, fieldX, cy + 130, fieldW, fieldH, "Username:", cachedUsernameText, activeUserField == 0, "e.g. alice (letters, digits, _, -)");
            DrawInputField(canvas, labelX, fieldX, cy + 196, fieldW, fieldH, "Computer Name:", cachedHostnameText, activeUserField == 1, "e.g. zonderq-pc");
            DrawMaskedInputField(canvas, labelX, fieldX, cy + 262, fieldW, fieldH, "Password:", passwordLength, activeUserField == 2, "Minimum 8 characters (letters + digits)");
            DrawMaskedInputField(canvas, labelX, fieldX, cy + 328, fieldW, fieldH, "Confirm Password:", confirmPasswordLength, activeUserField == 3, "Re-enter the same password");

            int footerY = cy + ch - 52;
            DrawButton(canvas, cx + 16, footerY, 130, 38, "Back", null, Muted, false);
            DrawButton(canvas, cx + cw - 226, footerY, 210, 38, "Install Now", null, Good, true);
        }

        private void DrawInputField(Canvas canvas, int labelX, int fieldX, int y, int w, int h,
            string label, string value, bool focused, string hint)
        {
            SmallTextRenderer.Draw(canvas, label, labelX, y + 12, TextColor);
            canvas.DrawFilledRectangle(focused ? PanelSelected : Panel, fieldX, y, w, h);
            canvas.DrawRectangle(focused ? Accent : Border, fieldX, y, w, h);
            string safeVal = value ?? string.Empty;
            SmallTextRenderer.DrawClipped(canvas, safeVal, fieldX + 12, y + 12, w - 30, TextColor);
            if (focused)
            {
                int caretX = fieldX + 12 + Math.Min(SmallTextRenderer.Width(safeVal), w - 30);
                SmallTextRenderer.Draw(canvas, "_", caretX, y + 12, Accent);
            }
            if (!string.IsNullOrEmpty(hint))
                SmallTextRenderer.Draw(canvas, hint, fieldX, y + h + 6, Muted);
        }

        private void DrawMaskedInputField(Canvas canvas, int labelX, int fieldX, int y, int w, int h,
            string label, int maskLength, bool focused, string hint)
        {
            SmallTextRenderer.Draw(canvas, label, labelX, y + 12, TextColor);
            canvas.DrawFilledRectangle(focused ? PanelSelected : Panel, fieldX, y, w, h);
            canvas.DrawRectangle(focused ? Accent : Border, fieldX, y, w, h);
            int maxChars = Math.Max(0, (w - 30) / 6);
            int visible = Math.Min(maskLength, Math.Min(maxChars, PasswordMask.Length));
            if (visible > 0)
                SmallTextRenderer.DrawRange(canvas, PasswordMask, 0, visible, fieldX + 12, y + 12, TextColor);
            if (focused)
                SmallTextRenderer.Draw(canvas, "_", fieldX + 12 + visible * 6, y + 12, Accent);
            if (!string.IsNullOrEmpty(hint))
                SmallTextRenderer.Draw(canvas, hint, fieldX, y + h + 6, Muted);
        }

        private void RenderProgressScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            int boxX = cx + 28;
            int boxW = cw - 56;

            SmallTextRenderer.Draw(canvas,
                stepTracker.HasFailed ? "INSTALLATION FAILED" : "INSTALLING ZONDERQOS",
                boxX, cy + 66,
                stepTracker.HasFailed ? Danger : TextColor);
            SmallTextRenderer.Draw(canvas,
                "Target: " + installedDiskSummary,
                boxX, cy + 86, Muted);

            // Real progress bar
            int barY = cy + 112;
            int barH = 28;
            canvas.DrawFilledRectangle(Panel, boxX, barY, boxW, barH);
            canvas.DrawRectangle(Border, boxX, barY, boxW, barH);

            int pct = stepTracker.ProgressPercent;
            int fillW = ((boxW - 4) * pct) / 100;
            if (fillW > 0)
            {
                canvas.DrawFilledRectangle(stepTracker.HasFailed ? Danger : Accent, boxX + 2, barY + 2, fillW, barH - 4);
            }
            SmallTextRenderer.DrawCentered(canvas,
                stepTracker.CurrentStepName + " - " + pct + "%",
                boxX, barY + 8, boxW, TextColor);

            // List of all 9 installation steps
            int listY = cy + 154;
            int rowH = 30;
            for (int i = 0; i < InstallerStepTracker.StepCount; i++)
            {
                int ry = listY + i * (rowH + 4);
                int state = stepTracker.GetStepState(i);
                string badge = state == InstallerStepTracker.StateCompleted ? "[DONE]"
                    : state == InstallerStepTracker.StateActive ? "[ACTIVE]"
                    : state == InstallerStepTracker.StateFailed ? "[FAILED]"
                    : "[PENDING]";
                Color col = state == InstallerStepTracker.StateCompleted ? Good
                    : state == InstallerStepTracker.StateActive ? Accent
                    : state == InstallerStepTracker.StateFailed ? Danger
                    : Muted;

                canvas.DrawFilledRectangle(Panel, boxX, ry, boxW, rowH);
                canvas.DrawRectangle(Border, boxX, ry, boxW, rowH);
                canvas.DrawFilledRectangle(col, boxX, ry, 4, rowH);
                SmallTextRenderer.Draw(canvas, badge, boxX + 16, ry + 8, col);
                SmallTextRenderer.Draw(canvas, InstallerStepTracker.GetStepTitle(i), boxX + 118, ry + 8, TextColor);
            }

            if (stepTracker.HasFailed)
            {
                int errY = cy + ch - 96;
                canvas.DrawFilledRectangle(DangerBg, boxX, errY, boxW, 38);
                canvas.DrawRectangle(Danger, boxX, errY, boxW, 38);
                SmallTextRenderer.DrawClipped(canvas, "ERROR: " + stepTracker.ErrorMessage, boxX + 12, errY + 12, boxW - 24, Danger);

                int footerY = cy + ch - 52;
                DrawButton(canvas, cx + 16, footerY, 180, 38, "Back to Disk Select", null, Warning, false);
                DrawButton(canvas, cx + cw - 156, footerY, 140, 38, "Close", null, Muted, false);
            }
        }

        private void RenderCompleteScreen(Canvas canvas, int cx, int cy, int cw, int ch)
        {
            int boxX = cx + 40;
            int boxY = cy + 82;
            int boxW = cw - 80;

            canvas.DrawFilledRectangle(Panel, boxX, boxY, boxW, 270);
            canvas.DrawRectangle(Good, boxX, boxY, boxW, 270);
            canvas.DrawFilledRectangle(Good, boxX, boxY, 5, 270);

            SmallTextRenderer.Draw(canvas, "ZonderqOS has been successfully installed.", boxX + 28, boxY + 26, Good);
            SmallTextRenderer.Draw(canvas,
                "All partitions, FAT32 filesystems, system files, A/B boot slots, and Limine bootloader",
                boxX + 28, boxY + 56, TextColor);
            SmallTextRenderer.Draw(canvas,
                "have been written and cryptographically verified on the target disk.",
                boxX + 28, boxY + 76, TextColor);

            SmallTextRenderer.Draw(canvas, "Installed Target Disk:  " + installedDiskSummary, boxX + 28, boxY + 118, Accent);
            SmallTextRenderer.Draw(canvas, "Configured Account:     " + installedUserSummary, boxX + 28, boxY + 142, Accent);
            SmallTextRenderer.Draw(canvas, "Bootloader & Slot A:    Limine UEFI/BIOS + EFI BootGuard (Slot A Healthy)", boxX + 28, boxY + 166, Good);
            SmallTextRenderer.Draw(canvas, "IMPORTANT: Remove the installation ISO or USB drive before restarting so the", boxX + 28, boxY + 206, Warning);
            SmallTextRenderer.Draw(canvas, "computer boots directly from the newly installed ZonderqOS disk.", boxX + 28, boxY + 226, Warning);

            int btnY = cy + 390;
            int restartX = cx + (cw / 2) - 230;
            int shutdownX = cx + (cw / 2) + 20;
            DrawButton(canvas, restartX, btnY, 210, 48, "Restart Now", null, Good, true);
            DrawButton(canvas, shutdownX, btnY, 210, 48, "Shut Down", null, Accent, false);
        }

        private void RenderStatusBar(Canvas canvas, int cx, int y, int cw)
        {
            canvas.DrawFilledRectangle(HeaderBg, cx, y - 22, cw, 24);
            canvas.DrawRectangle(Border, cx, y - 22, cw, 24);
            canvas.DrawFilledRectangle(statusColor, cx + 8, y - 13, 6, 6);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, cx + 22, y - 15, cw - 30, statusColor);
        }

        private static void DrawButton(Canvas canvas, int x, int y, int w, int h,
            string title, string subtitle, Color accent, bool primary)
        {
            Color bg = primary ? PanelSelected : Panel;
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawRectangle(accent, x, y, w, h);
            canvas.DrawFilledRectangle(accent, x, y, 4, h);

            if (string.IsNullOrEmpty(subtitle))
            {
                SmallTextRenderer.DrawCentered(canvas, title, x + 6, y + (h - 10) / 2, w - 12, TextColor);
            }
            else
            {
                SmallTextRenderer.Draw(canvas, title, x + 16, y + 11, TextColor);
                SmallTextRenderer.Draw(canvas, subtitle, x + 16, y + 31, Muted);
            }
        }

        private static bool Hit(int px, int py, int x, int y, int w, int h)
        {
            return px >= x && px < x + w && py >= y && py < y + h;
        }
    }
}
