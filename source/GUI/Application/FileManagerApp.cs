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

        private bool contextMenuVisible;
        private int contextMenuX;
        private int contextMenuY;
        private int dialogMode;
        private string dialogName = "";

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
            HandleMouse(mouseX, mouseY, isClicked, wasClicked, false, false);
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            Window.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked);
            if (!Window.Visible)
                return;

            int localX = mouseX - view.X;
            int localY = mouseY - view.Y;
            if (localX < 0 || localY < 0 || localX >= view.Width || localY >= view.Height)
                return;

            if (rightClicked && !rightWasClicked)
            {
                if (dialogMode == 0)
                {
                    contextMenuX = Math.Max(6, Math.Min(localX, view.Width - 226));
                    contextMenuY = Math.Max(42, Math.Min(localY, view.Height - 126));
                    contextMenuVisible = true;
                    status = "Quick menu";
                }
                return;
            }

            if (!leftClicked || leftWasClicked)
                return;

            if (dialogMode != 0)
            {
                return;
            }

            if (contextMenuVisible)
            {
                if (HandleContextMenuClick(localX, localY))
                    return;
                contextMenuVisible = false;
            }

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

        private bool HandleContextMenuClick(int x, int y)
        {
            if (x < contextMenuX || x >= contextMenuX + 220 ||
                y < contextMenuY || y >= contextMenuY + 116)
                return false;

            int item = (y - contextMenuY) / 29;
            contextMenuVisible = false;

            if (item == 0)
            {
                BeginCreate(1);
                return true;
            }

            if (item == 1)
            {
                BeginCreate(2);
                return true;
            }

            if (item == 2)
            {
                Refresh();
                return true;
            }

            if (item == 3)
            {
                GoUp();
                return true;
            }

            return true;
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
                if (dialogMode == 1)
                {
                    if (File.Exists(path) || Directory.Exists(path))
                    {
                        status = "Already exists";
                        return;
                    }
                    File.WriteAllText(path, "");
                    status = "Created file: " + name;
                }
                else
                {
                    if (Directory.Exists(path) || File.Exists(path))
                    {
                        status = "Already exists";
                        return;
                    }
                    Directory.CreateDirectory(path);
                    status = "Created folder: " + name;
                }

                dialogMode = 0;
                dialogName = "";
                RefreshPreserveStatus();
            }
            catch (Exception ex)
            {
                status = "Create error: " + ex.Message;
                dialogMode = 0;
                dialogName = "";
            }
        }

        private void CancelCreate()
        {
            dialogMode = 0;
            dialogName = "";
            status = "Cancelled";
        }

        private void HandleToolbar(int x)
        {
            if (x < 42)
                GoUp();
            else if (x < 84)
            {
                currentPath = "/root";
                searchText = "";
                Refresh();
            }
            else if (x < 126)
                Refresh();
            else if (x < 168)
            {
                searchText = "";
                status = "Search cleared";
                RefreshPreserveStatus();
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (dialogMode != 0)
            {
                if (key.Key == ConsoleKeyEx.Escape)
                {
                    CancelCreate();
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

            try
            {
                string parent = Directory.GetParent(currentPath)?.FullName;
                if (string.IsNullOrEmpty(parent))
                    parent = "/";

                currentPath = parent.Replace('\\', '/');
                searchText = "";
                Refresh();
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
                status = "No editor available";
        }

        private void Refresh()
        {
            RefreshInternal("");
        }

        private void RefreshPreserveStatus()
        {
            string oldStatus = status;
            RefreshInternal(oldStatus);
        }

        private void RefreshInternal(string statusAfter)
        {
            entries.Clear();
            selectedIndex = -1;
            scrollIndex = 0;
            contextMenuVisible = false;

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

                status = string.IsNullOrEmpty(statusAfter)
                    ? entries.Count + " item(s)"
                    : statusAfter;
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
            DrawButton(canvas, IconType.ArrowUp, 8);
            DrawButton(canvas, IconType.Start, 50);
            DrawButton(canvas, IconType.Refresh, 92);
            DrawButton(canvas, IconType.Search, 134);

            int pathX = X + 180;
            int pathWidth = Math.Max(120, Width - 190);
            canvas.DrawFilledRectangle(Color.White, pathX, Y + 8, pathWidth, 26);
            canvas.DrawRectangle(Color.Silver, pathX, Y + 8, pathWidth, 26);

            string pathText = app.CurrentPath;
            if (!string.IsNullOrEmpty(app.SearchText))
                pathText += " [" + app.SearchText + "]";
            int maxPathChars = Math.Max(8, (pathWidth - 14) / 16);
            if (pathText.Length > maxPathChars)
                pathText = "..." + pathText.Substring(pathText.Length - maxPathChars + 3);
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

                IconManager.Draw(canvas, entry.IsDirectory ? IconType.Folder : IconType.File,
                    X + 10, rowY + 5, Color.White);

                string displayName = entry.Name;
                int maxNameChars = Math.Max(8, (Width - 205) / 16);
                if (displayName.Length > maxNameChars)
                    displayName = displayName.Substring(0, maxNameChars - 3) + "...";

                canvas.DrawString(displayName, font, Color.Black, X + 34, rowY + 4);
                canvas.DrawString(entry.IsDirectory ? "Folder" : "File", font, Color.DimGray, X + Width - 150, rowY + 4);
            }

            int footerY = Y + Height - 34;
            canvas.DrawFilledRectangle(Color.FromArgb(225, 230, 235), X + 4, footerY, Width - 8, 28);

            string statusText = app.Status ?? "";
            int maxStatusChars = Math.Max(8, (Width / 2 - 16) / 16);
            if (statusText.Length > maxStatusChars)
                statusText = statusText.Substring(0, maxStatusChars - 3) + "...";
            canvas.DrawString(statusText, font, Color.DimGray, X + 10, footerY + 3);

            string help = "Enter open | Backspace parent | F5 refresh";
            int helpX = Math.Max(X + Width / 2, X + 220);
            int maxHelpChars = Math.Max(8, (Width - (helpX - X) - 12) / 16);
            if (help.Length > maxHelpChars)
                help = help.Substring(0, maxHelpChars - 3) + "...";
            canvas.DrawString(help, font, Color.DimGray, helpX, footerY + 3);

            if (app.ContextMenuVisible)
                RenderContextMenu(canvas);

            if (app.DialogMode != 0)
                RenderDialog(canvas);
        }

        private void DrawButton(Canvas canvas, IconType type, int offset)
        {
            int bx = X + offset;
            canvas.DrawFilledRectangle(Color.White, bx, Y + 7, 36, 28);
            canvas.DrawRectangle(Color.Silver, bx, Y + 7, 36, 28);
            IconManager.Draw(canvas, type, bx + 9, Y + 12, Color.Black);
        }

        private void RenderContextMenu(Canvas canvas)
        {
            int menuX = X + app.ContextMenuX;
            int menuY = Y + app.ContextMenuY;
            canvas.DrawFilledRectangle(Color.FromArgb(245, 245, 245), menuX + 3, menuY + 3, 220, 116);
            canvas.DrawFilledRectangle(Color.White, menuX, menuY, 220, 116);
            canvas.DrawRectangle(Color.DimGray, menuX, menuY, 220, 116);

            DrawMenuItem(canvas, menuX, menuY, 0, "Nowy plik");
            DrawMenuItem(canvas, menuX, menuY, 1, "Nowy folder");
            DrawMenuItem(canvas, menuX, menuY, 2, "Odśwież");
            DrawMenuItem(canvas, menuX, menuY, 3, "Przejdź wyżej");
        }

        private void DrawMenuItem(Canvas canvas, int x, int y, int index, string text)
        {
            int itemY = y + 4 + index * 29;
            canvas.DrawFilledRectangle(Color.White, x + 4, itemY, 212, 25);
            canvas.DrawString(text, font, Color.Black, x + 12, itemY + 2);
        }

        private void RenderDialog(Canvas canvas)
        {
            int dialogWidth = 500;
            int dialogHeight = 150;
            int dx = X + (Width - dialogWidth) / 2;
            int dy = Y + (Height - dialogHeight) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(40, 40, 40), dx + 4, dy + 4, dialogWidth, dialogHeight);
            canvas.DrawFilledRectangle(Color.WhiteSmoke, dx, dy, dialogWidth, dialogHeight);
            canvas.DrawRectangle(Color.DimGray, dx, dy, dialogWidth, dialogHeight);
            canvas.DrawFilledRectangle(Color.FromArgb(35, 55, 75), dx, dy, dialogWidth, 32);

            string title = app.DialogMode == 1 ? "Utwórz nowy plik" : "Utwórz nowy folder";
            canvas.DrawString(title, font, Color.White, dx + 12, dy + 4);
            canvas.DrawString("Nazwa:", font, Color.Black, dx + 16, dy + 52);

            canvas.DrawFilledRectangle(Color.White, dx + 100, dy + 45, 380, 30);
            canvas.DrawRectangle(Color.Silver, dx + 100, dy + 45, 380, 30);

            string name = app.DialogName ?? "";
            int maxChars = 22;
            if (name.Length > maxChars)
                name = name.Substring(name.Length - maxChars);
            canvas.DrawString(name + "_", font, Color.Black, dx + 108, dy + 49);

            canvas.DrawString("Enter = utwórz    Esc = anuluj", font, Color.DimGray, dx + 16, dy + 105);
        }
    }
}
