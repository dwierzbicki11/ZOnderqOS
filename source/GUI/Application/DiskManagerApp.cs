using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.FileSystem;
using ZonderqOS.GUI.Icons;
using ZonderqOS.Storage;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Native storage manager for ZOnderqOS. Storage/VFS data is snapshotted only on
    /// open, F5 or an explicit action. Rendering uses fixed arrays and cached strings,
    /// so leaving the window open does not continuously allocate managed objects.
    /// </summary>
    public sealed class DiskManagerApp : Application
    {
        private const int MaxDevices = 8;
        private const int MaxPartitions = 24;
        private const int MaxMounts = 24;
        private const int MaxVisibleRows = 6;
        private const int RowHeight = 58;
        private const int RowGap = 7;
        private const int ListTopOffset = 162;
        private const int ActionBottomOffset = 142;

        // Derived from the actual window size, shared by draw and hit-testing.
        private bool ShowDetails => Window.Width >= 920 && Window.Height >= 690;
        private int ListWidth => ShowDetails ? 650 : System.Math.Max(1, Window.Width - 36);
        private int ActionY => Window.Y + Window.Height - ActionBottomOffset;
        private int VisibleRows => System.Math.Max(1, System.Math.Min(MaxVisibleRows,
            (ActionY - (Window.Y + ListTopOffset) - 8 + RowGap) / (RowHeight + RowGap)));

        private static readonly string[] TabLabels = { "DISKS", "PARTITIONS", "MOUNTS" };
        private static readonly Color Surface = Color.FromArgb(19, 24, 30);
        private static readonly Color Header = Color.FromArgb(25, 32, 39);
        private static readonly Color Panel = Color.FromArgb(29, 36, 44);
        private static readonly Color PanelHover = Color.FromArgb(38, 50, 61);
        private static readonly Color PanelSelected = Color.FromArgb(37, 57, 74);
        private static readonly Color Border = Color.FromArgb(55, 68, 80);
        private static readonly Color Text = Color.FromArgb(232, 237, 242);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color Good = Color.FromArgb(78, 185, 126);
        private static readonly Color Warning = Color.FromArgb(224, 174, 76);
        private static readonly Color Danger = Color.FromArgb(215, 86, 91);

        private readonly Action closeCallback;

        private readonly int[] deviceSourceIndex = new int[MaxDevices];
        private readonly string[] deviceIndexText = new string[MaxDevices];
        private readonly string[] deviceNames = new string[MaxDevices];
        private readonly string[] deviceDetails = new string[MaxDevices];
        private readonly string[] deviceSizeText = new string[MaxDevices];
        private readonly ulong[] deviceSizeMb = new ulong[MaxDevices];
        private readonly bool[] deviceProtected = new bool[MaxDevices];
        private readonly DiskIdentityToken[] deviceTokens = new DiskIdentityToken[MaxDevices];

        private readonly int[] partitionSourceIndex = new int[MaxPartitions];
        private readonly string[] partitionIndexText = new string[MaxPartitions];
        private readonly string[] partitionNames = new string[MaxPartitions];
        private readonly string[] partitionDetails = new string[MaxPartitions];
        private readonly string[] partitionFsInfo = new string[MaxPartitions];
        private readonly string[] partitionMounts = new string[MaxPartitions];
        private readonly string[] partitionSizeText = new string[MaxPartitions];
        private readonly ulong[] partitionSizeMb = new ulong[MaxPartitions];
        private readonly bool[] partitionProtected = new bool[MaxPartitions];

        private readonly string[] mountNames = new string[MaxMounts];
        private readonly string[] mountDetails = new string[MaxMounts];
        private readonly string[] mountSources = new string[MaxMounts];

        private int deviceCount;
        private int partitionCount;
        private int mountCount;
        private int tab;
        private int selection;
        private int offset;
        private int hoveredRow = -1;
        private int hoveredTab = -1;
        private int hoveredAction = -1;
        private string statusMessage = "READY";
        private Color statusColor = Good;

        // Explicit confirmation modal for destructive Disk Manager operations (ETAP 9)
        // modalOperation: 0 = none, 1 = prepare/format disk, 2 = format partition FAT32, 3 = delete partition
        private int modalOperation;
        private string modalTargetCode = string.Empty;
        private string modalTitle = string.Empty;
        private string modalDescription = string.Empty;
        private readonly char[] modalConfirmChars = new char[24];
        private int modalConfirmLen;

        public DiskManagerApp(int x, int y, Action onClose) : base("Disk Manager")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 1040, 700, "Disk Manager - ZOnderqOS");
            Window.CloseAction = Close;
            RefreshSnapshot();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (modalOperation != 0)
            {
                HandleModalKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshSnapshot();
                SetStatus("STORAGE INFORMATION REFRESHED", Good);
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                ChangeTab(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                ChangeTab(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                MoveSelection(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                MoveSelection(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                if (tab == 1)
                    ToggleSelectedPartitionMount();
                return;
            }

            char ch = key.KeyChar;
            if (ch == 'r' || ch == 'R')
            {
                if (tab == 0)
                    RescanSelectedDisk();
                else
                {
                    RefreshSnapshot();
                    SetStatus("STORAGE INFORMATION REFRESHED", Good);
                }
            }
            else if ((ch == 'm' || ch == 'M') && tab == 1)
            {
                ToggleSelectedPartitionMount();
            }
            else if (ch == 'f' || ch == 'F')
            {
                if (tab == 0)
                    RequestPrepareSelectedDisk();
                else if (tab == 1)
                    RequestFormatSelectedPartition();
            }
            else if ((ch == 'd' || ch == 'D') && tab == 1)
            {
                RequestDeleteSelectedPartition();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);

            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredRow = -1;
                hoveredTab = -1;
                hoveredAction = -1;
                return;
            }

            if (modalOperation != 0)
            {
                if (leftClicked && !leftWasClicked)
                    HandleModalMouse(mouseX, mouseY);
                return;
            }

            UpdateHover(mouseX, mouseY);
            if (!leftClicked || leftWasClicked)
                return;

            if (hoveredTab >= 0)
            {
                tab = hoveredTab;
                selection = 0;
                offset = 0;
                SetStatus("VIEW CHANGED", SystemTheme.Accent);
                return;
            }

            if (hoveredRow >= 0)
            {
                int index = offset + hoveredRow;
                if (index < CurrentCount())
                {
                    selection = index;
                    KeepSelectionVisible();
                    SetStatus("ITEM SELECTED", Good);
                }
                return;
            }

            if (hoveredAction == 0)
            {
                RefreshSnapshot();
                SetStatus("STORAGE INFORMATION REFRESHED", Good);
            }
            else if (hoveredAction == 1)
            {
                if (tab == 0)
                    RescanSelectedDisk();
                else if (tab == 1)
                    ToggleSelectedPartitionMount();
            }
            else if (hoveredAction == 2)
            {
                if (tab == 0)
                    RequestPrepareSelectedDisk();
                else if (tab == 1)
                    RequestFormatSelectedPartition();
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!IsRunning || Window == null || !Window.Visible || Window.IsMinimized)
                return;

            Window.Render(canvas);
            int contentX = Window.X + 1;
            int contentY = Window.Y + 39;
            int contentWidth = Window.Width - 2;
            int contentHeight = Window.Height - 40;
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            RenderHeader(canvas);
            RenderTabs(canvas);
            KeepSelectionVisible();
            RenderList(canvas);
            if (ShowDetails)
                RenderDetails(canvas);
            RenderActions(canvas);
            if (modalOperation != 0)
                RenderConfirmationModal(canvas);
            RenderStatus(canvas);
        }

        private void RenderHeader(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 49;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Header, x, y, width, 54);
            canvas.DrawRectangle(Border, x, y, width, 54);
            canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, 4, 54);
            IconManager.DrawScaled(canvas, IconType.DiskManager, x + 15, y + 13, 28, 28);
            SmallTextRenderer.Draw(canvas, "DISK MANAGER", x + 55, y + 13, Text);
            SmallTextRenderer.Draw(canvas, "F5 REFRESH  |  ARROWS SELECT  |  ENTER/M MOUNT", x + 55, y + 32, Muted);

            if (Window.Width >= 1000)
            {
                int countX = x + width - 250;
                SmallTextRenderer.Draw(canvas, "DISKS", countX, y + 15, Muted);
                SmallTextRenderer.DrawUInt(canvas, (ulong)deviceCount, countX + 49, y + 15, Text);
                SmallTextRenderer.Draw(canvas, "PART", countX + 90, y + 15, Muted);
                SmallTextRenderer.DrawUInt(canvas, (ulong)partitionCount, countX + 132, y + 15, Text);
                SmallTextRenderer.Draw(canvas, "VFS", countX + 174, y + 15, Muted);
                SmallTextRenderer.DrawUInt(canvas, (ulong)mountCount, countX + 205, y + 15, Text);
            }
        }

        private void RenderTabs(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 111;
            int totalWidth = Window.Width - 36;
            int gap = 8;
            int tabWidth = (totalWidth - gap * 2) / 3;

            for (int i = 0; i < 3; i++)
            {
                int tx = x + i * (tabWidth + gap);
                bool selected = tab == i;
                bool hover = hoveredTab == i;
                Color bg = selected ? PanelSelected : hover ? PanelHover : Panel;
                canvas.DrawFilledRectangle(bg, tx, y, tabWidth, 38);
                canvas.DrawRectangle(selected || hover ? SystemTheme.AccentBorder : Border, tx, y, tabWidth, 38);
                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, tx, y + 35, tabWidth, 3);
                SmallTextRenderer.DrawCentered(canvas, TabLabels[i], tx + 5, y + 16, tabWidth - 10,
                    selected ? Text : Muted);
            }
        }

        private void RenderList(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + 162;
            int width = ListWidth;

            for (int row = 0; row < VisibleRows; row++)
            {
                int index = offset + row;
                int ry = y + row * (RowHeight + RowGap);
                bool exists = index < CurrentCount();
                bool selected = exists && index == selection;
                bool hover = exists && row == hoveredRow;
                Color bg = selected ? PanelSelected : hover ? PanelHover : Panel;

                canvas.DrawFilledRectangle(bg, x, ry, width, RowHeight);
                canvas.DrawRectangle(selected || hover ? SystemTheme.AccentBorder : Border, x, ry, width, RowHeight);
                if (selected)
                    canvas.DrawFilledRectangle(SystemTheme.Accent, x, ry + 5, 3, RowHeight - 10);

                if (!exists)
                {
                    if (row == 0 && CurrentCount() == 0)
                        SmallTextRenderer.Draw(canvas, "NO ITEMS", x + 18, ry + 24, Muted);
                    continue;
                }

                IconType icon = tab == 0 ? IconType.DiskManager : tab == 1 ? IconType.FileManager : IconType.Folder;
                IconManager.DrawScaled(canvas, icon, x + 14, ry + 16, 24, 24);
                RenderListText(canvas, index, x + 50, ry, width - 66, selected);
            }
        }

        private void RenderListText(Canvas canvas, int index, int x, int y, int width, bool selected)
        {
            if (tab == 0)
            {
                SmallTextRenderer.DrawClipped(canvas, deviceNames[index] ?? "DISK", x, y + 15, width - 150, Text);
                SmallTextRenderer.DrawClipped(canvas, deviceDetails[index] ?? string.Empty, x, y + 35, width - 150, Muted);
                DrawSizeChip(canvas, x + width - 135, y + 12, 125, deviceSizeMb[index], selected ? SystemTheme.Accent : Text);
            }
            else if (tab == 1)
            {
                SmallTextRenderer.DrawClipped(canvas, partitionNames[index] ?? "PARTITION", x, y + 15, width - 190, Text);
                SmallTextRenderer.DrawClipped(canvas, partitionDetails[index] ?? string.Empty, x, y + 35, width - 190, Muted);
                string mount = partitionMounts[index];
                if (!string.IsNullOrEmpty(mount))
                    SmallTextRenderer.DrawClipped(canvas, mount, x + width - 180, y + 35, 170,
                        partitionSourceIndex[index] == 0 ? Warning : Good);
                DrawSizeChip(canvas, x + width - 180, y + 8, 170, partitionSizeMb[index], selected ? SystemTheme.Accent : Text);
            }
            else
            {
                SmallTextRenderer.DrawClipped(canvas, mountNames[index] ?? "MOUNT", x, y + 15, width - 150, Text);
                SmallTextRenderer.DrawClipped(canvas, mountDetails[index] ?? string.Empty, x, y + 35, width - 150, Muted);
                SmallTextRenderer.DrawClipped(canvas, mountSources[index] ?? string.Empty, x + width - 130, y + 25, 120,
                    selected ? SystemTheme.Accent : Good);
            }
        }

        private static void DrawSizeChip(Canvas canvas, int x, int y, int width, ulong sizeMb, Color color)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x, y, width, 24);
            canvas.DrawRectangle(Border, x, y, width, 24);
            int numberWidth = SmallTextRenderer.WidthUInt(sizeMb);
            int totalWidth = numberWidth + 20;
            int drawX = x + System.Math.Max(6, (width - totalWidth) / 2);
            SmallTextRenderer.DrawUInt(canvas, sizeMb, drawX, y + 10, color);
            SmallTextRenderer.Draw(canvas, "MB", drawX + numberWidth + 5, y + 10, Muted);
        }

        private void RenderDetails(Canvas canvas)
        {
            int x = Window.X + 18 + ListWidth + 14;
            int y = Window.Y + ListTopOffset;
            int width = Window.Width - ListWidth - 50;
            int height = 382;
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "DETAILS", x + 16, y + 16, Text);
            canvas.DrawLine(Border, x + 16, y + 36, x + width - 16, y + 36);

            if (CurrentCount() <= 0 || selection < 0 || selection >= CurrentCount())
            {
                SmallTextRenderer.Draw(canvas, "NO ITEM SELECTED", x + 16, y + 58, Muted);
                return;
            }

            if (tab == 0)
                RenderDeviceDetails(canvas, x, y, width);
            else if (tab == 1)
                RenderPartitionDetails(canvas, x, y, width);
            else
                RenderMountDetails(canvas, x, y, width);
        }

        private void RenderDeviceDetails(Canvas canvas, int x, int y, int width)
        {
            int index = selection;
            DrawDetailLine(canvas, x, y + 58, width, "NAME", deviceNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "INDEX", deviceIndexText[index], Text);
            DrawDetailLine(canvas, x, y + 118, width, "SIZE", deviceSizeText[index], Text);
            DrawDetailLine(canvas, x, y + 148, width, "PARAMETERS", deviceDetails[index], Muted);
            DrawDetailLine(canvas, x, y + 178, width, "PROTECTION",
                deviceProtected[index] ? "PROTECTED (SYSTEM/BOOT)" : "WRITABLE DISK",
                deviceProtected[index] ? Warning : Good);
            DrawInfoBox(canvas, x + 14, y + 215, width - 28,
                deviceProtected[index]
                    ? "Active system or boot media is protected from formatting."
                    : "R rescans partitions. F prepares disk with MBR + FAT32 (confirmation required).",
                deviceProtected[index] ? Warning : Good);
        }

        private void RenderPartitionDetails(Canvas canvas, int x, int y, int width)
        {
            int index = selection;
            int source = partitionSourceIndex[index];
            string mount = partitionMounts[index];
            bool prot = partitionProtected[index];
            DrawDetailLine(canvas, x, y + 58, width, "PARTITION", partitionNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "FILESYSTEM", partitionFsInfo[index] ?? "Unknown", Text);
            DrawDetailLine(canvas, x, y + 118, width, "SIZE", partitionSizeText[index], Text);
            DrawDetailLine(canvas, x, y + 148, width, "MOUNT", string.IsNullOrEmpty(mount) ? "NOT MOUNTED" : mount,
                string.IsNullOrEmpty(mount) ? Muted : Good);
            DrawDetailLine(canvas, x, y + 178, width, "STATUS", prot ? "PROTECTED SYSTEM/BOOT" : "AVAILABLE",
                prot ? Warning : Good);
            DrawInfoBox(canvas, x + 14, y + 215, width - 28,
                (source == 0 || prot)
                    ? "System/BootGuard partition is protected from formatting or deletion."
                    : "F formats partition to FAT32. D deletes partition (confirmation required).",
                (source == 0 || prot) ? Warning : Good);
        }

        private void RenderMountDetails(Canvas canvas, int x, int y, int width)
        {
            int index = selection;
            DrawDetailLine(canvas, x, y + 58, width, "MOUNT POINT", mountNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "SYSTEM", mountDetails[index], Text);
            DrawDetailLine(canvas, x, y + 118, width, "SOURCE", mountSources[index], Good);
            DrawInfoBox(canvas, x + 14, y + 185, width - 28,
                "VFS view is read-only. Mount volumes in the PARTITIONS tab.", Muted);
        }

        private static void DrawDetailLine(Canvas canvas, int x, int y, int width, string label, string value, Color valueColor)
        {
            SmallTextRenderer.Draw(canvas, label, x + 16, y, Muted);
            SmallTextRenderer.DrawClipped(canvas, value ?? "-", x + 104, y, System.Math.Max(20, width - 120), valueColor);
        }

        private static void DrawInfoBox(Canvas canvas, int x, int y, int width, string text, Color color)
        {
            canvas.DrawFilledRectangle(Color.FromArgb(23, 29, 35), x, y, width, 72);
            canvas.DrawRectangle(Border, x, y, width, 72);
            canvas.DrawFilledRectangle(color, x, y, 3, 72);
            SmallTextRenderer.DrawClipped(canvas, text, x + 12, y + 19, System.Math.Max(20, width - 24), color);
        }

        private void RenderActions(Canvas canvas)
        {
            int y = ActionY;
            int x = Window.X + 18;
            int width = Window.Width - 36;
            int gap = 10;
            int buttonWidth = (width - gap * 2) / 3;

            DrawAction(canvas, 0, x, y, buttonWidth, "REFRESH", "F5", IconType.Refresh, Good);

            if (tab == 0)
            {
                DrawAction(canvas, 1, x + buttonWidth + gap, y, buttonWidth, "SCAN PARTITIONS", "R", IconType.DiskManager, Warning);
                bool prot = selection >= 0 && selection < deviceCount && deviceProtected[selection];
                DrawAction(canvas, 2, x + (buttonWidth + gap) * 2, y, buttonWidth,
                    prot ? "DISK PROTECTED" : "PREPARE DISK",
                    prot ? "SYSTEM / BOOT" : "F (CONFIRM)",
                    IconType.DiskManager, prot ? Muted : Danger);
            }
            else if (tab == 1)
            {
                int source = SelectedPartitionSource();
                string mount = SelectedPartitionMount();
                bool prot = selection >= 0 && selection < partitionCount && partitionProtected[selection];
                DrawAction(canvas, 1, x + buttonWidth + gap, y, buttonWidth,
                    (source == 0 || prot) ? "SYSTEM VOLUME" : string.IsNullOrEmpty(mount) ? "MOUNT" : "UNMOUNT",
                    (source == 0 || prot) ? "PROTECTED" : "ENTER / M",
                    IconType.Folder, (source == 0 || prot) ? Warning : Good);
                DrawAction(canvas, 2, x + (buttonWidth + gap) * 2, y, buttonWidth,
                    prot ? "PROTECTED" : "FORMAT FAT32",
                    prot ? "LOCKED" : "F / D=DEL",
                    IconType.DiskManager, prot ? Muted : Danger);
            }
            else
            {
                DrawAction(canvas, 1, x + buttonWidth + gap, y, buttonWidth, "VFS VIEW ONLY", "PARTITIONS -> MOUNT", IconType.Folder, Muted);
                DrawAction(canvas, 2, x + (buttonWidth + gap) * 2, y, buttonWidth, "READ ONLY", "ACTIVE MOUNTS", IconType.Folder, Muted);
            }
        }

        private void DrawAction(Canvas canvas, int index, int x, int y, int width, string label, string hint,
            IconType icon, Color color)
        {
            bool hover = hoveredAction == index;
            canvas.DrawFilledRectangle(hover ? PanelHover : Panel, x, y, width, 48);
            canvas.DrawRectangle(hover ? SystemTheme.AccentBorder : Border, x, y, width, 48);
            if (hover)
                canvas.DrawFilledRectangle(SystemTheme.Accent, x, y, width, 2);
            IconManager.DrawScaled(canvas, icon, x + 13, y + 13, 22, 22);
            SmallTextRenderer.Draw(canvas, label, x + 46, y + 14, color);
            SmallTextRenderer.DrawClipped(canvas, hint, x + width - 150, y + 29, 135, Muted);
        }

        private void RenderStatus(Canvas canvas)
        {
            int x = Window.X + 18;
            int y = Window.Y + Window.Height - 43;
            int width = Window.Width - 36;
            canvas.DrawFilledRectangle(Color.FromArgb(20, 25, 31), x, y, width, 26);
            canvas.DrawRectangle(Border, x, y, width, 26);
            canvas.DrawFilledRectangle(statusColor, x + 9, y + 10, 5, 5);
            SmallTextRenderer.DrawClipped(canvas, statusMessage, x + 24, y + 10, width - 34, statusColor);
        }

        private void UpdateHover(int mouseX, int mouseY)
        {
            hoveredTab = -1;
            hoveredRow = -1;
            hoveredAction = -1;

            int tabsX = Window.X + 18;
            int tabsY = Window.Y + 111;
            int totalWidth = Window.Width - 36;
            int gap = 8;
            int tabWidth = (totalWidth - gap * 2) / 3;
            for (int i = 0; i < 3; i++)
            {
                int x = tabsX + i * (tabWidth + gap);
                if (Hit(mouseX, mouseY, x, tabsY, tabWidth, 38))
                {
                    hoveredTab = i;
                    return;
                }
            }

            int listX = Window.X + 18;
            int listY = Window.Y + 162;
            for (int row = 0; row < VisibleRows; row++)
            {
                int y = listY + row * (RowHeight + RowGap);
                if (Hit(mouseX, mouseY, listX, y, ListWidth, RowHeight))
                {
                    hoveredRow = row;
                    return;
                }
            }

            int actionY = ActionY;
            int actionGap = 10;
            int actionWidth = (Window.Width - 36 - actionGap * 2) / 3;
            if (Hit(mouseX, mouseY, Window.X + 18, actionY, actionWidth, 48))
                hoveredAction = 0;
            else if (Hit(mouseX, mouseY, Window.X + 18 + actionWidth + actionGap, actionY, actionWidth, 48))
                hoveredAction = 1;
            else if (Hit(mouseX, mouseY, Window.X + 18 + (actionWidth + actionGap) * 2, actionY, actionWidth, 48))
                hoveredAction = 2;
        }

        private void RefreshSnapshot()
        {
            ClearSnapshot();
            LoadDevices();
            LoadPartitions();
            LoadMounts();
            ClampSelection();
        }

        private void ClearSnapshot()
        {
            deviceCount = 0;
            partitionCount = 0;
            mountCount = 0;
            for (int i = 0; i < MaxDevices; i++)
            {
                deviceSourceIndex[i] = -1;
                deviceIndexText[i] = null;
                deviceNames[i] = null;
                deviceDetails[i] = null;
                deviceSizeText[i] = null;
                deviceSizeMb[i] = 0;
                deviceProtected[i] = true;
                deviceTokens[i] = null;
            }
            for (int i = 0; i < MaxPartitions; i++)
            {
                partitionSourceIndex[i] = -1;
                partitionIndexText[i] = null;
                partitionNames[i] = null;
                partitionDetails[i] = null;
                partitionFsInfo[i] = null;
                partitionMounts[i] = null;
                partitionSizeText[i] = null;
                partitionSizeMb[i] = 0;
                partitionProtected[i] = true;
            }
            for (int i = 0; i < MaxMounts; i++)
            {
                mountNames[i] = null;
                mountDetails[i] = null;
                mountSources[i] = null;
            }
        }

        private void LoadDevices()
        {
            try
            {
                int count = StorageManager.DeviceCount;
                for (int source = 0; source < count && deviceCount < MaxDevices; source++)
                {
                    var device = StorageManager.GetDevice(source);
                    if (device == null)
                        continue;

                    int slot = deviceCount++;
                    ulong sizeMb = ToMegabytes(device.BlockCount, device.BlockSize);
                    DiskCatalogEntry inspected = DiskCatalog.InspectDevice(device, source, slot);
                    deviceSourceIndex[slot] = source;
                    deviceIndexText[slot] = source.ToString();
                    deviceNames[slot] = string.IsNullOrEmpty(device.Name) ? "DISK " + source : device.Name;
                    deviceDetails[slot] = inspected.InterfaceType + "  |  " + inspected.PartitionTableType + "  |  " + inspected.FilesystemsSummary;
                    deviceSizeMb[slot] = sizeMb;
                    deviceSizeText[slot] = sizeMb + " MB";
                    deviceProtected[slot] = !inspected.CanInstall || RamRootVolume.IsRamDevice(device);
                    deviceTokens[slot] = inspected.IdentityToken;
                }
            }
            catch
            {
                deviceCount = 0;
            }
        }

        private void LoadPartitions()
        {
            try
            {
                int count = StorageManager.Partitions.Count;
                for (int source = 0; source < count && partitionCount < MaxPartitions; source++)
                {
                    Partition part = StorageManager.Partitions[source];
                    if (part == null)
                        continue;

                    int slot = partitionCount++;
                    ulong sizeMb = ToMegabytes(part.BlockCount, part.BlockSize);
                    partitionSourceIndex[slot] = source;
                    partitionIndexText[slot] = source.ToString();
                    partitionNames[slot] = string.IsNullOrEmpty(part.Name) ? "PARTITION " + source : part.Name;
                    string host = part.Host != null && !string.IsNullOrEmpty(part.Host.Name) ? part.Host.Name : "?";
                    string fs = FilesystemSignatureProbe.ProbePartitionFilesystem(part, out string label);
                    string fsDesc = string.IsNullOrEmpty(label) ? fs : (fs + " (" + label + ")");
                    partitionFsInfo[slot] = fsDesc;
                    partitionDetails[slot] = "HOST " + host + "  |  FS " + fsDesc + "  |  LBA " + part.StartSector;
                    partitionSizeMb[slot] = sizeMb;
                    partitionSizeText[slot] = sizeMb + " MB";
                    string mnt = global::ZonderqOS.GUI.StorageMountManager.GetMountPoint(source);
                    partitionMounts[slot] = mnt;

                    bool hostProtected = part.Host == null ||
                                         RamRootVolume.IsRamPartition(part) ||
                                         DiskCatalog.IsDeviceHostingActiveRoot(part.Host) ||
                                         LimineBootMediaDetector.IsBootOrInstallationMedia(part.Host);
                    partitionProtected[slot] = hostProtected || !string.IsNullOrEmpty(mnt);
                }
            }
            catch
            {
                partitionCount = 0;
            }
        }

        private void LoadMounts()
        {
            try
            {
                foreach (VfsMount mount in VfsManager.Mounts)
                {
                    if (mountCount >= MaxMounts)
                        break;
                    int slot = mountCount++;
                    mountNames[slot] = mount.MountPoint;
                    mountDetails[slot] = "VFS " + mount.Name;
                    mountSources[slot] = "SRC " + mount.Source;
                }
            }
            catch
            {
                mountCount = 0;
            }
        }

        private void RescanSelectedDisk()
        {
            if (tab != 0 || deviceCount <= 0 || selection < 0 || selection >= deviceCount)
            {
                SetStatus("NO DISK SELECTED", Danger);
                return;
            }

            try
            {
                int source = deviceSourceIndex[selection];
                var device = StorageManager.GetDevice(source);
                if (device == null)
                {
                    SetStatus("CANNOT READ DISK", Danger);
                    return;
                }

                StorageManager.RescanPartitions(device);
                RefreshSnapshot();
                SetStatus("PARTITION TABLE RELOADED", Good);
            }
            catch
            {
                SetStatus("DISK RESCAN FAILED", Danger);
            }
        }

        private void ToggleSelectedPartitionMount()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
                return;

            int source = partitionSourceIndex[selection];
            if (source == 0)
            {
                SetStatus("SYSTEM VOLUME / IS PROTECTED", Warning);
                return;
            }

            string mount = partitionMounts[selection];
            string error;
            if (string.IsNullOrEmpty(mount))
            {
                string mountedAt;
                if (global::ZonderqOS.GUI.StorageMountManager.TryMount(source, out mountedAt, out error))
                {
                    RefreshSnapshot();
                    SetStatus("PARTITION MOUNTED", Good);
                }
                else
                {
                    SetStatus(string.IsNullOrEmpty(error) ? "MOUNT FAILED" : error, Danger);
                }
            }
            else
            {
                if (global::ZonderqOS.GUI.StorageMountManager.TryUnmount(source, out error))
                {
                    RefreshSnapshot();
                    SetStatus("PARTITION UNMOUNTED", Good);
                }
                else
                {
                    SetStatus(string.IsNullOrEmpty(error) ? "UNMOUNT FAILED" : error, Danger);
                }
            }
        }

        private void ChangeTab(int delta)
        {
            tab += delta;
            if (tab < 0) tab = 2;
            if (tab > 2) tab = 0;
            selection = 0;
            offset = 0;
            ClampSelection();
            SetStatus("VIEW CHANGED", SystemTheme.Accent);
        }

        private void MoveSelection(int delta)
        {
            int count = CurrentCount();
            if (count <= 0)
                return;
            selection += delta;
            if (selection < 0) selection = 0;
            if (selection >= count) selection = count - 1;
            KeepSelectionVisible();
        }

        private void KeepSelectionVisible()
        {
            if (selection < offset)
                offset = selection;
            if (selection >= offset + VisibleRows)
                offset = selection - VisibleRows + 1;
            int maxOffset = System.Math.Max(0, CurrentCount() - VisibleRows);
            if (offset > maxOffset) offset = maxOffset;
            if (offset < 0) offset = 0;
        }

        private void ClampSelection()
        {
            int count = CurrentCount();
            if (count <= 0)
            {
                selection = 0;
                offset = 0;
                return;
            }
            if (selection >= count) selection = count - 1;
            if (selection < 0) selection = 0;
            KeepSelectionVisible();
        }

        private int CurrentCount()
        {
            return tab == 0 ? deviceCount : tab == 1 ? partitionCount : mountCount;
        }

        private int SelectedPartitionSource()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
                return -1;
            return partitionSourceIndex[selection];
        }

        private string SelectedPartitionMount()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
                return null;
            return partitionMounts[selection];
        }

        private void SetStatus(string message, Color color)
        {
            statusMessage = string.IsNullOrEmpty(message) ? "READY" : message;
            statusColor = color;
        }

        private static ulong ToMegabytes(ulong blocks, ulong blockSize)
        {
            if (blocks == 0 || blockSize == 0)
                return 0;
            return (blocks * blockSize) / 1024UL / 1024UL;
        }

        private static bool Hit(int px, int py, int x, int y, int width, int height)
        {
            return px >= x && px < x + width && py >= y && py < y + height;
        }

        private void RequestPrepareSelectedDisk()
        {
            if (tab != 0 || deviceCount <= 0 || selection < 0 || selection >= deviceCount)
            {
                SetStatus("NO DISK SELECTED", Danger);
                return;
            }

            if (deviceProtected[selection] || deviceTokens[selection] == null)
            {
                SetStatus("BLOCKED: ACTIVE SYSTEM OR INSTALLATION DISK IS PROTECTED", Danger);
                return;
            }

            Array.Clear(modalConfirmChars, 0, modalConfirmChars.Length);
            modalConfirmLen = 0;
            modalOperation = 1;
            modalTargetCode = deviceTokens[selection].DiskId;
            modalTitle = "WARNING: PREPARE & FORMAT DISK (" + modalTargetCode + ")";
            modalDescription = "All existing partitions and data on " + deviceNames[selection] +
                               " (" + deviceSizeText[selection] + ") will be permanently deleted.";
            SetStatus("TYPE " + modalTargetCode + " TO CONFIRM PREPARING DISK", Warning);
        }

        private void RequestFormatSelectedPartition()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
            {
                SetStatus("NO PARTITION SELECTED", Danger);
                return;
            }

            if (partitionProtected[selection] || partitionSourceIndex[selection] == 0)
            {
                SetStatus("BLOCKED: SYSTEM, BOOTGUARD OR MOUNTED PARTITION IS PROTECTED", Danger);
                return;
            }

            Array.Clear(modalConfirmChars, 0, modalConfirmChars.Length);
            modalConfirmLen = 0;
            modalOperation = 2;
            modalTargetCode = "PART-" + partitionSourceIndex[selection];
            modalTitle = "WARNING: FORMAT PARTITION TO FAT32 (" + modalTargetCode + ")";
            modalDescription = "All files on partition " + partitionNames[selection] +
                               " (" + partitionSizeText[selection] + ") will be permanently erased.";
            SetStatus("TYPE " + modalTargetCode + " TO CONFIRM FAT32 FORMAT", Warning);
        }

        private void RequestDeleteSelectedPartition()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
            {
                SetStatus("NO PARTITION SELECTED", Danger);
                return;
            }

            if (partitionProtected[selection] || partitionSourceIndex[selection] == 0)
            {
                SetStatus("BLOCKED: SYSTEM, BOOTGUARD OR MOUNTED PARTITION CANNOT BE DELETED", Danger);
                return;
            }

            Array.Clear(modalConfirmChars, 0, modalConfirmChars.Length);
            modalConfirmLen = 0;
            modalOperation = 3;
            modalTargetCode = "DEL-" + partitionSourceIndex[selection];
            modalTitle = "WARNING: DELETE PARTITION (" + partitionNames[selection] + ")";
            modalDescription = "Partition " + partitionNames[selection] +
                               " (" + partitionSizeText[selection] + ") will be removed from the partition table.";
            SetStatus("TYPE " + modalTargetCode + " TO CONFIRM PARTITION DELETION", Warning);
        }

        private void HandleModalKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
            {
                modalOperation = 0;
                modalConfirmLen = 0;
                SetStatus("OPERATION CANCELLED", Warning);
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (modalConfirmLen > 0)
                    modalConfirmChars[--modalConfirmLen] = '\0';
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                ExecuteConfirmedModalOperation();
                return;
            }

            char ch = key.KeyChar;
            if (ch >= 32 && ch <= 126 && modalConfirmLen < modalConfirmChars.Length)
                modalConfirmChars[modalConfirmLen++] = char.ToUpperInvariant(ch);
        }

        private void HandleModalMouse(int mouseX, int mouseY)
        {
            int mw = 560;
            int mh = 250;
            int mx = Window.X + (Window.Width - mw) / 2;
            int my = Window.Y + (Window.Height - mh) / 2;
            int btnY = my + mh - 52;

            if (Hit(mouseX, mouseY, mx + 24, btnY, 160, 38))
            {
                modalOperation = 0;
                modalConfirmLen = 0;
                SetStatus("OPERATION CANCELLED", Warning);
            }
            else if (Hit(mouseX, mouseY, mx + mw - 224, btnY, 200, 38))
            {
                ExecuteConfirmedModalOperation();
            }
        }

        private void ExecuteConfirmedModalOperation()
        {
            string typed = modalConfirmLen > 0 ? new string(modalConfirmChars, 0, modalConfirmLen) : string.Empty;
            if (!InstallerInputValidator.ValidateDiskConfirmation(modalTargetCode, typed))
            {
                SetStatus("CONFIRMATION MISMATCH: TYPE " + modalTargetCode + " EXACTLY", Danger);
                return;
            }

            int op = modalOperation;
            modalOperation = 0;
            modalConfirmLen = 0;

            if (op == 1)
            {
                if (selection < 0 || selection >= deviceCount || deviceProtected[selection] || deviceTokens[selection] == null)
                {
                    SetStatus("DISK PROTECTED OR UNAVAILABLE", Danger);
                    return;
                }

                if (DiskFormattingEngine.InitializeDiskWithSingleFat32Partition(
                    deviceTokens[selection], typed, "ZONDERQVOL", out string err))
                {
                    RefreshSnapshot();
                    SetStatus("DISK PREPARED AND FORMATTED AS FAT32 (ZONDERQVOL)", Good);
                }
                else
                {
                    SetStatus(err ?? "DISK PREPARATION FAILED", Danger);
                }
            }
            else if (op == 2)
            {
                if (selection < 0 || selection >= partitionCount || partitionProtected[selection])
                {
                    SetStatus("PARTITION PROTECTED", Danger);
                    return;
                }

                int srcIdx = partitionSourceIndex[selection];
                if (srcIdx <= 0 || srcIdx >= StorageManager.Partitions.Count)
                {
                    SetStatus("INVALID PARTITION TARGET", Danger);
                    return;
                }

                Partition part = StorageManager.Partitions[srcIdx];
                if (DiskFormattingEngine.FormatAndVerifyFat32(part, "ZONDERQVOL ", false, out string err))
                {
                    RefreshSnapshot();
                    SetStatus("PARTITION FORMATTED AND VERIFIED AS FAT32", Good);
                }
                else
                {
                    SetStatus(err ?? "PARTITION FORMAT FAILED", Danger);
                }
            }
            else if (op == 3)
            {
                if (selection < 0 || selection >= partitionCount || partitionProtected[selection])
                {
                    SetStatus("PARTITION PROTECTED", Danger);
                    return;
                }

                int srcIdx = partitionSourceIndex[selection];
                if (srcIdx <= 0 || srcIdx >= StorageManager.Partitions.Count)
                {
                    SetStatus("INVALID PARTITION TARGET", Danger);
                    return;
                }

                Partition part = StorageManager.Partitions[srcIdx];
                if (part?.Host == null || DiskCatalog.IsDeviceHostingActiveRoot(part.Host))
                {
                    SetStatus("CANNOT MODIFY ACTIVE SYSTEM DISK", Danger);
                    return;
                }

                try
                {
                    var entries = Mbr.Parse(part.Host);
                    int mbrSlot = -1;
                    for (int i = 0; i < entries.Count; i++)
                    {
                        if (entries[i].StartSector == part.StartSector)
                        {
                            mbrSlot = i;
                            break;
                        }
                    }

                    if (mbrSlot < 0)
                    {
                        SetStatus("COULD NOT LOCATE MBR PARTITION ENTRY", Danger);
                        return;
                    }

                    Mbr.RemovePartition(part.Host, mbrSlot);
                    part.Host.Flush();
                    StorageManager.RescanPartitions(part.Host);
                    RefreshSnapshot();
                    SetStatus("PARTITION DELETED", Good);
                }
                catch (Exception ex)
                {
                    SetStatus("DELETE FAILED: " + ex.Message, Danger);
                }
            }
        }

        private void RenderConfirmationModal(Canvas canvas)
        {
            int mw = 560;
            int mh = 250;
            int mx = Window.X + (Window.Width - mw) / 2;
            int my = Window.Y + (Window.Height - mh) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(42, 20, 24), mx, my, mw, mh);
            canvas.DrawRectangle(Danger, mx, my, mw, mh);
            canvas.DrawFilledRectangle(Danger, mx, my, mw, 34);
            SmallTextRenderer.DrawClipped(canvas, modalTitle, mx + 16, my + 10, mw - 32, Text);

            SmallTextRenderer.DrawClipped(canvas, modalDescription, mx + 20, my + 52, mw - 40, Text);
            SmallTextRenderer.Draw(canvas, "This operation will destroy data and cannot be undone.", mx + 20, my + 74, Warning);
            SmallTextRenderer.Draw(canvas, "Type '" + modalTargetCode + "' below to confirm:", mx + 20, my + 102, Text);

            canvas.DrawFilledRectangle(Surface, mx + 20, my + 124, 280, 34);
            canvas.DrawRectangle(SystemTheme.AccentBorder, mx + 20, my + 124, 280, 34);
            string typed = modalConfirmLen > 0 ? new string(modalConfirmChars, 0, modalConfirmLen) : string.Empty;
            SmallTextRenderer.Draw(canvas, typed + "_", mx + 30, my + 135, Text);

            int btnY = my + mh - 52;
            canvas.DrawFilledRectangle(Panel, mx + 24, btnY, 160, 38);
            canvas.DrawRectangle(Border, mx + 24, btnY, 160, 38);
            SmallTextRenderer.DrawCentered(canvas, "CANCEL (ESC)", mx + 28, btnY + 12, 152, Text);

            bool match = InstallerInputValidator.ValidateDiskConfirmation(modalTargetCode, typed);
            canvas.DrawFilledRectangle(match ? Danger : Panel, mx + mw - 224, btnY, 200, 38);
            canvas.DrawRectangle(match ? Danger : Border, mx + mw - 224, btnY, 200, 38);
            SmallTextRenderer.DrawCentered(canvas, "CONFIRM & ERASE", mx + mw - 220, btnY + 12, 192, match ? Text : Muted);
        }
    }
}
