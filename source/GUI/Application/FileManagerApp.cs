using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;
using Font = Cosmos.Kernel.System.Graphics.Fonts.Font;

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

        private const int ToolbarTop = 4;
        private const int ToolbarHeight = 40;
        private const int ContentTop = 50;
        private const int FooterHeight = 24;
        private const int TileWidth = 112;
        private const int TileHeight = 94;
        private const int TileGap = 6;

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
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(360, Window.Width - 20);
            view.Height = Math.Max(220, Window.Height - 50);
        }

        public override void HandleMouse(int x, int y, bool clicked, bool wasClicked)
        {
            HandleMouse(x, y, clicked, wasClicked, false, false);
        }

        public override void HandleMouse(int mx, int my, bool left, bool oldLeft, bool right, bool oldRight)
        {
            Window.HandleMouse(mx, my, left, oldLeft);
            if (!Window.Visible)
                return;

            int x = mx - view.X;
            int y = my - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)
                return;

            if (right && !oldRight)
            {
                if (dialogMode == 0)
                {
                    contextMenuX = Math.Max(6, Math.Min(x, view.Width - 226));
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

            int contentBottom = view.Height - FooterHeight - 8;
            if (y < ContentTop || y >= contentBottom || x < 4)
                return;

            int columns = Columns();
            int col = (x - 4) / (TileWidth + TileGap);
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
                Refresh();
            else if (item == 3)
                GoUp();

            return true;
        }

        private void Toolbar(int x)
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
                RefreshKeepStatus();
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

        private int Columns()
        {
            return Math.Max(1, (view.Width - 8 + TileGap) / (TileWidth + TileGap));
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
                string p = Directory.GetParent(currentPath)?.FullName;
                currentPath = string.IsNullOrEmpty(p) ? "/" : p.Replace('\\', '/');
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

        private void Add(string[] paths, bool dir)
        {
            if (paths == null)
                return;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                string name = Path.GetFileName(path.TrimEnd('/', '\\'));
                if (string.IsNullOrEmpty(name))
                    name = path;

                entries.Add(new FileEntry(name, path.Replace('\\', '/'), dir));
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
        private static readonly string[] CharCache = BuildCharCache();

        private const int ContentTop = 50;
        private const int FooterHeight = 24;
        private const int TileWidth = 112;
        private const int TileHeight = 94;
        private const int TileGap = 6;

        private static readonly Color Chrome = Color.FromArgb(32, 38, 45);
        private static readonly Color ChromeRaised = Color.FromArgb(43, 50, 58);
        private static readonly Color ChromeBorder = Color.FromArgb(68, 78, 90);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color ContentBackground = Color.FromArgb(245, 247, 249);
        private static readonly Color FooterText = Color.FromArgb(190, 199, 208);

        private static string[] BuildCharCache()
        {
            string[] cache = new string[256];
            for (int i = 0; i < cache.Length; i++)
                cache[i] = ((char)i).ToString();
            return cache;
        }

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

            canvas.DrawFilledRectangle(ChromeRaised, X + 4, Y + 4, Width - 8, 40);
            ToolbarButton(canvas, IconType.ArrowUp, 8);
            ToolbarButton(canvas, IconType.Start, 50);
            ToolbarButton(canvas, IconType.Refresh, 92);
            ToolbarButton(canvas, IconType.Search, 134);

            int pathX = X + 180;
            int pathW = Math.Max(120, Width - 188);
            canvas.DrawFilledRectangle(Color.FromArgb(25, 30, 36), pathX, Y + 9, pathW, 30);
            canvas.DrawRectangle(Color.FromArgb(82, 94, 108), pathX, Y + 9, pathW, 30);
            DrawPath(canvas, pathX + 7, Y + 10, pathW - 14);

            int footerY = Y + Height - FooterHeight - 4;
            int listX = X + 4;
            int listY = Y + ContentTop;
            int listW = Width - 8;
            int listH = Math.Max(40, footerY - listY - 4);
            canvas.DrawFilledRectangle(ContentBackground, listX, listY, listW, listH);
            canvas.DrawRectangle(Color.FromArgb(199, 205, 212), listX, listY, listW, listH);
            Grid(canvas, listX, listY, listW, listH);

            RenderFooter(canvas, footerY);

            if (app.ContextMenuVisible)
                Menu(canvas);
            if (app.DialogMode != 0)
                Dialog(canvas);
        }

        private void RenderFooter(Canvas canvas, int footerY)
        {
            int footerX = X + 4;
            int footerW = Width - 8;

            canvas.DrawFilledRectangle(Color.FromArgb(27, 32, 38), footerX, footerY, footerW, FooterHeight);
            canvas.DrawLine(Color.FromArgb(72, 84, 96), footerX, footerY, footerX + footerW, footerY);

            string hint = Width >= 620
                ? "ENTER OPEN   F5 REFRESH   BACKSPACE UP"
                : "ENTER OPEN   F5 REFRESH";

            int hintWidth = TinyTextWidth(hint);
            int hintX = Math.Max(X + Width / 2, X + Width - 10 - hintWidth);
            int hintMaxWidth = Math.Max(0, X + Width - 10 - hintX);
            int statusX = X + 10;
            int statusMaxWidth = Math.Max(18, hintX - statusX - 12);
            int textY = footerY + 8;

            DrawTinyTextClipped(canvas, app.Status ?? "", statusX, textY, statusMaxWidth, FooterText);
            DrawTinyTextClipped(canvas, hint, hintX, textY, hintMaxWidth, Color.FromArgb(155, 168, 181));
        }

        private void DrawPath(Canvas canvas, int x, int y, int width)
        {
            string path = app.CurrentPath;
            int maxChars = Math.Max(8, width / 16);
            int searchExtra = string.IsNullOrEmpty(app.SearchText) ? 0 : app.SearchText.Length + 3;
            int total = path.Length + searchExtra;
            int start = total > maxChars ? total - maxChars + 3 : 0;

            if (start > 0)
                canvas.DrawString("...", font, Color.FromArgb(226, 232, 238), x, y);

            int px = x + (start > 0 ? 3 * 16 : 0);
            if (start < path.Length)
            {
                int take = path.Length - start;
                if (take > maxChars)
                    take = maxChars;

                DrawStringRange(canvas, path, start, take, px, y, Color.FromArgb(226, 232, 238));
                px += take * 16;
            }

            if (!string.IsNullOrEmpty(app.SearchText) && px - x < width)
            {
                canvas.DrawString(" [", font, Color.FromArgb(155, 190, 220), px, y);
                px += 2 * 16;

                int remaining = Math.Max(0, width - (px - x));
                int takeSearch = Math.Min(app.SearchText.Length, remaining / 16);
                DrawStringRange(canvas, app.SearchText, 0, takeSearch, px, y, Color.FromArgb(155, 190, 220));
                px += takeSearch * 16;

                if (takeSearch < app.SearchText.Length && px - x + 3 * 16 <= width)
                    canvas.DrawString("...", font, Color.FromArgb(155, 190, 220), px, y);
            }
        }

        private void DrawStringRange(Canvas canvas, string text, int start, int count, int x, int y, Color color)
        {
            if (text == null || count <= 0 || start < 0 || start >= text.Length)
                return;

            int end = Math.Min(text.Length, start + count);
            for (int i = start; i < end; i++)
            {
                char ch = text[i];
                canvas.DrawString(ch < 256 ? CharCache[ch] : ch.ToString(), font, color, x + (i - start) * 16, y);
            }
        }

        private void Grid(Canvas canvas, int x, int y, int w, int h)
        {
            int iconSize = 40;
            int labelWidth = 84;
            int labelHeight = 18;
            int cols = Math.Max(1, (w + TileGap) / (TileWidth + TileGap));
            int rows = Math.Max(1, (h + TileGap) / (TileHeight + TileGap));
            int count = cols * rows;

            for (int i = 0; i < count; i++)
            {
                int index = app.ScrollIndex + i;
                if (index >= app.Entries.Count)
                    break;

                FileEntry entry = app.Entries[index];
                int tileX = x + i % cols * (TileWidth + TileGap);
                int tileY = y + i / cols * (TileHeight + TileGap);

                if (index == app.SelectedIndex)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(218, 234, 248), tileX, tileY, TileWidth, TileHeight);
                    canvas.DrawRectangle(Accent, tileX, tileY, TileWidth, TileHeight);
                    canvas.DrawFilledRectangle(Accent, tileX, tileY, 3, TileHeight);
                }

                int iconX = tileX + (TileWidth - iconSize) / 2;
                IconManager.DrawScaled(
                    canvas,
                    entry.IsDirectory ? IconType.Folder : IconType.File,
                    iconX,
                    tileY + 5,
                    iconSize,
                    iconSize);

                DrawWrappedName(
                    canvas,
                    entry.Name ?? "",
                    tileX + (TileWidth - labelWidth) / 2,
                    tileY + iconSize + 9,
                    labelWidth,
                    labelHeight,
                    Color.FromArgb(35, 40, 46));
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

            int pos = 0;
            int maxLines = Math.Max(1, height / lineHeight);

            for (int line = 0; line < maxLines && pos < name.Length; line++)
            {
                int take = Math.Min(maxChars, name.Length - pos);
                if (pos + take < name.Length)
                {
                    int breakAt = name.LastIndexOf(' ', pos + take - 1, take);
                    if (breakAt >= pos)
                        take = breakAt - pos;
                }

                if (take <= 0)
                    take = Math.Min(maxChars, name.Length - pos);

                bool truncated = pos + take < name.Length && line == maxLines - 1;
                int drawTake = truncated && take >= 3 ? take - 3 : take;
                int textWidth = drawTake * (glyphWidth + spacing) - (drawTake > 0 ? spacing : 0);

                if (truncated)
                    textWidth += 3 * (glyphWidth + spacing);

                int px = x + Math.Max(0, (width - textWidth) / 2);
                DrawTinyTextRange(canvas, name, pos, drawTake, px, y + line * lineHeight, color);

                if (truncated)
                    DrawTinyText(canvas, "...", px + drawTake * (glyphWidth + spacing), y + line * lineHeight, color);

                pos += take;
                while (pos < name.Length && name[pos] == ' ')
                    pos++;
            }
        }

        private int TinyTextWidth(string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            return text.Length * 6 - 1;
        }

        private void DrawTinyTextClipped(Canvas canvas, string text, int x, int y, int maxWidth, Color color)
        {
            if (string.IsNullOrEmpty(text) || maxWidth < 5)
                return;

            int maxChars = Math.Max(1, (maxWidth + 1) / 6);
            bool truncated = text.Length > maxChars;
            int drawChars = Math.Min(text.Length, maxChars);

            if (truncated && maxChars >= 4)
            {
                drawChars = maxChars - 3;
                DrawTinyTextRange(canvas, text, 0, drawChars, x, y, color);
                DrawTinyText(canvas, "...", x + drawChars * 6, y, color);
                return;
            }

            DrawTinyTextRange(canvas, text, 0, drawChars, x, y, color);
        }

        private void DrawTinyTextRange(Canvas canvas, string text, int start, int count, int x, int y, Color color)
        {
            if (text == null || count <= 0 || start < 0 || start >= text.Length)
                return;

            int end = Math.Min(text.Length, start + count);
            for (int i = start; i < end; i++)
                DrawTinyChar(canvas, text[i], x + (i - start) * 6, y, color);
        }

        private void DrawTinyText(Canvas canvas, string text, int x, int y, Color color)
        {
            if (string.IsNullOrEmpty(text))
                return;

            for (int i = 0; i < text.Length; i++)
                DrawTinyChar(canvas, text[i], x + i * 6, y, color);
        }

        private void DrawTinyChar(Canvas canvas, char ch, int x, int y, Color color)
        {
            const int glyphWidth = 5;
            for (int row = 0; row < 7; row++)
            {
                int bits = TinyGlyph(ch, row);
                for (int col = 0; col < glyphWidth; col++)
                {
                    if ((bits & (1 << (glyphWidth - 1 - col))) != 0)
                        canvas.DrawFilledRectangle(color, x + col, y + row, 1, 1);
                }
            }
        }

        private int TinyGlyph(char ch, int row)
        {
            string pattern;
            switch (char.ToUpperInvariant(ch))
            {
                case 'A': pattern = "011101000110001111111000110001"; break;
                case 'B': pattern = "111101000110001111101000111101"; break;
                case 'C': pattern = "011101000010000100001000001110"; break;
                case 'D': pattern = "111101000110001100011000111101"; break;
                case 'E': pattern = "111111000010000111101000011111"; break;
                case 'F': pattern = "111111000010000111101000010000"; break;
                case 'G': pattern = "011101000010000101111000101111"; break;
                case 'H': pattern = "100011000110001111111000110001"; break;
                case 'I': pattern = "111110010000100001000010011111"; break;
                case 'J': pattern = "001110001000100001001001001110"; break;
                case 'K': pattern = "100011001010100110001010010001"; break;
                case 'L': pattern = "100001000010000100001000011111"; break;
                case 'M': pattern = "100011101110101101011000110001"; break;
                case 'N': pattern = "100011100110101100111000110001"; break;
                case 'O': pattern = "011101000110001100011000101110"; break;
                case 'P': pattern = "111101000110001111101000010000"; break;
                case 'Q': pattern = "011101000110001100011010010101"; break;
                case 'R': pattern = "111101000110001111101010010001"; break;
                case 'S': pattern = "011111000010000011000000111110"; break;
                case 'T': pattern = "111110010000100001000010000100"; break;
                case 'U': pattern = "100011000110001100011000101110"; break;
                case 'V': pattern = "100011000110001100011010000100"; break;
                case 'W': pattern = "100011000110001101011010101010"; break;
                case 'X': pattern = "100011000101010001000101010001"; break;
                case 'Y': pattern = "100011000101010001000010000100"; break;
                case 'Z': pattern = "111110000100010001000100011111"; break;
                case '0': pattern = "011101000110011101011000101110"; break;
                case '1': pattern = "001000110000100001000010011111"; break;
                case '2': pattern = "011101000100001000100100011111"; break;
                case '3': pattern = "111100000100001001110000111110"; break;
                case '4': pattern = "000100011001010111110001000010"; break;
                case '5': pattern = "111111000011110000010000111110"; break;
                case '6': pattern = "011101000010000111101000101110"; break;
                case '7': pattern = "111110000100010001000010000100"; break;
                case '8': pattern = "011101000110001011101000110111"; break;
                case '9': pattern = "011101000110001011110000101110"; break;
                case '.': pattern = "000000000000000000000000000001"; break;
                case '-': pattern = "000000000000000011100000000000"; break;
                case '_': pattern = "000000000000000000000000011111"; break;
                case '/': pattern = "000010001000100010001000000000"; break;
                case ':': pattern = "000000010000000001000000000000"; break;
                case '|': pattern = "001000010000100001000010000100"; break;
                case '=': pattern = "000001111100000111110000000000"; break;
                case ' ': pattern = "000000000000000000000000000000"; break;
                default: pattern = "011101000100010001000000010000"; break;
            }

            int offset = row * 5;
            if (offset + 5 > pattern.Length)
                return 0;

            int bits = 0;
            for (int i = 0; i < 5; i++)
            {
                if (pattern[offset + i] == '1')
                    bits |= 1 << (4 - i);
            }

            return bits;
        }

        private void ToolbarButton(Canvas canvas, IconType type, int offset)
        {
            int buttonX = X + offset;
            canvas.DrawFilledRectangle(Color.FromArgb(235, 239, 243), buttonX, Y + 7, 36, 30);
            canvas.DrawRectangle(Color.FromArgb(112, 124, 136), buttonX, Y + 7, 36, 30);
            IconManager.Draw(canvas, type, buttonX + 9, Y + 11, Color.Black);
        }

        private void Menu(Canvas canvas)
        {
            int x = X + app.ContextMenuX;
            int y = Y + app.ContextMenuY;

            canvas.DrawFilledRectangle(Color.FromArgb(34, 40, 47), x + 3, y + 3, 220, 116);
            canvas.DrawFilledRectangle(Color.FromArgb(43, 50, 58), x, y, 220, 116);
            canvas.DrawRectangle(Color.FromArgb(85, 102, 118), x, y, 220, 116);

            DrawMenuItem(canvas, "New file", x, y + 3);
            DrawMenuItem(canvas, "New folder", x, y + 32);
            DrawMenuItem(canvas, "Refresh", x, y + 61);
            DrawMenuItem(canvas, "Go up", x, y + 90);
        }

        private void DrawMenuItem(Canvas canvas, string text, int x, int y)
        {
            DrawTinyText(canvas, text, x + 10, y + 10, Color.FromArgb(226, 232, 238));
        }

        private void Dialog(Canvas canvas)
        {
            int width = 430;
            int height = 126;
            int x = X + (Width - width) / 2;
            int y = Y + (Height - height) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), x + 4, y + 4, width, height);
            canvas.DrawFilledRectangle(Color.FromArgb(38, 44, 51), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);

            string title = app.DialogMode == 1 ? "New file" : "New folder";
            canvas.DrawString(title, font, Color.FromArgb(235, 239, 243), x + 12, y + 10);

            canvas.DrawFilledRectangle(Color.FromArgb(247, 248, 250), x + 12, y + 42, width - 24, 30);
            canvas.DrawRectangle(Color.FromArgb(112, 124, 136), x + 12, y + 42, width - 24, 30);
            canvas.DrawString(app.DialogName + "_", font, Color.FromArgb(35, 40, 46), x + 18, y + 43);

            DrawTinyText(canvas, "ENTER CREATE   ESC CANCEL", x + 12, y + 92, Color.FromArgb(170, 181, 192));
        }
    }
}
