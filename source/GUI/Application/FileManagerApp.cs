using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using Cosmos.Kernel.System.Storage;
using ZonderqOS.GUI.Icons;
using Font = Cosmos.Kernel.System.Graphics.Fonts.Font;

namespace ZonderqOS.GUI.Apps
{
    public class FileManagerApp : Application
    {
        private readonly Action<string> nanoLauncher;
        private readonly FileManagerView view;
        private readonly List<FileEntry> entries = new List<FileEntry>();
        private readonly List<SidebarEntry> sidebarEntries = new List<SidebarEntry>();

        private string currentPath = "/root";
        private string searchText = "";
        private string status = "";
        private int selectedIndex = -1;
        private int scrollIndex;
        private int lastClickIndex = -1;
        private int clickFrame = -1000;
        private int frameCounter;
        private bool contextMenuVisible;
        private int contextMenuX;
        private int contextMenuY;
        private int dialogMode;
        private string dialogName = "";

        internal const int ToolbarTop = 4;
        internal const int ToolbarHeight = 40;
        internal const int ContentTop = 50;
        internal const int FooterHeight = 24;
        internal const int SidebarWidth = 176;
        internal const int TileWidth = 112;
        internal const int TileHeight = 94;
        internal const int TileGap = 6;
        internal const int SidebarSectionHeight = 22;
        internal const int SidebarItemHeight = 36;

        public FileManagerApp(int x, int y, Action<string> openFile) : base("File Manager")
        {
            nanoLauncher = openFile;
            Window = new Window(x, y, 820, 520, "ZonderqOS File Manager");
            Window.CloseAction = Close;
            view = new FileManagerView(10, 40, 800, 465, this);
            Window.AddChild(view);
            RebuildSidebar();
            Refresh();
        }

        public override void Update()
        {
            frameCounter++;
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(480, Window.Width - 20);
            view.Height = Math.Max(260, Window.Height - 50);
        }

        public override void HandleMouse(int x, int y, bool clicked, bool wasClicked)
        {
            HandleMouse(x, y, clicked, wasClicked, false, false);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft, bool right, bool oldRight)
        {
            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            if (!Window.Visible)
                return;

            int x = mouseX - view.X;
            int y = mouseY - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)
                return;

            int contentBottom = view.Height - FooterHeight - 8;

            if (right && !oldRight)
            {
                if (dialogMode == 0 && x >= SidebarWidth + 6 && y >= ContentTop && y < contentBottom)
                {
                    contextMenuX = Math.Max(SidebarWidth + 8, Math.Min(x, view.Width - 226));
                    contextMenuY = Math.Max(ContentTop, Math.Min(y, view.Height - 126));
                    contextMenuVisible = true;
                }
                return;
            }

            if (!left || oldLeft || dialogMode != 0)
                return;

            if (contextMenuVisible)
            {
                if (ContextClick(x, y))
                    return;
                contextMenuVisible = false;
            }

            if (y >= ToolbarTop && y < ToolbarTop + ToolbarHeight)
            {
                Toolbar(x);
                return;
            }

            if (y < ContentTop || y >= contentBottom)
                return;

            if (x < SidebarWidth + 4)
            {
                SidebarClick(y);
                return;
            }

            int gridLeft = SidebarWidth + 8;
            int columns = Columns();
            int col = (x - gridLeft) / (TileWidth + TileGap);
            int row = (y - ContentTop) / (TileHeight + TileGap);
            if (col < 0 || col >= columns || row < 0)
                return;

            int index = scrollIndex + row * columns + col;
            if (index < 0 || index >= entries.Count)
                return;

            if (selectedIndex == index && lastClickIndex == index && frameCounter - clickFrame <= 25)
                OpenEntry(index);
            else
                selectedIndex = index;

            lastClickIndex = index;
            clickFrame = frameCounter;
            EnsureSelectionVisible();
        }

        private bool ContextClick(int x, int y)
        {
            if (x < contextMenuX || x >= contextMenuX + 220 || y < contextMenuY || y >= contextMenuY + 116)
                return false;

            int item = (y - contextMenuY) / 29;
            contextMenuVisible = false;

            if (item == 0)
                BeginCreate(1);
            else if (item == 1)
                BeginCreate(2);
            else if (item == 2)
                RefreshAll();
            else if (item == 3)
                GoUp();

            return true;
        }

