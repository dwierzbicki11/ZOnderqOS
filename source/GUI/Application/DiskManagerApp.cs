using System;
using System.Drawing;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Storage;
using Cosmos.Kernel.System.Vfs;
using ZonderqOS.GUI.Icons;

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
        private const int VisibleRows = 6;
        private const int RowHeight = 58;
        private const int RowGap = 7;

        private static readonly string[] TabLabels = { "DYSKI", "PARTYCJE", "MONTOWANIA" };
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
        private readonly string[] deviceNames = new string[MaxDevices];
        private readonly string[] deviceDetails = new string[MaxDevices];
        private readonly ulong[] deviceSizeMb = new ulong[MaxDevices];

        private readonly int[] partitionSourceIndex = new int[MaxPartitions];
        private readonly string[] partitionNames = new string[MaxPartitions];
        private readonly string[] partitionDetails = new string[MaxPartitions];
        private readonly string[] partitionMounts = new string[MaxPartitions];
        private readonly ulong[] partitionSizeMb = new ulong[MaxPartitions];

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
        private int lastMouseX = -1;
        private int lastMouseY = -1;
        private string statusMessage = "GOTOWE";
        private Color statusColor = Good;

        public DiskManagerApp(int x, int y, Action onClose) : base("Menedzer dyskow")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 1040, 700, "Menedzer dyskow - ZOnderqOS");
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

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshSnapshot();
                SetStatus("ODSWIEZONO MAGAZYN DANYCH", Good);
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
                    SetStatus("ODSWIEZONO MAGAZYN DANYCH", Good);
                }
            }
            else if ((ch == 'm' || ch == 'M') && tab == 1)
            {
                ToggleSelectedPartitionMount();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            lastMouseX = mouseX;
            lastMouseY = mouseY;
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);

            if (!Window.Visible || Window.IsMinimized)
            {
                hoveredRow = -1;
                hoveredTab = -1;
                hoveredAction = -1;
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
                SetStatus("ZMIENIONO WIDOK", SystemTheme.Accent);
                return;
            }

            if (hoveredRow >= 0)
            {
                int index = offset + hoveredRow;
                if (index < CurrentCount())
                {
                    selection = index;
                    KeepSelectionVisible();
                    SetStatus("WYBRANO ELEMENT", Good);
                }
                return;
            }

            if (hoveredAction == 0)
            {
                RefreshSnapshot();
                SetStatus("ODSWIEZONO MAGAZYN DANYCH", Good);
            }
            else if (hoveredAction == 1)
            {
                if (tab == 0)
                    RescanSelectedDisk();
                else if (tab == 1)
                    ToggleSelectedPartitionMount();
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
            RenderList(canvas);
            RenderDetails(canvas);
            RenderActions(canvas);
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
            SmallTextRenderer.Draw(canvas, "MENEDZER DYSKOW", x + 55, y + 13, Text);
            SmallTextRenderer.Draw(canvas, "F5 ODSWIEZ  |  STRZALKI WYBOR  |  ENTER/M MONTUJ", x + 55, y + 32, Muted);

            int countX = x + width - 250;
            SmallTextRenderer.Draw(canvas, "DYSKI", countX, y + 15, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)deviceCount, countX + 49, y + 15, Text);
            SmallTextRenderer.Draw(canvas, "PART", countX + 90, y + 15, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)partitionCount, countX + 132, y + 15, Text);
            SmallTextRenderer.Draw(canvas, "VFS", countX + 174, y + 15, Muted);
            SmallTextRenderer.DrawUInt(canvas, (ulong)mountCount, countX + 205, y + 15, Text);
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
            int width = 650;

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
                        SmallTextRenderer.Draw(canvas, "BRAK ELEMENTOW", x + 18, ry + 24, Muted);
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
                SmallTextRenderer.DrawClipped(canvas, deviceNames[index] ?? "DYSK", x, y + 15, width - 150, Text);
                SmallTextRenderer.DrawClipped(canvas, deviceDetails[index] ?? string.Empty, x, y + 35, width - 150, Muted);
                DrawSizeChip(canvas, x + width - 135, y + 12, 125, deviceSizeMb[index], selected ? SystemTheme.Accent : Text);
            }
            else if (tab == 1)
            {
                SmallTextRenderer.DrawClipped(canvas, partitionNames[index] ?? "PARTYCJA", x, y + 15, width - 190, Text);
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
            int x = Window.X + 682;
            int y = Window.Y + 162;
            int width = Window.Width - 700;
            int height = 382;
            canvas.DrawFilledRectangle(Panel, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);
            SmallTextRenderer.Draw(canvas, "SZCZEGOLY", x + 16, y + 16, Text);
            canvas.DrawLine(Border, x + 16, y + 36, x + width - 16, y + 36);

            if (CurrentCount() <= 0 || selection < 0 || selection >= CurrentCount())
            {
                SmallTextRenderer.Draw(canvas, "BRAK WYBRANEGO ELEMENTU", x + 16, y + 58, Muted);
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
            DrawDetailLine(canvas, x, y + 58, width, "NAZWA", deviceNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "INDEKS", deviceSourceIndex[index].ToString(), Text);
            DrawDetailLine(canvas, x, y + 118, width, "ROZMIAR", deviceSizeMb[index].ToString() + " MB", Text);
            DrawDetailLine(canvas, x, y + 148, width, "PARAMETRY", deviceDetails[index], Muted);
            DrawInfoBox(canvas, x + 14, y + 205, width - 28,
                "R ponownie odczytuje tablice partycji wybranego dysku. Operacja nie formatuje nosnika.", Warning);
        }

        private void RenderPartitionDetails(Canvas canvas, int x, int y, int width)
        {
            int index = selection;
            int source = partitionSourceIndex[index];
            string mount = partitionMounts[index];
            DrawDetailLine(canvas, x, y + 58, width, "PARTYCJA", partitionNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "INDEKS", source.ToString(), Text);
            DrawDetailLine(canvas, x, y + 118, width, "ROZMIAR", partitionSizeMb[index].ToString() + " MB", Text);
            DrawDetailLine(canvas, x, y + 148, width, "MOUNT", string.IsNullOrEmpty(mount) ? "NIEZAMONTOWANA" : mount,
                string.IsNullOrEmpty(mount) ? Muted : Good);
            DrawInfoBox(canvas, x + 14, y + 205, width - 28,
                source == 0 ? "Wolumin systemowy / jest chroniony przed odmontowaniem." :
                "ENTER lub M montuje/odmontowuje FAT. Brak automatycznego formatowania.",
                source == 0 ? Warning : Good);
        }

        private void RenderMountDetails(Canvas canvas, int x, int y, int width)
        {
            int index = selection;
            DrawDetailLine(canvas, x, y + 58, width, "PUNKT", mountNames[index], Text);
            DrawDetailLine(canvas, x, y + 88, width, "SYSTEM", mountDetails[index], Text);
            DrawDetailLine(canvas, x, y + 118, width, "ZRODLO", mountSources[index], Good);
            DrawInfoBox(canvas, x + 14, y + 185, width - 28,
                "Widok VFS jest tylko informacyjny. Montowanie wykonuj w zakladce PARTYCJE.", Muted);
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
            int y = Window.Y + 558;
            int x = Window.X + 18;
            int width = Window.Width - 36;
            int gap = 10;
            int buttonWidth = (width - gap) / 2;

            DrawAction(canvas, 0, x, y, buttonWidth, "ODSWIEZ", "F5", IconType.Refresh, Good);

            string label;
            string hint;
            IconType icon;
            Color color;
            if (tab == 0)
            {
                label = "SKANUJ PARTYCJE";
                hint = "R";
                icon = IconType.DiskManager;
                color = Warning;
            }
            else if (tab == 1)
            {
                int source = SelectedPartitionSource();
                string mount = SelectedPartitionMount();
                label = source == 0 ? "WOLUMIN SYSTEMOWY" : string.IsNullOrEmpty(mount) ? "MONTUJ" : "ODMONTUJ";
                hint = source == 0 ? "CHRONIONY" : "ENTER / M";
                icon = IconType.Folder;
                color = source == 0 ? Warning : Good;
            }
            else
            {
                label = "TYLKO PODGLAD VFS";
                hint = "PARTYCJE -> MOUNT";
                icon = IconType.Folder;
                color = Muted;
            }

            DrawAction(canvas, 1, x + buttonWidth + gap, y, buttonWidth, label, hint, icon, color);
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
                if (Hit(mouseX, mouseY, listX, y, 650, RowHeight))
                {
                    hoveredRow = row;
                    return;
                }
            }

            int actionY = Window.Y + 558;
            int actionWidth = (Window.Width - 36 - 10) / 2;
            if (Hit(mouseX, mouseY, Window.X + 18, actionY, actionWidth, 48))
                hoveredAction = 0;
            else if (Hit(mouseX, mouseY, Window.X + 18 + actionWidth + 10, actionY, actionWidth, 48))
                hoveredAction = 1;
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
                deviceNames[i] = null;
                deviceDetails[i] = null;
                deviceSizeMb[i] = 0;
            }
            for (int i = 0; i < MaxPartitions; i++)
            {
                partitionSourceIndex[i] = -1;
                partitionNames[i] = null;
                partitionDetails[i] = null;
                partitionMounts[i] = null;
                partitionSizeMb[i] = 0;
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
                    deviceSourceIndex[slot] = source;
                    deviceNames[slot] = string.IsNullOrEmpty(device.Name) ? "DYSK " + source : device.Name;
                    deviceDetails[slot] = "BLOCK " + device.BlockSize + " B  |  " + device.BlockCount + " BLOKOW";
                    deviceSizeMb[slot] = ToMegabytes(device.BlockCount, device.BlockSize);
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
                    partitionSourceIndex[slot] = source;
                    partitionNames[slot] = string.IsNullOrEmpty(part.Name) ? "PARTYCJA " + source : part.Name;
                    string host = part.Host != null && !string.IsNullOrEmpty(part.Host.Name) ? part.Host.Name : "?";
                    partitionDetails[slot] = "HOST " + host + "  |  LBA " + part.StartSector + "  |  BLOCK " + part.BlockSize;
                    partitionSizeMb[slot] = ToMegabytes(part.BlockCount, part.BlockSize);
                    partitionMounts[slot] = global::ZonderqOS.GUI.StorageMountManager.GetMountPoint(source);
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
                foreach (VfsManager.VfsMount mount in VfsManager.Mounts)
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
                SetStatus("BRAK WYBRANEGO DYSKU", Danger);
                return;
            }

            try
            {
                int source = deviceSourceIndex[selection];
                var device = StorageManager.GetDevice(source);
                if (device == null)
                {
                    SetStatus("NIE MOZNA ODCZYTAC DYSKU", Danger);
                    return;
                }

                StorageManager.RescanPartitions(device);
                RefreshSnapshot();
                SetStatus("PONOWNIE ODCZYTANO TABLICE PARTYCJI", Good);
            }
            catch
            {
                SetStatus("BLAD PONOWNEGO SKANOWANIA DYSKU", Danger);
            }
        }

        private void ToggleSelectedPartitionMount()
        {
            if (tab != 1 || partitionCount <= 0 || selection < 0 || selection >= partitionCount)
                return;

            int source = partitionSourceIndex[selection];
            if (source == 0)
            {
                SetStatus("WOLUMIN SYSTEMOWY / JEST CHRONIONY", Warning);
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
                    SetStatus("ZAMONTOWANO PARTYCJE", Good);
                }
                else
                {
                    SetStatus(string.IsNullOrEmpty(error) ? "NIE UDALO SIE ZAMONTOWAC" : error, Danger);
                }
            }
            else
            {
                if (global::ZonderqOS.GUI.StorageMountManager.TryUnmount(source, out error))
                {
                    RefreshSnapshot();
                    SetStatus("ODMONTOWANO PARTYCJE", Good);
                }
                else
                {
                    SetStatus(string.IsNullOrEmpty(error) ? "NIE UDALO SIE ODMONTOWAC" : error, Danger);
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
            SetStatus("ZMIENIONO WIDOK", SystemTheme.Accent);
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
            statusMessage = string.IsNullOrEmpty(message) ? "GOTOWE" : message;
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
    }
}
