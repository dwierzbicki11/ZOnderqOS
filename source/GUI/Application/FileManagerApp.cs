using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public class FileManagerApp : Application
    {
        private readonly Action<string> nanoLauncher;
        private readonly FileManagerView view;
        private readonly List<FileEntry> entries = new List<FileEntry>();
        private string currentPath = "/root";
        private string searchText = "";
        private int selectedIndex = -1;
        private int scrollIndex;
        private int lastClickIndex = -1;
        private int clickFrame = -1000;
        private int frameCounter;
        private string status = "";

        public FileManagerApp(int x, int y, Action<string> openFile) : base("File Manager")
        {
            nanoLauncher = openFile;
            Window = new Window(x, y, 820, 520, "ZonderqOS File Manager");
            Window.CloseAction = Close;
            view = new FileManagerView(10, 40, 800, 465, this);
            Window.AddChild(view);
            Refresh();
        }

        public override void Update()
        {
            frameCounter++;
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(360, Window.Width - 20);
            view.Height = Math.Max(220, Window.Height - 50);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool isClicked, bool wasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, isClicked, wasClicked);
            if (!isClicked || wasClicked || !Window.Visible)
                return;

            int localX = mouseX - view.X;
            int localY = mouseY - view.Y;
            if (localX < 0 || localY < 0 || localX >= view.Width || localY >= view.Height)
                return;

            if (localY >= 4 && localY < 38)
            {
                HandleToolbar(localX);
                return;
            }

            int listTop = 82;
            int rowHeight = 28;
            if (localY >= listTop)
            {
                int index = scrollIndex + (localY - listTop) / rowHeight;
                if (index >= 0 && index < entries.Count)
                {
                    if (selectedIndex == index && lastClickIndex == index && frameCounter - clickFrame <= 25)
                        OpenEntry(index);
                    else
                        selectedIndex = index;

                    lastClickIndex = index;
                    clickFrame = frameCounter;
                    EnsureSelectionVisible();
                }
            }
        }

        private void HandleToolbar(int x)
        {
            if (x < 42)
            {
                GoUp();
            }
            else if (x < 84)
            {
                currentPath = "/root";
                searchText = "";
                Refresh();
            }
            else if (x < 126)
            {
                Refresh();
            }
            else if (x < 168)
            {
                searchText = "";
                status = "Search cleared";
                Refresh();
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                GoUp();
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                SelectRelative(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                SelectRelative(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                if (selectedIndex >= 0 && selectedIndex < entries.Count)
                    OpenEntry(selectedIndex);
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                Refresh();
                return;
            }

            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                if (searchText.Length < 48)
                    searchText += key.KeyChar;
                Refresh();
            }
        }

        private void SelectRelative(int delta)
        {
            if (entries.Count == 0)
                return;

            if (selectedIndex < 0)
                selectedIndex = delta > 0 ? 0 : entries.Count - 1;
            else
                selectedIndex = Math.Max(0, Math.Min(entries.Count - 1, selectedIndex + delta));

            EnsureSelectionVisible();
        }

        private void EnsureSelectionVisible()
        {
            int visible = Math.Max(1, (view.Height - 112) / 28);
            if (selectedIndex < scrollIndex)
                scrollIndex = selectedIndex;
            else if (selectedIndex >= scrollIndex + visible)
                scrollIndex = selectedIndex - visible + 1;

            int maxScroll = Math.Max(0, entries.Count - visible);
            if (scrollIndex > maxScroll) scrollIndex = maxScroll;
            if (scrollIndex < 0) scrollIndex = 0;
        }

        private void GoUp()
        {
            if (currentPath == "/")
            {
                status = "Already at root";
                return;
            }

            string parent = Directory.GetParent(currentPath)?.FullName;
            if (string.IsNullOrEmpty(parent))
                parent = "/";

            currentPath = parent.Replace('\\', '/');
            searchText = "";
            Refresh();
        }

        private void OpenEntry(int index)
        {
            if (index < 0 || index >= entries.Count)
                return;

            FileEntry entry = entries[index];
            if (entry.IsDirectory)
            {
                currentPath = entry.FullPath;
                searchText = "";
                Refresh();
                return;
            }

            if (nanoLauncher != null)
            {
                nanoLauncher(entry.FullPath);
                status = "Opened " + entry.Name;
            }
            else
            {
                status = "No editor available";
            }
        }

        private void Refresh()
        {
            entries.Clear();
            selectedIndex = -1;
            scrollIndex = 0;
            status = "";

            try
            {
                if (!Directory.Exists(currentPath))
                {
                    currentPath = "/";
                    if (!Directory.Exists(currentPath))
                    {
                        status = "Directory not found";
                        return;
                    }
                }

                string[] directories = Directory.GetDirectories(currentPath);
                string[] files = Directory.GetFiles(currentPath);

                AddEntries(directories, true);
                AddEntries(files, false);
                SortEntries();

                if (!string.IsNullOrEmpty(searchText))
                    FilterEntries();

                status = entries.Count + " item(s)";
            }
            catch (Exception ex)
            {
                status = "Read error: " + ex.Message;
            }
        }

        private void AddEntries(string[] paths, bool directory)
        {
            if (paths == null) return;
            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string name = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(name)) name = path;
                entries.Add(new FileEntry(name, path.Replace('\\', '/'), directory));
            }
        }

        private void SortEntries()
        {
            for (int i = 1; i < entries.Count; i++)
            {
                FileEntry value = entries[i];
                int j = i - 1;
                while (j >= 0 && CompareEntries(entries[j], value) > 0)
                {
                    entries[j + 1] = entries[j];
                    j--;
                }
                entries[j + 1] = value;
            }
        }

        private int CompareEntries(FileEntry a, FileEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void FilterEntries()
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

    internal sealed class FileManagerView : Widget
    {
        private readonly FileManagerApp app;
        private readonly Font font = PCScreenFont.DefaultFont;

        public FileManagerView(int x, int y, int width, int height, FileManagerApp owner)
            : base(x, y, width, height)
        {
            app = owner;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(Color.WhiteSmoke, X, Y, Width, Height);
            canvas.DrawRectangle(Color.Gray, X, Y, Width, Height);

            canvas.DrawFilledRectangle(Color.FromArgb(225, 230, 235), X + 4, Y + 4, Width - 8, 34);
            DrawButton(canvas, IconType.ArrowUp, 8, "Up");
            DrawButton(canvas, IconType.Start, 50, "Home");
            DrawButton(canvas, IconType.Refresh, 92, "Refresh");
            DrawButton(canvas, IconType.Search, 134, "Search");

            int pathX = X + 180;
            canvas.DrawFilledRectangle(Color.White, pathX, Y + 8, Math.Max(120, Width - 190), 26);
            canvas.DrawRectangle(Color.Silver, pathX, Y + 8, Math.Max(120, Width - 190), 26);
            string pathText = app.CurrentPath;
            if (!string.IsNullOrEmpty(app.SearchText))
                pathText += "   [search: " + app.SearchText + "]";
            canvas.DrawString(pathText, font, Color.Black, pathX + 7, Y + 11);

            int listY = Y + 46;
            canvas.DrawFilledRectangle(Color.FromArgb(35, 55, 75), X + 4, listY, Width - 8, 30);
            canvas.DrawString("Name", font, Color.White, X + 34, listY + 4);
            canvas.DrawString("Type", font, Color.White, X + Width - 150, listY + 4);

            int rowHeight = 28;
            int visible = Math.Max(1, (Height - 112) / rowHeight);
            int start = app.ScrollIndex;
            for (int i = 0; i < visible; i++)
            {
                int index = start + i;
                if (index >= app.Entries.Count) break;

                FileEntry entry = app.Entries[index];
                int rowY = listY + 30 + i * rowHeight;
                bool selected = index == app.SelectedIndex;

                canvas.DrawFilledRectangle(selected ? Color.LightSteelBlue : Color.White,
                    X + 4, rowY, Width - 8, rowHeight);
                canvas.DrawLine(Color.Gainsboro, X + 4, rowY + rowHeight - 1, X + Width - 4, rowY + rowHeight - 1);

                IconManager.Draw(canvas,
                    entry.IsDirectory ? IconType.Folder : IconType.File,
                    X + 10, rowY + 5, Color.White);
                canvas.DrawString(entry.Name, font, Color.Black, X + 34, rowY + 4);
                canvas.DrawString(entry.IsDirectory ? "Folder" : "File", font, Color.DimGray, X + Width - 150, rowY + 4);
            }

            int footerY = Y + Height - 34;
            canvas.DrawFilledRectangle(Color.FromArgb(225, 230, 235), X + 4, footerY, Width - 8, 28);
            canvas.DrawString(app.Status, font, Color.DimGray, X + 10, footerY + 3);
            canvas.DrawString("Enter: open    Backspace: parent    F5: refresh", font, Color.DimGray,
                X + Math.Max(10, Width - 410), footerY + 3);
        }

        private void DrawButton(Canvas canvas, IconType type, int offset, string label)
        {
            int bx = X + offset;
            canvas.DrawFilledRectangle(Color.White, bx, Y + 7, 36, 28);
            canvas.DrawRectangle(Color.Silver, bx, Y + 7, 36, 28);
            IconManager.Draw(canvas, type, bx + 9, Y + 12, Color.Black);
        }
    }
}