        private void Toolbar(int x)
        {
            if (x < 42)
            {
                GoUp();
            }
            else if (x < 84)
            {
                NavigateTo("/root");
            }
            else if (x < 126)
            {
                RefreshAll();
            }
            else if (x < 168)
            {
                searchText = "";
                status = "Search cleared";
                RefreshKeepStatus();
            }
        }

        private void SidebarClick(int y)
        {
            int itemY = ContentTop + 8;
            for (int i = 0; i < sidebarEntries.Count; i++)
            {
                SidebarEntry entry = sidebarEntries[i];
                int height = entry.IsSection ? SidebarSectionHeight : SidebarItemHeight;

                if (!entry.IsSection && y >= itemY && y < itemY + height)
                {
                    if (entry.Navigable && !string.IsNullOrEmpty(entry.Path))
                    {
                        NavigateTo(entry.Path);
                    }
                    else
                    {
                        status = string.IsNullOrEmpty(entry.Detail)
                            ? entry.Label
                            : entry.Label + ": " + entry.Detail;
                    }
                    return;
                }

                itemY += height;
                if (itemY >= view.Height - FooterHeight - 10)
                    break;
            }
        }

        private void NavigateTo(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !Directory.Exists(path))
                {
                    status = "Location unavailable";
                    return;
                }

