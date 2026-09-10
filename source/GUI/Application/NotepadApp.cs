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
    public sealed class NotepadApp : Application
    {
        private readonly List<string> lines = new List<string>();
        private readonly List<OpenEntry> openEntries = new List<OpenEntry>();
        private readonly Action closeCallback;
        private readonly NotepadView view;

        private string filePath;
        private string status = "Gotowy";
        private string positionText = "LN 1  COL 1";
        private int cursorX;
        private int cursorY;
        private int scrollX;
        private int scrollY;
        private int documentVersion;
        private bool dirty;

        // 0 = none, 1 = graphical open picker, 2 = save-as path, 3 = discard confirmation.
        private int dialogMode;
        private string dialogText = "";
        private int pendingAction;

        private string openDirectory = "/root";
        private int openSelectedIndex = -1;
        private int openScrollIndex;

        public NotepadApp(int x, int y, string path, Action onClose) : base("Notatnik")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 980, 650, "Notatnik");
            Window.CloseAction = RequestClose;

            lines.Add("");
            view = new NotepadView(10, 40, 960, 595, this);
            Window.AddChild(view);

            if (!string.IsNullOrEmpty(path))
                LoadDocument(path);
            else
                UpdateWindowTitle();

            UpdateLayout();
            UpdatePositionText();
        }

        public override void Update()
        {
            UpdateLayout();
        }

        private void UpdateLayout()
        {
            view.X = Window.X + 10;
            view.Y = Window.Y + 40;
            view.Width = Math.Max(300, Window.Width - 20);
            view.Height = Math.Max(180, Window.Height - 50);
            EnsureCursorVisible();
            EnsureOpenSelectionVisible();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            if (!Window.Visible || !IsRunning || !left || oldLeft)
                return;

            int x = mouseX - view.X;
            int y = mouseY - view.Y;
            if (x < 0 || y < 0 || x >= view.Width || y >= view.Height)
                return;

            if (dialogMode == 1)
            {
                HandleOpenDialogMouse(x, y);
                return;
            }

            if (dialogMode != 0)
                return;

            int action = view.ToolbarActionAt(x, y);
            if (action != 0)
            {
                if (action == 1)
                    RequestNew();
                else if (action == 2)
                    RequestOpen();
                else if (action == 3)
                    Save();
                else if (action == 4)
                    BeginSaveAs();
                return;
            }

            int line;
            int column;
            if (view.TrySetCursorFromPoint(x, y, out line, out column))
            {
                cursorY = Math.Max(0, Math.Min(lines.Count - 1, line));
                cursorX = Math.Max(0, Math.Min(lines[cursorY].Length, column));
                EnsureCursorVisible();
                UpdatePositionText();
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (dialogMode != 0)
            {
                HandleDialogKeyboard(key);
                return;
            }

            bool control = (key.Modifiers & ConsoleModifiers.Control) != 0;
            bool shift = (key.Modifiers & ConsoleModifiers.Shift) != 0;

            if (control)
            {
                if (key.Key == ConsoleKeyEx.S)
                {
                    if (shift)
                        BeginSaveAs();
                    else
                        Save();
                    return;
                }

                if (key.Key == ConsoleKeyEx.O)
                {
                    RequestOpen();
                    return;
                }

                if (key.Key == ConsoleKeyEx.N)
                {
                    RequestNew();
                    return;
                }
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                RequestClose();
                return;
            }

            if (key.Key == ConsoleKeyEx.F2)
            {
                Save();
                return;
            }

            bool changed = false;

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (cursorY > 0)
                {
                    cursorY--;
                    cursorX = Math.Min(cursorX, lines[cursorY].Length);
                }
            }
            else if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (cursorY < lines.Count - 1)
                {
                    cursorY++;
                    cursorX = Math.Min(cursorX, lines[cursorY].Length);
                }
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                if (cursorX > 0)
                {
                    cursorX--;
                }
                else if (cursorY > 0)
                {
                    cursorY--;
                    cursorX = lines[cursorY].Length;
                }
            }
            else if (key.Key == ConsoleKeyEx.RightArrow)
            {
                if (cursorX < lines[cursorY].Length)
                {
                    cursorX++;
                }
                else if (cursorY < lines.Count - 1)
                {
                    cursorY++;
                    cursorX = 0;
                }
            }
            else if (key.Key == ConsoleKeyEx.Backspace)
            {
                changed = Backspace();
            }
            else if (key.Key == ConsoleKeyEx.Enter)
            {
                InsertNewLine();
                changed = true;
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                InsertCharacter(key.KeyChar);
                changed = true;
            }

            if (changed)
                MarkDocumentChanged();

            EnsureCursorVisible();
            UpdatePositionText();
        }

        private void InsertCharacter(char value)
        {
            string line = lines[cursorY] ?? "";
            lines[cursorY] = line.Insert(cursorX, value.ToString());
            cursorX++;
        }

        private void InsertNewLine()
        {
            string line = lines[cursorY] ?? "";
            string remainder = line.Substring(cursorX);
            lines[cursorY] = line.Substring(0, cursorX);
            lines.Insert(cursorY + 1, remainder);
            cursorY++;
            cursorX = 0;
        }

        private bool Backspace()
        {
            if (cursorX > 0)
            {
                string line = lines[cursorY] ?? "";
                lines[cursorY] = line.Remove(cursorX - 1, 1);
                cursorX--;
                return true;
            }

            if (cursorY <= 0)
                return false;

            int previousLength = lines[cursorY - 1].Length;
            lines[cursorY - 1] += lines[cursorY];
            lines.RemoveAt(cursorY);
            cursorY--;
            cursorX = previousLength;
            return true;
        }

        private void MarkDocumentChanged()
        {
            documentVersion++;
            status = "Niezapisane zmiany";
            if (!dirty)
            {
                dirty = true;
                UpdateWindowTitle();
            }
        }

        private void RequestNew()
        {
            if (dirty)
            {
                pendingAction = 2;
                dialogMode = 3;
                return;
            }

            NewDocument();
        }

        private void RequestOpen()
        {
            if (dirty)
            {
                pendingAction = 3;
                dialogMode = 3;
                return;
            }

            BeginOpen();
        }

        private void RequestClose()
        {
            if (!IsRunning)
                return;

            if (dirty)
            {
                pendingAction = 1;
                dialogMode = 3;
                return;
            }

            CloseNow();
        }

        private void NewDocument()
        {
            lines.Clear();
            lines.Add("");
            filePath = null;
            cursorX = 0;
            cursorY = 0;
            scrollX = 0;
            scrollY = 0;
            dirty = false;
            documentVersion++;
            status = "Nowy dokument";
            UpdateWindowTitle();
            UpdatePositionText();
        }

        private void BeginOpen()
        {
            string start = "/root";
            if (!string.IsNullOrEmpty(filePath))
            {
                try
                {
                    string parent = Path.GetDirectoryName(filePath);
                    if (!string.IsNullOrEmpty(parent) && Directory.Exists(parent))
                        start = parent.Replace('\\', '/');
                }
                catch
                {
                }
            }

            openDirectory = start;
            openSelectedIndex = -1;
            openScrollIndex = 0;
            dialogMode = 1;
            RefreshOpenEntries();
            status = "Wybierz plik do otwarcia";
        }

        private void BeginSaveAs()
        {
            dialogMode = 2;
            dialogText = string.IsNullOrEmpty(filePath) ? "/root/untitled.txt" : filePath;
            status = "Zapisz jako";
        }

        private void HandleDialogKeyboard(KeyEvent key)
        {
            if (dialogMode == 3)
            {
                if (key.Key == ConsoleKeyEx.Escape)
                {
                    dialogMode = 0;
                    pendingAction = 0;
                    status = "Anulowano";
                    return;
                }

                if (key.Key == ConsoleKeyEx.Enter)
                {
                    int action = pendingAction;
                    dialogMode = 0;
                    pendingAction = 0;
                    dirty = false;

                    if (action == 1)
                        CloseNow();
                    else if (action == 2)
                        NewDocument();
                    else if (action == 3)
                        BeginOpen();
                    return;
                }

                return;
            }

            if (dialogMode == 1)
            {
                HandleOpenDialogKeyboard(key);
                return;
            }

            if (key.Key == ConsoleKeyEx.Escape)
            {
                dialogMode = 0;
                dialogText = "";
                status = "Anulowano";
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (dialogText.Length > 0)
                    dialogText = dialogText.Substring(0, dialogText.Length - 1);
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter)
            {
                string path = NormalizePath(dialogText);
                if (SaveTo(path))
                {
                    dialogMode = 0;
                    dialogText = "";
                }
                return;
            }

            // No artificial character limit for save path input.
            if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
                dialogText += key.KeyChar;
        }

        private void HandleOpenDialogKeyboard(KeyEvent key)
        {
            if (key.Key == ConsoleKeyEx.Escape)
            {
                dialogMode = 0;
                openSelectedIndex = -1;
                status = "Anulowano";
                return;
            }

            if (key.Key == ConsoleKeyEx.Backspace || key.Key == ConsoleKeyEx.LeftArrow)
            {
                OpenParentDirectory();
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (openEntries.Count > 0)
                {
                    if (openSelectedIndex < 0)
                        openSelectedIndex = 0;
                    else if (openSelectedIndex > 0)
                        openSelectedIndex--;
                    EnsureOpenSelectionVisible();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (openEntries.Count > 0)
                {
                    if (openSelectedIndex < 0)
                        openSelectedIndex = 0;
                    else if (openSelectedIndex < openEntries.Count - 1)
                        openSelectedIndex++;
                    EnsureOpenSelectionVisible();
                }
                return;
            }

            if (key.Key == ConsoleKeyEx.Enter || key.Key == ConsoleKeyEx.RightArrow)
            {
                OpenSelectedPickerEntry();
            }
        }

        private void HandleOpenDialogMouse(int x, int y)
        {
            int dialogX;
            int dialogY;
            int dialogWidth;
            int dialogHeight;
            view.GetOpenDialogBounds(out dialogX, out dialogY, out dialogWidth, out dialogHeight);

            if (x < dialogX || x >= dialogX + dialogWidth || y < dialogY || y >= dialogY + dialogHeight)
                return;

            if (x >= dialogX + 14 && x < dialogX + 74 && y >= dialogY + 48 && y < dialogY + 78)
            {
                OpenParentDirectory();
                return;
            }

            int listX = dialogX + 14;
            int listY = dialogY + 92;
            int listW = dialogWidth - 28;
            int listH = dialogHeight - 150;
            if (x >= listX && x < listX + listW && y >= listY && y < listY + listH)
            {
                int row = (y - listY) / NotepadView.OpenRowHeight;
                int index = openScrollIndex + row;
                if (index >= 0 && index < openEntries.Count)
                {
                    openSelectedIndex = index;
                    EnsureOpenSelectionVisible();
                }
                return;
            }

            int buttonY = dialogY + dialogHeight - 44;
            if (y >= buttonY && y < buttonY + 30)
            {
                if (x >= dialogX + dialogWidth - 174 && x < dialogX + dialogWidth - 94)
                {
                    OpenSelectedPickerEntry();
                    return;
                }

                if (x >= dialogX + dialogWidth - 88 && x < dialogX + dialogWidth - 14)
                {
                    dialogMode = 0;
                    openSelectedIndex = -1;
                    status = "Anulowano";
                }
            }
        }

        private void RefreshOpenEntries()
        {
            openEntries.Clear();
            openSelectedIndex = -1;
            openScrollIndex = 0;

            try
            {
                if (string.IsNullOrEmpty(openDirectory) || !Directory.Exists(openDirectory))
                    openDirectory = "/root";
                if (!Directory.Exists(openDirectory))
                    openDirectory = "/";

                string[] directories = Directory.GetDirectories(openDirectory);
                string[] files = Directory.GetFiles(openDirectory);

                for (int i = 0; i < directories.Length; i++)
                    openEntries.Add(new OpenEntry(GetDisplayName(directories[i]), directories[i].Replace('\\', '/'), true));
                for (int i = 0; i < files.Length; i++)
                    openEntries.Add(new OpenEntry(GetDisplayName(files[i]), files[i].Replace('\\', '/'), false));

                SortOpenEntries();
                status = openEntries.Count + " elementow w " + openDirectory;
            }
            catch (Exception ex)
            {
                status = "Blad katalogu: " + ex.Message;
            }
        }

        private static string GetDisplayName(string path)
        {
            string value = (path ?? "").TrimEnd('/', '\\');
            string name = Path.GetFileName(value);
            return string.IsNullOrEmpty(name) ? value : name;
        }

        private void SortOpenEntries()
        {
            for (int i = 1; i < openEntries.Count; i++)
            {
                OpenEntry value = openEntries[i];
                int j = i - 1;
                while (j >= 0 && CompareOpenEntries(openEntries[j], value) > 0)
                {
                    openEntries[j + 1] = openEntries[j];
                    j--;
                }
                openEntries[j + 1] = value;
            }
        }

        private static int CompareOpenEntries(OpenEntry a, OpenEntry b)
        {
            if (a.IsDirectory != b.IsDirectory)
                return a.IsDirectory ? -1 : 1;
            return string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
        }

        private void OpenParentDirectory()
        {
            if (openDirectory == "/")
                return;

            try
            {
                DirectoryInfo parent = Directory.GetParent(openDirectory);
                openDirectory = parent == null ? "/" : parent.FullName.Replace('\\', '/');
                RefreshOpenEntries();
            }
            catch (Exception ex)
            {
                status = "Blad katalogu: " + ex.Message;
            }
        }

        private void OpenSelectedPickerEntry()
        {
            if (openSelectedIndex < 0 || openSelectedIndex >= openEntries.Count)
            {
                status = "Wybierz plik lub folder";
                return;
            }

            OpenEntry entry = openEntries[openSelectedIndex];
            if (entry.IsDirectory)
            {
                openDirectory = entry.FullPath;
                RefreshOpenEntries();
                return;
            }

            if (LoadDocument(entry.FullPath))
            {
                dialogMode = 0;
                openSelectedIndex = -1;
                openScrollIndex = 0;
            }
        }

        private void EnsureOpenSelectionVisible()
        {
            if (dialogMode != 1 || openSelectedIndex < 0)
                return;

            int visible = Math.Max(1, view.OpenVisibleRows);
            if (openSelectedIndex < openScrollIndex)
                openScrollIndex = openSelectedIndex;
            else if (openSelectedIndex >= openScrollIndex + visible)
                openScrollIndex = openSelectedIndex - visible + 1;

            int maxScroll = Math.Max(0, openEntries.Count - visible);
            if (openScrollIndex > maxScroll)
                openScrollIndex = maxScroll;
            if (openScrollIndex < 0)
                openScrollIndex = 0;
        }

        private string NormalizePath(string path)
        {
            string value = (path ?? "").Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(value))
                return value;
            if (value[0] != '/')
                value = "/root/" + value;
            return value;
        }

        private bool LoadDocument(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                status = "Sciezka nie moze byc pusta";
                return false;
            }

            try
            {
                if (!File.Exists(path))
                {
                    status = "Nie znaleziono pliku: " + path;
                    return false;
                }

                string content = File.ReadAllText(path).Replace("\r", "");
                string[] loaded = content.Split('\n');

                lines.Clear();
                for (int i = 0; i < loaded.Length; i++)
                    lines.Add(loaded[i] ?? "");
                if (lines.Count == 0)
                    lines.Add("");

                filePath = path;
                cursorX = 0;
                cursorY = 0;
                scrollX = 0;
                scrollY = 0;
                dirty = false;
                documentVersion++;
                status = "Otwarto: " + path;
                UpdateWindowTitle();
                UpdatePositionText();
                return true;
            }
            catch (Exception ex)
            {
                status = "Blad odczytu: " + ex.Message;
                return false;
            }
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(filePath))
            {
                BeginSaveAs();
                return;
            }

            SaveTo(filePath);
        }

        private bool SaveTo(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                status = "Sciezka nie moze byc pusta";
                return false;
            }

            try
            {
                // Document content has no artificial character/size cap.
                File.WriteAllText(path, string.Join("\n", lines));
                filePath = path;
                dirty = false;
                status = "Zapisano: " + path;
                UpdateWindowTitle();
                return true;
            }
            catch (Exception ex)
            {
                status = "Blad zapisu: " + ex.Message;
                return false;
            }
        }

        private void UpdateWindowTitle()
        {
            string name = string.IsNullOrEmpty(filePath) ? "Bez nazwy" : Path.GetFileName(filePath);
            if (string.IsNullOrEmpty(name))
                name = filePath ?? "Bez nazwy";
            Window.Title = "Notatnik - " + name + (dirty ? " *" : "");
        }

        private void UpdatePositionText()
        {
            positionText = "LN " + (cursorY + 1) + "  COL " + (cursorX + 1);
        }

        private void EnsureCursorVisible()
        {
            int rows = Math.Max(1, view.VisibleRows);
            int columns = Math.Max(1, view.VisibleColumns);

            if (cursorY < scrollY)
                scrollY = cursorY;
            else if (cursorY >= scrollY + rows)
                scrollY = cursorY - rows + 1;

            if (cursorX < scrollX)
                scrollX = cursorX;
            else if (cursorX >= scrollX + columns)
                scrollX = cursorX - columns + 1;

            if (scrollX < 0) scrollX = 0;
            if (scrollY < 0) scrollY = 0;
        }

        private void CloseNow()
        {
            base.Close();
            if (closeCallback != null)
                closeCallback();
        }

        public override void Close()
        {
            RequestClose();
        }

        public List<string> Lines { get { return lines; } }
        public string FilePath { get { return filePath; } }
        public string Status { get { return status; } }
        public string PositionText { get { return positionText; } }
        public int CursorX { get { return cursorX; } }
        public int CursorY { get { return cursorY; } }
        public int ScrollX { get { return scrollX; } }
        public int ScrollY { get { return scrollY; } }
        public int DocumentVersion { get { return documentVersion; } }
        public bool Dirty { get { return dirty; } }
        public int DialogMode { get { return dialogMode; } }
        public string DialogText { get { return dialogText; } }
        public string OpenDirectory { get { return openDirectory; } }
        public int OpenSelectedIndex { get { return openSelectedIndex; } }
        public int OpenScrollIndex { get { return openScrollIndex; } }
        public List<OpenEntry> OpenEntries { get { return openEntries; } }
    }

    public sealed class OpenEntry
    {
        public readonly string Name;
        public readonly string FullPath;
        public readonly bool IsDirectory;

        public OpenEntry(string name, string fullPath, bool isDirectory)
        {
            Name = name;
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }
    }

    internal sealed class NotepadView : Widget
    {
        private readonly NotepadApp app;
        private readonly Font font = PCScreenFont.DefaultFont;

        private const int ToolbarHeight = 42;
        private const int EditorTop = 48;
        private const int StatusHeight = 24;
        private const int GutterWidth = 50;
        private const int CharWidth = 16;
        private const int LineHeight = 32;
        internal const int OpenRowHeight = 30;

        private static readonly Color Chrome = Color.FromArgb(31, 37, 44);
        private static readonly Color ChromeRaised = Color.FromArgb(40, 47, 55);
        private static readonly Color Border = Color.FromArgb(69, 82, 95);
        private static readonly Color Accent = Color.FromArgb(65, 140, 200);
        private static readonly Color EditorBackground = Color.FromArgb(24, 29, 35);
        private static readonly Color GutterBackground = Color.FromArgb(28, 34, 40);
        private static readonly Color MainText = Color.FromArgb(229, 234, 239);
        private static readonly Color SecondaryText = Color.FromArgb(145, 159, 173);

        private int cacheVersion = -1;
        private int cacheScrollX = -1;
        private int cacheScrollY = -1;
        private int cacheColumns = -1;
        private int cacheRows = -1;
        private string[] visibleText = new string[0];

        public NotepadView(int x, int y, int width, int height, NotepadApp owner)
            : base(x, y, width, height)
        {
            app = owner;
        }

        public int VisibleRows
        {
            get
            {
                int editorHeight = Math.Max(LineHeight, Height - EditorTop - StatusHeight - 8);
                return Math.Max(1, editorHeight / LineHeight);
            }
        }

        public int VisibleColumns
        {
            get
            {
                int textWidth = Math.Max(CharWidth, Width - GutterWidth - 22);
                return Math.Max(1, textWidth / CharWidth);
            }
        }

        public int OpenVisibleRows
        {
            get
            {
                int x;
                int y;
                int w;
                int h;
                GetOpenDialogBounds(out x, out y, out w, out h);
                return Math.Max(1, (h - 150) / OpenRowHeight);
            }
        }

        public int ToolbarActionAt(int x, int y)
        {
            if (y < 7 || y >= 36)
                return 0;
            if (x >= 8 && x < 66) return 1;
            if (x >= 70 && x < 136) return 2;
            if (x >= 140 && x < 206) return 3;
            if (x >= 210 && x < 294) return 4;
            return 0;
        }

        public bool TrySetCursorFromPoint(int x, int y, out int line, out int column)
        {
            line = 0;
            column = 0;

            int editorBottom = Height - StatusHeight - 4;
            int textLeft = 4 + GutterWidth + 8;
            if (y < EditorTop + 4 || y >= editorBottom || x < textLeft)
                return false;

            line = app.ScrollY + Math.Max(0, (y - EditorTop - 6) / LineHeight);
            if (line >= app.Lines.Count)
                line = app.Lines.Count - 1;
            if (line < 0)
                line = 0;

            column = app.ScrollX + Math.Max(0, (x - textLeft) / CharWidth);
            if (column > app.Lines[line].Length)
                column = app.Lines[line].Length;
            return true;
        }

        public void GetOpenDialogBounds(out int x, out int y, out int width, out int height)
        {
            width = Math.Min(640, Math.Max(360, Width - 80));
            height = Math.Min(460, Math.Max(310, Height - 70));
            x = (Width - width) / 2;
            y = (Height - height) / 2;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible)
                return;

            canvas.DrawFilledRectangle(Chrome, X, Y, Width, Height);
            canvas.DrawRectangle(Border, X, Y, Width, Height);

            RenderToolbar(canvas);
            RenderEditor(canvas);
            RenderStatus(canvas);

            if (app.DialogMode != 0)
                RenderDialog(canvas);
        }

        private void RenderToolbar(Canvas canvas)
        {
            canvas.DrawFilledRectangle(ChromeRaised, X + 4, Y + 4, Width - 8, ToolbarHeight - 2);
            DrawToolbarButton(canvas, 8, 58, IconType.File, "NEW");
            DrawToolbarButton(canvas, 70, 66, IconType.Folder, "OPEN");
            DrawToolbarButton(canvas, 140, 66, IconType.File, "SAVE");
            DrawToolbarButton(canvas, 210, 84, IconType.File, "SAVE AS");

            if (Width >= 520)
            {
                string document = string.IsNullOrEmpty(app.FilePath) ? "UNNAMED" : app.FilePath;
                SmallTextRenderer.DrawClipped(canvas, document, X + 308, Y + 18,
                    Width - 320, SecondaryText);
            }
        }

        private void DrawToolbarButton(Canvas canvas, int offset, int width, IconType icon, string label)
        {
            int x = X + offset;
            canvas.DrawFilledRectangle(Color.FromArgb(49, 57, 66), x, Y + 7, width, 29);
            canvas.DrawRectangle(Color.FromArgb(83, 96, 110), x, Y + 7, width, 29);
            IconManager.DrawScaled(canvas, icon, x + 6, Y + 13, 16, 16);
            SmallTextRenderer.DrawClipped(canvas, label, x + 27, Y + 18,
                Math.Max(10, width - 32), Color.FromArgb(226, 232, 238));
        }

        private void RenderEditor(Canvas canvas)
        {
            int editorX = X + 4;
            int editorY = Y + EditorTop;
            int editorW = Width - 8;
            int editorH = Math.Max(LineHeight + 8, Height - EditorTop - StatusHeight - 4);
            int textX = editorX + GutterWidth + 8;
            int textY = editorY + 6;

            canvas.DrawFilledRectangle(EditorBackground, editorX, editorY, editorW, editorH);
            canvas.DrawRectangle(Color.FromArgb(54, 64, 74), editorX, editorY, editorW, editorH);
            canvas.DrawFilledRectangle(GutterBackground, editorX + 1, editorY + 1, GutterWidth - 1, editorH - 2);
            canvas.DrawLine(Color.FromArgb(58, 69, 80), editorX + GutterWidth, editorY + 1,
                editorX + GutterWidth, editorY + editorH - 2);

            EnsureVisibleTextCache();
            int rows = Math.Min(VisibleRows, visibleText.Length);

            for (int i = 0; i < rows; i++)
            {
                int lineIndex = app.ScrollY + i;
                if (lineIndex >= app.Lines.Count)
                    break;

                int rowY = textY + i * LineHeight;
                if (lineIndex == app.CursorY)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(29, 39, 49), editorX + GutterWidth + 1,
                        rowY - 3, editorW - GutterWidth - 2, LineHeight);
                    canvas.DrawFilledRectangle(Accent, editorX + GutterWidth + 1, rowY - 3, 2, LineHeight);
                }

                string number = (lineIndex + 1).ToString();
                int numberWidth = SmallTextRenderer.Width(number);
                SmallTextRenderer.Draw(canvas, number, editorX + GutterWidth - 8 - numberWidth, rowY + 10,
                    lineIndex == app.CursorY ? Color.FromArgb(137, 190, 229) : Color.FromArgb(103, 117, 130));

                string text = visibleText[i];
                if (!string.IsNullOrEmpty(text))
                    canvas.DrawString(text, font, MainText, textX, rowY);
            }

            int cursorRow = app.CursorY - app.ScrollY;
            int cursorColumn = app.CursorX - app.ScrollX;
            if (cursorRow >= 0 && cursorRow < VisibleRows && cursorColumn >= 0 && cursorColumn <= VisibleColumns)
            {
                canvas.DrawFilledRectangle(Color.FromArgb(105, 185, 239),
                    textX + cursorColumn * CharWidth, textY + cursorRow * LineHeight, 2, 29);
            }

            if (app.ScrollX > 0)
                SmallTextRenderer.Draw(canvas, "<", editorX + GutterWidth + 3, editorY + 7, Color.FromArgb(116, 166, 204));
        }

        private void EnsureVisibleTextCache()
        {
            int rows = VisibleRows;
            int columns = VisibleColumns;
            if (cacheVersion == app.DocumentVersion && cacheScrollX == app.ScrollX &&
                cacheScrollY == app.ScrollY && cacheColumns == columns && cacheRows == rows)
                return;

            visibleText = new string[rows];
            for (int i = 0; i < rows; i++)
            {
                int lineIndex = app.ScrollY + i;
                if (lineIndex < 0 || lineIndex >= app.Lines.Count)
                {
                    visibleText[i] = "";
                    continue;
                }

                string line = app.Lines[lineIndex] ?? "";
                if (app.ScrollX >= line.Length)
                {
                    visibleText[i] = "";
                    continue;
                }

                int count = Math.Min(columns, line.Length - app.ScrollX);
                if (app.ScrollX == 0 && count == line.Length)
                    visibleText[i] = line;
                else
                    visibleText[i] = line.Substring(app.ScrollX, count);
            }

            cacheVersion = app.DocumentVersion;
            cacheScrollX = app.ScrollX;
            cacheScrollY = app.ScrollY;
            cacheColumns = columns;
            cacheRows = rows;
        }

        private void RenderStatus(Canvas canvas)
        {
            int statusY = Y + Height - StatusHeight;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), X + 4, statusY, Width - 8, StatusHeight - 4);
            canvas.DrawLine(Color.FromArgb(64, 76, 88), X + 4, statusY, X + Width - 4, statusY);

            int rightWidth = SmallTextRenderer.Width(app.PositionText);
            int rightX = X + Width - 12 - rightWidth;
            SmallTextRenderer.DrawClipped(canvas, app.Status ?? "", X + 12, statusY + 8,
                Math.Max(20, rightX - X - 28), Color.FromArgb(184, 195, 205));
            SmallTextRenderer.Draw(canvas, app.PositionText, rightX, statusY + 8, Color.FromArgb(137, 174, 202));
        }

        private void RenderDialog(Canvas canvas)
        {
            if (app.DialogMode == 1)
            {
                RenderOpenDialog(canvas);
                return;
            }

            int width = Math.Min(520, Math.Max(280, Width - 70));
            int height = app.DialogMode == 3 ? 118 : 142;
            int x = X + (Width - width) / 2;
            int y = Y + (Height - height) / 2;

            canvas.DrawFilledRectangle(Color.FromArgb(12, 16, 20), x + 4, y + 4, width, height);
            canvas.DrawFilledRectangle(Color.FromArgb(39, 46, 54), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);
            canvas.DrawFilledRectangle(Accent, x, y, 3, height);

            if (app.DialogMode == 3)
            {
                canvas.DrawString("Niezapisane zmiany", font, Color.WhiteSmoke, x + 14, y + 12);
                SmallTextRenderer.DrawClipped(canvas, "ENTER DISCARD   ESC CANCEL", x + 14, y + 65,
                    width - 28, Color.FromArgb(180, 194, 206));
                return;
            }

            canvas.DrawString("Zapisz jako", font, Color.WhiteSmoke, x + 14, y + 10);

            int fieldX = x + 14;
            int fieldY = y + 50;
            int fieldW = width - 28;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), fieldX, fieldY, fieldW, 34);
            canvas.DrawRectangle(Color.FromArgb(82, 96, 110), fieldX, fieldY, fieldW, 34);

            int maxChars = Math.Max(1, (fieldW - 20) / CharWidth);
            string value = app.DialogText ?? "";
            int start = Math.Max(0, value.Length - maxChars);
            string visible = start == 0 ? value : value.Substring(start);
            canvas.DrawString(visible, font, Color.FromArgb(230, 235, 240), fieldX + 8, fieldY + 2);

            int cursorColumn = Math.Min(maxChars, visible.Length);
            canvas.DrawFilledRectangle(Color.FromArgb(105, 185, 239),
                fieldX + 8 + cursorColumn * CharWidth, fieldY + 3, 2, 28);

            SmallTextRenderer.DrawClipped(canvas, "ENTER CONFIRM   ESC CANCEL", x + 14, y + 108,
                width - 28, Color.FromArgb(170, 184, 197));
        }

        private void RenderOpenDialog(Canvas canvas)
        {
            int localX;
            int localY;
            int width;
            int height;
            GetOpenDialogBounds(out localX, out localY, out width, out height);
            int x = X + localX;
            int y = Y + localY;

            canvas.DrawFilledRectangle(Color.FromArgb(11, 15, 19), x + 5, y + 5, width, height);
            canvas.DrawFilledRectangle(Color.FromArgb(35, 42, 49), x, y, width, height);
            canvas.DrawRectangle(Accent, x, y, width, height);
            canvas.DrawFilledRectangle(Accent, x, y, 3, height);

            canvas.DrawString("Otworz plik", font, Color.WhiteSmoke, x + 14, y + 10);

            int upX = x + 14;
            int upY = y + 48;
            canvas.DrawFilledRectangle(Color.FromArgb(49, 58, 67), upX, upY, 60, 30);
            canvas.DrawRectangle(Color.FromArgb(82, 96, 110), upX, upY, 60, 30);
            IconManager.DrawScaled(canvas, IconType.ArrowUp, upX + 7, upY + 7, 16, 16);
            SmallTextRenderer.Draw(canvas, "UP", upX + 30, upY + 11, MainText);

            int pathX = x + 80;
            int pathW = width - 94;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), pathX, upY, pathW, 30);
            canvas.DrawRectangle(Color.FromArgb(70, 83, 96), pathX, upY, pathW, 30);
            SmallTextRenderer.DrawClipped(canvas, app.OpenDirectory, pathX + 9, upY + 11,
                pathW - 18, Color.FromArgb(185, 201, 214));

            int listX = x + 14;
            int listY = y + 92;
            int listW = width - 28;
            int listH = height - 150;
            canvas.DrawFilledRectangle(Color.FromArgb(24, 29, 35), listX, listY, listW, listH);
            canvas.DrawRectangle(Color.FromArgb(59, 70, 81), listX, listY, listW, listH);

            int visibleRows = Math.Max(1, listH / OpenRowHeight);
            for (int row = 0; row < visibleRows; row++)
            {
                int index = app.OpenScrollIndex + row;
                if (index >= app.OpenEntries.Count)
                    break;

                OpenEntry entry = app.OpenEntries[index];
                int rowY = listY + row * OpenRowHeight;
                bool selected = index == app.OpenSelectedIndex;
                if (selected)
                {
                    canvas.DrawFilledRectangle(Color.FromArgb(39, 66, 88), listX + 2, rowY + 1,
                        listW - 4, OpenRowHeight - 2);
                    canvas.DrawFilledRectangle(Accent, listX + 2, rowY + 1, 3, OpenRowHeight - 2);
                }

                IconManager.DrawScaled(canvas, entry.IsDirectory ? IconType.Folder : IconType.File,
                    listX + 10, rowY + 6, 18, 18);
                SmallTextRenderer.DrawClipped(canvas, entry.Name, listX + 38, rowY + 11,
                    listW - 50, selected ? Color.WhiteSmoke : MainText);
            }

            if (app.OpenEntries.Count == 0)
                SmallTextRenderer.Draw(canvas, "TEN KATALOG JEST PUSTY", listX + 16, listY + 16, SecondaryText);

            int buttonY = y + height - 44;
            int openX = x + width - 174;
            int cancelX = x + width - 88;
            canvas.DrawFilledRectangle(Color.FromArgb(42, 78, 105), openX, buttonY, 80, 30);
            canvas.DrawRectangle(Color.FromArgb(78, 147, 198), openX, buttonY, 80, 30);
            SmallTextRenderer.DrawCentered(canvas, "OPEN", openX, buttonY + 11, 80, Color.WhiteSmoke);

            canvas.DrawFilledRectangle(Color.FromArgb(49, 57, 66), cancelX, buttonY, 74, 30);
            canvas.DrawRectangle(Color.FromArgb(82, 96, 110), cancelX, buttonY, 74, 30);
            SmallTextRenderer.DrawCentered(canvas, "CANCEL", cancelX, buttonY + 11, 74, Color.WhiteSmoke);

            SmallTextRenderer.DrawClipped(canvas, "ENTER OPEN   BACKSPACE UP   ESC CANCEL",
                x + 14, y + height - 12, Math.Max(20, width - 210), SecondaryText);
        }
    }
}
