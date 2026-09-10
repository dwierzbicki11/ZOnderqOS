using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public class FileManagerApp : Application
    {
        private readonly Action<string> nanoLauncher;
        private readonly FileManagerView view;
        private readonly List<FileEntry> entries = new List<FileEntry>();
        private string currentPath = "/root", searchText = "", status = "";
        private int selectedIndex = -1, scrollIndex, lastClickIndex = -1, clickFrame = -1000, frameCounter;
        private bool contextMenuVisible;
        private int contextMenuX, contextMenuY, dialogMode;
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
            view.X = Window.X + 10; view.Y = Window.Y + 40;
            view.Width = Math.Max(360, Window.Width - 20); view.Height = Math.Max(220, Window.Height - 50);
        }

        public override void HandleMouse(int x, int y, bool clicked, bool wasClicked) { HandleMouse(x, y, clicked, wasClicked, false, false); }

        public override void HandleMouse(int mx, int my, bool left, bool oldLeft, bool right, bool oldRight)
        {
            Window.HandleMouse(mx, my, left, oldLeft);
            if (!Window.Visible) return;
            int x = mx - view.X, y = my - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height) return;
            if (right && !oldRight)
            {
                if (dialogMode == 0)
                {
                    contextMenuX = Math.Max(6, Math.Min(x, view.Width - 226));
                    contextMenuY = Math.Max(78, Math.Min(y, view.Height - 126));
                    contextMenuVisible = true;
                }
                return;
            }
            if (!left || oldLeft || dialogMode != 0) return;
            if (contextMenuVisible)
            {
                if (ContextClick(x, y)) return;
                contextMenuVisible = false;
            }
            if (y >= 4 && y < 42) { Toolbar(x); return; }
            if (y < 78) return;

            int tileW = 112, tileH = 94, gap = 6;
            int columns = Math.Max(1, (view.Width - 8 + gap) / (tileW + gap));
            int col = Math.Max(0, (x - 4) / (tileW + gap));
            int row = Math.Max(0, (y - 78) / (tileH + gap));
            if (col >= columns) return;
            int index = scrollIndex + row * columns + col;
            if (index < 0 || index >= entries.Count) return;
            if (selectedIndex == index && lastClickIndex == index && frameCounter - clickFrame <= 25) OpenEntry(index);
            else selectedIndex = index;
            lastClickIndex = index; clickFrame = frameCounter; EnsureSelectionVisible();
        }

        private bool ContextClick(int x, int y)
        {
            if (x < contextMenuX || x >= contextMenuX + 220 || y < contextMenuY || y >= contextMenuY + 116) return false;
            int item = (y - contextMenuY) / 29; contextMenuVisible = false;
            if (item == 0) BeginCreate(1); else if (item == 1) BeginCreate(2); else if (item == 2) Refresh(); else if (item == 3) GoUp();
            return true;
        }

        private void Toolbar(int x)
        {
            if (x < 42) GoUp();
            else if (x < 84) { currentPath = "/root"; searchText = ""; Refresh(); }
            else if (x < 126) Refresh();
            else if (x < 168) { searchText = ""; status = "Search cleared"; RefreshKeepStatus(); }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (dialogMode != 0)
            {
                if (key.Key == ConsoleKeyEx.Escape) { dialogMode = 0; dialogName = ""; status = "Cancelled"; return; }
                if (key.Key == ConsoleKeyEx.Enter) { FinishCreate(); return; }
                if (key.Key == ConsoleKeyEx.Backspace) { if (dialogName.Length > 0) dialogName = dialogName.Substring(0, dialogName.Length - 1); return; }
                if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar) && dialogName.Length < 64) dialogName += key.KeyChar;
                return;
            }
            if (key.Key == ConsoleKeyEx.Escape) { if (contextMenuVisible) { contextMenuVisible = false; return; } Close(); return; }
            if (key.Key == ConsoleKeyEx.Backspace) { GoUp(); return; }
            if (key.Key == ConsoleKeyEx.LeftArrow) { Select(-1); return; }
            if (key.Key == ConsoleKeyEx.RightArrow) { Select(1); return; }
            if (key.Key == ConsoleKeyEx.UpArrow) { Select(-Columns()); return; }
            if (key.Key == ConsoleKeyEx.DownArrow) { Select(Columns()); return; }
            if (key.Key == ConsoleKeyEx.Enter) { if (selectedIndex >= 0) OpenEntry(selectedIndex); return; }
            if (key.Key == ConsoleKeyEx.F5) { Refresh(); return; }
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar)) { if (searchText.Length < 48) searchText += key.KeyChar; Refresh(); }
        }

        private int Columns() { return Math.Max(1, (view.Width - 8 + 6) / 118); }
        private void Select(int delta)
        {
            if (entries.Count == 0) return;
            if (selectedIndex < 0) selectedIndex = delta >= 0 ? 0 : entries.Count - 1;
            else selectedIndex = Math.Max(0, Math.Min(entries.Count - 1, selectedIndex + delta));
            EnsureSelectionVisible();
        }
        private void EnsureSelectionVisible()
        {
            int cols = Columns(), rows = Math.Max(1, (view.Height - 112) / 100), visible = cols * rows;
            if (selectedIndex < scrollIndex) scrollIndex = selectedIndex;
            else if (selectedIndex >= scrollIndex + visible) scrollIndex = selectedIndex - visible + cols;
            scrollIndex -= scrollIndex % cols;
            if (scrollIndex < 0) scrollIndex = 0;
            int max = Math.Max(0, entries.Count - visible); if (scrollIndex > max) scrollIndex = Math.Max(0, max - max % cols);
        }

        private void GoUp()
        {
            if (currentPath == "/") { status = "Already at root"; return; }
            try { string p = Directory.GetParent(currentPath)?.FullName; currentPath = string.IsNullOrEmpty(p) ? "/" : p.Replace('\\', '/'); searchText = ""; Refresh(); }
            catch (Exception ex) { status = "Parent error: " + ex.Message; }
        }

        private void OpenEntry(int index)
        {
            if (index < 0 || index >= entries.Count) return;
            FileEntry e = entries[index];
            if (e.IsDirectory) { currentPath = e.FullPath; searchText = ""; Refresh(); }
            else if (nanoLauncher != null) { nanoLauncher(e.FullPath); status = "Opened " + e.Name; }
        }

        private void BeginCreate(int mode) { dialogMode = mode; dialogName = ""; status = mode == 1 ? "New file: type a name" : "New folder: type a name"; }
        private void FinishCreate()
        {
            string n = (dialogName ?? "").Trim();
            if (string.IsNullOrEmpty(n)) { status = "Name cannot be empty"; return; }
            if (n == "." || n == ".." || n.IndexOf('/') >= 0 || n.IndexOf('\\') >= 0) { status = "Invalid name"; return; }
            string p = Path.Combine(currentPath, n).Replace('\\', '/');
            try
            {
                if (File.Exists(p) || Directory.Exists(p)) { status = "Already exists"; return; }
                if (dialogMode == 1) { File.WriteAllText(p, ""); status = "Created file: " + n; }
                else { Directory.CreateDirectory(p); status = "Created folder: " + n; }
                dialogMode = 0; dialogName = ""; RefreshKeepStatus();
            }
            catch (Exception ex) { status = "Create error: " + ex.Message; dialogMode = 0; dialogName = ""; }
        }

        private void Refresh() { RefreshInternal(""); }
        private void RefreshKeepStatus() { RefreshInternal(status); }
        private void RefreshInternal(string keep)
        {
            entries.Clear(); selectedIndex = -1; scrollIndex = 0; contextMenuVisible = false;
            try
            {
                if (!Directory.Exists(currentPath)) currentPath = "/";
                Add(Directory.GetDirectories(currentPath), true); Add(Directory.GetFiles(currentPath), false); Sort();
                if (!string.IsNullOrEmpty(searchText)) Filter();
                status = string.IsNullOrEmpty(keep) ? entries.Count + " item(s)" : keep;
            }
            catch (Exception ex) { status = "Read error: " + ex.Message; }
        }
        private void Add(string[] paths, bool dir)
        {
            if (paths == null) return;
            for (int i = 0; i < paths.Length; i++) { string p = paths[i]; string n = Path.GetFileName(p.TrimEnd('/', '\\')); if (string.IsNullOrEmpty(n)) n = p; entries.Add(new FileEntry(n, p.Replace('\\', '/'), dir)); }
        }
        private void Sort()
        {
            for (int i = 1; i < entries.Count; i++) { FileEntry v = entries[i]; int j = i - 1; while (j >= 0 && Compare(entries[j], v) > 0) { entries[j + 1] = entries[j]; j--; } entries[j + 1] = v; }
        }
        private int Compare(FileEntry a, FileEntry b) { if (a.IsDirectory != b.IsDirectory) return a.IsDirectory ? -1 : 1; return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase); }
        private void Filter() { string f = searchText.ToLowerInvariant(); for (int i = entries.Count - 1; i >= 0; i--) if (!entries[i].Name.ToLowerInvariant().Contains(f)) entries.RemoveAt(i); }

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
        public readonly string Name, FullPath; public readonly bool IsDirectory;
        public FileEntry(string name, string fullPath, bool isDirectory) { Name = name; FullPath = fullPath; IsDirectory = isDirectory; }
    }

    internal sealed class FileManagerView : Widget
    {
        private readonly FileManagerApp app;
        private readonly Font font = PCScreenFont.DefaultFont;
        public FileManagerView(int x, int y, int width, int height, FileManagerApp owner) : base(x, y, width, height) { app = owner; }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;
            canvas.DrawFilledRectangle(Color.WhiteSmoke, X, Y, Width, Height); canvas.DrawRectangle(Color.Gray, X, Y, Width, Height);
            canvas.DrawFilledRectangle(Color.FromArgb(225, 230, 235), X + 4, Y + 4, Width - 8, 38);
            Button(canvas, IconType.ArrowUp, 8); Button(canvas, IconType.Start, 50); Button(canvas, IconType.Refresh, 92); Button(canvas, IconType.Search, 134);
            int pathX = X + 180, pathW = Math.Max(120, Width - 190);
            canvas.DrawFilledRectangle(Color.White, pathX, Y + 9, pathW, 26); canvas.DrawRectangle(Color.Silver, pathX, Y + 9, pathW, 26);
            string path = app.CurrentPath + (string.IsNullOrEmpty(app.SearchText) ? "" : " [" + app.SearchText + "]");
            int max = Math.Max(8, (pathW - 14) / 16); if (path.Length > max) path = "..." + path.Substring(path.Length - max + 3);
            canvas.DrawString(path, font, Color.Black, pathX + 7, Y + 11);

            int listX = X + 4, listY = Y + 48, listW = Width - 8, listH = Height - 84;
            canvas.DrawFilledRectangle(Color.White, listX, listY, listW, listH); Grid(canvas, listX, listY, listW, listH);

            int fy = Y + Height - 34; canvas.DrawFilledRectangle(Color.FromArgb(225, 230, 235), X + 4, fy, Width - 8, 28);
            string s = app.Status ?? ""; int sm = Math.Max(8, (Width / 2 - 16) / 16); if (s.Length > sm) s = s.Substring(0, sm - 3) + "...";
            canvas.DrawString(s, font, Color.DimGray, X + 10, fy + 3);
            canvas.DrawString("Double click open | Enter | F5 refresh", font, Color.DimGray, X + Width / 2, fy + 3);
            if (app.ContextMenuVisible) Menu(canvas); if (app.DialogMode != 0) Dialog(canvas);
        }

        private void Grid(Canvas canvas, int x, int y, int w, int h)
        {
            int tw = 112, th = 94, gap = 6, cols = Math.Max(1, (w + gap) / (tw + gap)), rows = Math.Max(1, h / (th + gap));
            int count = cols * rows;
            for (int i = 0; i < count; i++)
            {
                int idx = app.ScrollIndex + i; if (idx >= app.Entries.Count) break; FileEntry e = app.Entries[idx];
                int tx = x + i % cols * (tw + gap), ty = y + i / cols * (th + gap);
                if (idx == app.SelectedIndex) { canvas.DrawFilledRectangle(Color.FromArgb(220, 232, 247), tx, ty, tw, th); canvas.DrawRectangle(Color.FromArgb(105, 150, 205), tx, ty, tw, th); }
                IconManager.Draw(canvas, e.IsDirectory ? IconType.Folder : IconType.File, tx + 40, ty + 8, Color.White);
                string n = e.Name ?? ""; if (n.Length > 11) n = n.Substring(0, 8) + "...";
                // The label is kept on its own line under the icon, like desktop file managers.
                int nx = tx + Math.Max(4, (tw - n.Length * 16) / 2); canvas.DrawString(n, font, Color.FromArgb(30, 30, 30), nx, ty + 58);
            }
        }

        private void Button(Canvas c, IconType type, int off) { int bx = X + off; c.DrawFilledRectangle(Color.White, bx, Y + 7, 36, 30); c.DrawRectangle(Color.Silver, bx, Y + 7, 36, 30); IconManager.Draw(c, type, bx + 9, Y + 11, Color.Black); }
        private void Menu(Canvas c)
        {
            int x = X + app.ContextMenuX, y = Y + app.ContextMenuY; c.DrawFilledRectangle(Color.FromArgb(245, 245, 245), x + 3, y + 3, 220, 116); c.DrawFilledRectangle(Color.White, x, y, 220, 116); c.DrawRectangle(Color.DimGray, x, y, 220, 116);
            string[] a = { "Nowy plik", "Nowy folder", "Odśwież", "Przejdź wyżej" }; for (int i = 0; i < 4; i++) c.DrawString(a[i], font, Color.Black, x + 12, y + 6 + i * 29);
        }
        private void Dialog(Canvas c)
        {
            int w = 500, h = 150, x = X + (Width - w) / 2, y = Y + (Height - h) / 2; c.DrawFilledRectangle(Color.FromArgb(40, 40, 40), x + 4, y + 4, w, h); c.DrawFilledRectangle(Color.WhiteSmoke, x, y, w, h); c.DrawRectangle(Color.DimGray, x, y, w, h); c.DrawFilledRectangle(Color.FromArgb(35, 55, 75), x, y, w, 32);
            c.DrawString(app.DialogMode == 1 ? "Utwórz nowy plik" : "Utwórz nowy folder", font, Color.White, x + 12, y + 4); c.DrawString("Nazwa:", font, Color.Black, x + 16, y + 52); c.DrawFilledRectangle(Color.White, x + 100, y + 45, 380, 30); c.DrawRectangle(Color.Silver, x + 100, y + 45, 380, 30);
            string n = app.DialogName ?? ""; if (n.Length > 22) n = n.Substring(n.Length - 22); c.DrawString(n + "_", font, Color.Black, x + 108, y + 49); c.DrawString("Enter = utwórz    Esc = anuluj", font, Color.DimGray, x + 16, y + 105);
        }
    }
}