                currentPath = path.Replace('\\', '/');
                searchText = "";
                Refresh();
            }
            catch (Exception ex)
            {
                status = "Navigation error: " + ex.Message;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (dialogMode != 0)
            {
                if (key.Key == ConsoleKeyEx.Escape)
                {
                    dialogMode = 0;
                    dialogName = "";
                    status = "Cancelled";
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    FinishCreate();
                    return;
                }

                if (key.Key == ConsoleKeyEx.Backspace)
                {
                    if (dialogName.Length > 0)
                        dialogName = dialogName.Substring(0, dialogName.Length - 1);
                    return;
                }

                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && dialogName.Length < 64)
                    dialogName += key.KeyChar;
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                if (contextMenuVisible)
                {
                    contextMenuVisible = false;
                    return;
                }

                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                GoUp();
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                Select(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                Select(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                Select(-Columns());
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                Select(Columns());
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                if (selectedIndex >= 0)
                    OpenEntry(selectedIndex);
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                RefreshAll();
                return;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                if (searchText.Length < 48)
                    searchText += key.KeyChar;
                Refresh();
            }
        }

        private int Columns()
        {
            int gridWidth = Math.Max(TileWidth, view.Width - SidebarWidth - 14);
            return Math.Max(1, (gridWidth + TileGap) / (TileWidth + TileGap));
        }

        private void Select(int delta)
        {
            if (entries.Count == 0)
                return;

            if (selectedIndex < 0)
                selectedIndex = delta >= 0 ? 0 : entries.Count - 1;
            else
                selectedIndex = Math.Max(0, Math.Min(entries.Count - 1, selectedIndex + delta));

            EnsureSelectionVisible();
        }

        private void EnsureSelectionVisible()
        {
            int cols = Columns();
            int availableHeight = Math.Max(TileHeight, view.Height - ContentTop - FooterHeight - 12);
            int rows = Math.Max(1, (availableHeight + TileGap) / (TileHeight + TileGap));
            int visible = cols * rows;

            if (selectedIndex < scrollIndex)
                scrollIndex = selectedIndex;
            else if (selectedIndex >= scrollIndex + visible)
                scrollIndex = selectedIndex - visible + cols;

            scrollIndex -= scrollIndex % cols;
            if (scrollIndex < 0)
                scrollIndex = 0;

            int max = Math.Max(0, entries.Count - visible);
            if (scrollIndex > max)
                scrollIndex = Math.Max(0, max - max % cols);
        }

        private void GoUp()
        {
            if (currentPath == "/")
            {
                status = "Already at root";
                return;
            }

            try
            {
                string parent = Directory.GetParent(currentPath)?.FullName;
                NavigateTo(string.IsNullOrEmpty(parent) ? "/" : parent);
            }
            catch (Exception ex)
            {
                status = "Parent error: " + ex.Message;
            }
        }

        private void OpenEntry(int index)
        {
            if (index < 0 || index >= entries.Count)
                return;

            FileEntry entry = entries[index];
            if (entry.IsDirectory)
            {
                NavigateTo(entry.FullPath);
            }
            else if (nanoLauncher != null)
            {
                nanoLauncher(entry.FullPath);
                status = "Opened " + entry.Name;
            }
        }

        private void BeginCreate(int mode)
        {
            dialogMode = mode;
            dialogName = "";
            status = mode == 1 ? "New file: type a name" : "New folder: type a name";
        }

        private void FinishCreate()
        {
            string name = (dialogName ?? "").Trim();
            if (string.IsNullOrEmpty(name))
            {
                status = "Name cannot be empty";
                return;
            }

            if (name == "." || name == ".." || name.IndexOf('/') >= 0 || name.IndexOf('\\') >= 0)
            {
                status = "Invalid name";
                return;
            }

            string path = Path.Combine(currentPath, name).Replace('\\', '/');
            try
            {
                if (File.Exists(path) || Directory.Exists(path))
                {
                    status = "Already exists";
                    return;
                }

                if (dialogMode == 1)
                {
                    File.WriteAllText(path, "");
                    status = "Created file: " + name;
                }
                else
                {
                    Directory.CreateDirectory(path);
                    status = "Created folder: " + name;
                }

                dialogMode = 0;
                dialogName = "";
                RefreshKeepStatus();
            }
            catch (Exception ex)
            {
                status = "Create error: " + ex.Message;
                dialogMode = 0;
                dialogName = "";
            }
        }

        private void RefreshAll()
        {
            RebuildSidebar();
            Refresh();
        }

        private void Refresh()
        {
            RefreshInternal("");
        }

        private void RefreshKeepStatus()
        {
            RefreshInternal(status);
        }

        private void RefreshInternal(string keep)
        {
            entries.Clear();
            selectedIndex = -1;
            scrollIndex = 0;
            contextMenuVisible = false;

            try
            {
                if (!Directory.Exists(currentPath))
                    currentPath = "/";

                Add(Directory.GetDirectories(currentPath), true);
                Add(Directory.GetFiles(currentPath), false);
                Sort();

                if (!string.IsNullOrEmpty(searchText))
                    Filter();

                status = string.IsNullOrEmpty(keep) ? entries.Count + " item(s)" : keep;
            }
            catch (Exception ex)
            {
                status = "Read error: " + ex.Message;
            }
        }

        private void RebuildSidebar()
        {
            sidebarEntries.Clear();
            sidebarEntries.Add(SidebarEntry.Section("PLACES"));
            sidebarEntries.Add(SidebarEntry.Location("Home", "/root", IconType.Start, "USER FILES"));
            sidebarEntries.Add(SidebarEntry.Location("Root", "/", IconType.Folder, "FILESYSTEM"));

            if (Directory.Exists("/home"))
                sidebarEntries.Add(SidebarEntry.Location("Users", "/home", IconType.Folder, "/HOME"));
            if (Directory.Exists("/mnt"))
                sidebarEntries.Add(SidebarEntry.Location("Mounts", "/mnt", IconType.Folder, "/MNT"));

            sidebarEntries.Add(SidebarEntry.Section("VOLUMES"));

            try
            {
                int partitionCount = StorageManager.Partitions.Count;
                if (partitionCount > 0)
                {
                    var rootPartition = StorageManager.Partitions[0];
                    ulong rootBytes = (ulong)rootPartition.BlockCount * (ulong)rootPartition.BlockSize;
                    sidebarEntries.Add(SidebarEntry.Location("System", "/", IconType.FileManager,
                        FormatCapacity(rootBytes) + "  MOUNTED"));

                    for (int i = 1; i < partitionCount && i < 5; i++)
                    {
                        var partition = StorageManager.Partitions[i];
                        ulong bytes = (ulong)partition.BlockCount * (ulong)partition.BlockSize;
                        sidebarEntries.Add(SidebarEntry.Device("Partition " + i, IconType.FileManager,
                            FormatCapacity(bytes) + "  NOT MOUNTED"));
                    }
                }
                else
                {
                    sidebarEntries.Add(SidebarEntry.Device("No volumes", IconType.FileManager, "NONE DETECTED"));
                }
            }
            catch
            {
                sidebarEntries.Add(SidebarEntry.Device("System", IconType.FileManager, "STORAGE READY"));
            }

            sidebarEntries.Add(SidebarEntry.Section("DEVICES"));

            try
            {
                int deviceCount = StorageManager.DeviceCount;
                for (int i = 0; i < deviceCount && i < 5; i++)
                {
                    var device = StorageManager.GetDevice(i);
                    ulong bytes = (ulong)device.BlockCount * (ulong)device.BlockSize;
                    sidebarEntries.Add(SidebarEntry.Device("Disk " + i, IconType.Settings,
                        FormatCapacity(bytes) + "  DEVICE"));
                }

                if (deviceCount == 0)
                    sidebarEntries.Add(SidebarEntry.Device("No disks", IconType.Settings, "NONE DETECTED"));
            }
            catch
            {
                sidebarEntries.Add(SidebarEntry.Device("Storage", IconType.Settings, "UNAVAILABLE"));
            }
        }

        private static string FormatCapacity(ulong bytes)
        {
            ulong megabytes = bytes / (1024UL * 1024UL);
            if (megabytes >= 1024)
            {
                ulong wholeGb = megabytes / 1024;
                ulong tenths = (megabytes % 1024) * 10 / 1024;
                return wholeGb + "." + tenths + " GB";
            }

            return megabytes + " MB";
        }

        private void Add(string[] paths, bool directory)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string name = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(name))
                    name = path;

                entries.Add(new FileEntry(name, path.Replace('\\', '/'), directory));
            }
        }

        private void Sort()
        {
            for (int i = 1; i < entries.Count; i++)
            {
                FileEntry value = entries[i];
                int j = i - 1;
                while (j >= 0 && Compare(entries[j], value) > 0)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }

                entries[j + 1] = value;
            }
        }

        private int Compare(FileEntry a, FileEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;

            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void Filter()
        {
            string filter = searchText.ToLowerInvariant();
            for (int i = entries.Count - 1; i >= 0; i--)
            {
                if (!entries[i].Name.ToLowerInvariant().Contains(filter))
                    entries.RemoveAt(i);
            }
        }

        public string CurrentPath { get { return currentPath; } }
        public string SearchText { get { return searchText; } }
        public string Status { get { return status; } }
        public int SelectedIndex { get { return selectedIndex; } }
        public int ScrollIndex { get { return scrollIndex; } }
        public List<FileEntry> Entries { get { return entries; } }
        public List<SidebarEntry> SidebarEntries { get { return sidebarEntries; } }
        public bool ContextMenuVisible { get { return contextMenuVisible; } }
        public int ContextMenuX { get { return contextMenuX; } }
        public int ContextMenuY { get { return contextMenuY; } }
        public int DialogMode { get { return dialogMode; } }
        public string DialogName { get { return dialogName; } }
    }

    public sealed class FileEntry
    {
        public readonly string Name;
        public readonly string FullPath;
        public readonly bool IsDirectory;

        public FileEntry(string name, string fullPath, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }
    }

    public sealed class SidebarEntry
    {
        public readonly string Label;
        public readonly string Path;
        public readonly string Detail;
        public readonly IconType Icon;
        public readonly bool Navigable;
        public readonly bool IsSection;

        private SidebarEntry(string label, string path, IconType icon, string detail, bool navigable, bool isSection)
        {
            Label = label;
            Path = path;
            Icon = icon;
            Detail = detail;
            Navigable = navigable;
            IsSection = isSection;
        }

        public static SidebarEntry Section(string label)
        {
            return new SidebarEntry(label, null, IconType.Folder, "", false, true);
        }

        public static SidebarEntry Location(string label, string path, IconType icon, string detail)
        {
            return new SidebarEntry(label, path, icon, detail, true, false);
        }

        public static SidebarEntry Device(string label, IconType icon, string detail)
        {
            return new SidebarEntry(label, null, icon, detail, false, false);
        }
    }

    internal sealed class FileManagerView : Widget
    {
        private readonly FileManagerApp app;
        private readonly Font font = PCScreenFont.DefaultFont;

        private static readonly Color Chrome = Color.FromArgb(30, 35, 41);
        private static readonly Color ChromeRaised = Color.FromArgb(40, 47, 55);
        private static readonly Color ChromeBorder = Color.FromArgb(68, 78, 90);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color ContentBackground = Color.FromArgb(28, 33, 39);
        private static readonly Color SidebarBackground = Color.FromArgb(24, 29, 35);
        private static readonly Color FooterText = Color.FromArgb(190, 199, 208);
        private static readonly Color MainText = Color.FromArgb(224, 230, 236);
        private static readonly Color SecondaryText = Color.FromArgb(142, 156, 170);

        public FileManagerView(int x, int y, int width, int height, FileManagerApp owner) : base(x, y, width, height)
        {
            app = owner;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            canvas.DrawFilledRectangle(Chrome, X, Y, Width, Height);
            canvas.DrawRectangle(ChromeBorder, X, Y, Width, Height);

            RenderToolbar(canvas);

            int footerY = Y + Height - FileManagerApp.FooterHeight - 4;
            int contentY = Y + FileManagerApp.ContentTop;
            int contentH = Math.Max(40, footerY - contentY - 4);
            int sidebarX = X + 4;
            int sidebarW = FileManagerApp.SidebarWidth;
            int gridX = X + FileManagerApp.SidebarWidth + 8;
            int gridW = Math.Max(FileManagerApp.TileWidth, Width - FileManagerApp.SidebarWidth - 12);

            canvas.DrawFilledRectangle(SidebarBackground, sidebarX, contentY, sidebarW, contentH);
            canvas.DrawRectangle(Color.FromArgb(53, 62, 72), sidebarX, contentY, sidebarW, contentH);
            RenderSidebar(canvas, sidebarX, contentY, sidebarW, contentH);

            canvas.DrawFilledRectangle(ContentBackground, gridX, contentY, gridW, contentH);
            canvas.DrawRectangle(Color.FromArgb(53, 62, 72), gridX, contentY, gridW, contentH);
            Grid(canvas, gridX, contentY, gridW, contentH);

            RenderFooter(canvas, footerY);

            if (app.ContextMenuVisible)
                Menu(canvas);
            if (app.DialogMode != 0)
                Dialog(canvas);
        }

        private void RenderToolbar(Canvas canvas)
        {
            canvas.DrawFilledRectangle(ChromeRaised, X + 4, Y + 4, Width - 8, 40);
            ToolbarButton(canvas, IconType.ArrowUp, 8);
            ToolbarButton(canvas, IconType.Start, 50);
            ToolbarButton(canvas, IconType.Refresh, 92);
            ToolbarButton(canvas, IconType.Search, 134);

            int pathX = X + 180;
            int pathW = Math.Max(120, Width - 188);
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), pathX, Y + 9, pathW, 30);
            canvas.DrawRectangle(Color.FromArgb(82, 94, 108), pathX, Y + 9, pathW, 30);
            canvas.DrawFilledRectangle(Accent, pathX, Y + 9, 3, 30);

            SmallTextRenderer.DrawClipped(canvas, app.CurrentPath, pathX + 10, Y + 20,
                Math.Max(20, pathW - 20), MainText);

            if (!string.IsNullOrEmpty(app.SearchText) && pathW >= 250)
            {
                int searchWidth = Math.Min(170, pathW / 3);
                int searchX = pathX + pathW - searchWidth - 8;
                canvas.DrawFilledRectangle(Color.FromArgb(35, 47, 58), searchX, Y + 14, searchWidth, 20);
                IconManager.DrawScaled(canvas, IconType.Search, searchX + 4, Y + 16, 14, 14);
                SmallTextRenderer.DrawClipped(canvas, app.SearchText, searchX + 22, Y + 21,
                    searchWidth - 27, Color.FromArgb(166, 202, 231));
            }
        }

        private void RenderSidebar(Canvas canvas, int x, int y, int width, int height)
        {
            int itemY = y + 8;
            List<SidebarEntry> items = app.SidebarEntries;

            for (int i = 0; i < items.Count; i++)
            {
                SidebarEntry entry = items[i];
                int rowHeight = entry.IsSection ? FileManagerApp.SidebarSectionHeight : FileManagerApp.SidebarItemHeight;
                if (itemY + rowHeight > y + height)
                    break;

                if (entry.IsSection)
                {
                    SmallTextRenderer.DrawClipped(canvas, entry.Label, x + 10, itemY + 7,
                        width - 20, Color.FromArgb(104, 130, 151));
                    itemY += rowHeight;
                    continue;
                }

                bool active = entry.Navigable && entry.Path == app.CurrentPath;
                if (active)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(37, 62, 83), x + 4, itemY + 1, width - 8, rowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, x + 4, itemY + 1, 3, rowHeight - 2);
                }

                int iconY = itemY + 8;
                canvas.DrawFilledRectangle(Color.FromArgb(31, 38, 45), x + 10, iconY - 3, 26, 26);
                IconManager.DrawScaled(canvas, entry.Icon, x + 14, iconY + 1, 18, 18);

                SmallTextRenderer.DrawClipped(canvas, entry.Label, x + 44, itemY + 8,
                    width - 52, active ? Color.WhiteSmoke : MainText);
                SmallTextRenderer.DrawClipped(canvas, entry.Detail, x + 44, itemY + 21,
                    width - 52, active ? Color.FromArgb(143, 190, 226) : SecondaryText);

                itemY += rowHeight;
            }
        }

        private void RenderFooter(Canvas canvas, int footerY)
        {
            int footerX = X + 4;
            int footerW = Width - 8;

            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), footerX, footerY, footerW, FileManagerApp.FooterHeight);
            canvas.DrawLine(Color.FromArgb(72, 84, 96), footerX, footerY, footerX + footerW, footerY);

            string hint = Width >= 700
                ? "ENTER OPEN   F5 REFRESH   BACKSPACE UP"
                : "ENTER OPEN   F5 REFRESH";

            int hintWidth = SmallTextRenderer.Width(hint);
            int hintX = Math.Max(X + Width / 2, X + Width - 10 - hintWidth);
            int hintMaxWidth = Math.Max(0, X + Width - 10 - hintX);
            int statusX = X + 10;
            int statusMaxWidth = Math.Max(18, hintX - statusX - 12);
            int textY = footerY + 8;

            SmallTextRenderer.DrawClipped(canvas, app.Status ?? "", statusX, textY, statusMaxWidth, FooterText);
            SmallTextRenderer.DrawClipped(canvas, hint, hintX, textY, hintMaxWidth, SecondaryText);
        }

        private void Grid(Canvas canvas, int x, int y, int width, int height)
        {
            int iconSize = 42;
            int labelWidth = 88;
            int labelHeight = 18;
            int cols = Math.Max(1, (width + FileManagerApp.TileGap) /
                (FileManagerApp.TileWidth + FileManagerApp.TileGap));
            int rows = Math.Max(1, (height + FileManagerApp.TileGap) /
                (FileManagerApp.TileHeight + FileManagerApp.TileGap));
            int count = cols * rows;

            for (int i = 0; i < count; i++)
            {
                int index = app.ScrollIndex + i;
                if (index >= app.Entries.Count)
                    break;

                FileEntry entry = app.Entries[index];
                int tileX = x + i % cols * (FileManagerApp.TileWidth + FileManagerApp.TileGap);
                int tileY = y + i / cols * (FileManagerApp.TileHeight + FileManagerApp.TileGap);

                if (index == app.SelectedIndex)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(41, 66, 87), tileX, tileY,
                        FileManagerApp.TileWidth, FileManagerApp.TileHeight);
                    canvas.DrawRectangle(Accent, tileX, tileY,
                        FileManagerApp.TileWidth, FileManagerApp.TileHeight);
                    canvas.DrawFilledRectangle(Accent, tileX, tileY, 3, FileManagerApp.TileHeight);
                }

                int iconX = tileX + (FileManagerApp.TileWidth - iconSize) / 2;
                canvas.DrawFilledRectangle(Color.FromArgb(34, 40, 47), iconX - 5, tileY + 3, iconSize + 10, iconSize + 8);
                IconManager.DrawScaled(canvas, entry.IsDirectory ? IconType.Folder : IconType.File,
                    iconX, tileY + 7, iconSize, iconSize);

                DrawWrappedName(canvas, entry.Name ?? "",
                    tileX + (FileManagerApp.TileWidth - labelWidth) / 2,
                    tileY + iconSize + 15,
                    labelWidth,
                    labelHeight,
                    index == app.SelectedIndex ? Color.WhiteSmoke : MainText);
            }
        }

        private void DrawWrappedName(Canvas canvas, string name, int x, int y, int width, int height, Color color)
        {
            const int glyphWidth = 5;
            const int spacing = 1;
            const int lineHeight = 9;
            int maxChars = Math.Max(1, (width + spacing) / (glyphWidth + spacing));

            if (string.IsNullOrEmpty(name))
                return;

            int position = 0;
            int maxLines = Math.Max(1, height / lineHeight);

            for (int line = 0; line < maxLines && position < name.Length; line++)
            {
                int take = Math.Min(maxChars, name.Length - position);
                if (position + take < name.Length)
                {
                    int breakAt = name.LastIndexOf(' ', position + take - 1, take);
                    if (breakAt >= position)
                        take = breakAt - position;
                }

                if (take <= 0)
                    take = Math.Min(maxChars, name.Length - position);

                bool truncated = position + take < name.Length && line == maxLines - 1;
                int drawTake = truncated && take >= 3 ? take - 3 : take;
                int textWidth = drawTake * 6 - (drawTake > 0 ? 1 : 0);
                if (truncated)
                    textWidth += 18;

                int textX = x + Math.Max(0, (width - textWidth) / 2);
                SmallTextRenderer.DrawRange(canvas, name, position, drawTake, textX, y + line * lineHeight, color);
                if (truncated)
                    SmallTextRenderer.Draw(canvas, "...", textX + drawTake * 6, y + line * lineHeight, color);

                position += take;
                while (position < name.Length && name[position] == ' ')
                    position++;
            }
        }

        private void ToolbarButton(Canvas canvas, IconType type, int offset)
        {
            int buttonX = X + offset;
            canvas.DrawFilledRectangle(Color.FromArgb(51, 59, 68), buttonX, Y + 7, 36, 30);
            canvas.DrawRectangle(Color.FromArgb(83, 96, 110), buttonX, Y + 7, 36, 30);
            IconManager.Draw(canvas, type, buttonX + 9, Y + 11, Color.WhiteSmoke);
        }

        private void Menu(Canvas canvas)
        {
            int x = X + app.ContextMenuX;
            int y = Y + app.ContextMenuY;

            canvas.DrawFilledRectangle(Color.FromArgb(20, 24, 29), x + 3, y + 3, 220, 116);
            canvas.DrawFilledRectangle(Color.FromArgb(40, 47, 55), x, y, 220, 116);
            canvas.DrawRectangle(Color.FromArgb(85, 102, 118), x, y, 220, 116);

            DrawMenuItem(canvas, "New file", x, y + 3);
            DrawMenuItem(canvas, "New folder", x, y + 32);
            DrawMenuItem(canvas, "Refresh", x, y + 61);
            DrawMenuItem(canvas, "Go up", x, y + 90);
        }

        private void DrawMenuItem(Canvas canvas, string text, int x, int y)
        {
            SmallTextRenderer.Draw(canvas, text, x + 10, y + 10, MainText);
        }

        private void Dialog(Canvas canvas)
        {
            int width = Math.Min(430, Math.Max(280, Width - 40));
            int height = 126;
            int x = X + (Width - width) / 2;
            int y = Y + (Height - height) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(20, 24, 29), x + 4, y + 4, width, height);
            canvas.DrawFilledRectangle(Color.FromArgb(38, 44, 51), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);

            string title = app.DialogMode == 1 ? "New file" : "New folder";
            canvas.DrawString(title, font, Color.FromArgb(235, 239, 243), x + 12, y + 10);

            canvas.DrawFilledRectangle(Color.FromArgb(26, 31, 37), x + 12, y + 42, width - 24, 30);
            canvas.DrawRectangle(Color.FromArgb(83, 96, 110), x + 12, y + 42, width - 24, 30);
            SmallTextRenderer.DrawClipped(canvas, app.DialogName + "_", x + 18, y + 54,
                width - 36, Color.FromArgb(226, 232, 238));

            SmallTextRenderer.Draw(canvas, "ENTER CREATE   ESC CANCEL", x + 12, y + 94, SecondaryText);
        }
    }
}
