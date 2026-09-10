using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Graphics.Fonts;
using Cosmos.Kernel.System.Keyboard;

namespace ZonderqOS.GUI.Apps
{
    public class NanoApp : Application
    {
        private const long MaxFileBytes = 1024 * 1024;
        private const int MaxDocumentCharacters = 1024 * 1024;

        private readonly string filePath;
        private readonly Action closeCallback;
        private readonly List<string> lines = new List<string>();
        private int cursorX;
        private int cursorY;
        private int scrollY;
        private int documentCharacters;
        private string status = "";
        private readonly NanoView editor;

        public NanoApp(string path, Action onClose) : base("nano")
        {
            filePath = path;
            closeCallback = onClose;
            Window = new Window(80, 55, 1000, 650, "nano - " + path);
            Window.CloseAction = Close;

            LoadFile();
            editor = new NanoView(10, 40, 980, 565, lines, () => cursorX, () => cursorY, () => scrollY, () => status);
            Window.AddChild(editor);
            UpdateLayout();
        }

        private void LoadFile()
        {
            try
            {
                if (File.Exists(filePath))
                {
                    if (!PermissionManager.CanRead(filePath, SecurityContext.CurrentUser))
                    {
                        status = "Permission denied";
                        SecurityLogger.LogEvent("WARN", $"Unauthorized nano read attempt on {filePath} by {SecurityContext.CurrentUser}");
                    }
                    else
                    {
                        FileInfo fileInfo = new FileInfo(filePath);
                        if (fileInfo.Length > MaxFileBytes)
                        {
                            status = $"File too large (limit {MaxFileBytes / 1024} KB)";
                        }
                        else
                        {
                            string content = File.ReadAllText(filePath);
                            string[] loaded = content.Replace("\r", "").Split('\n');
                            lines.AddRange(loaded);
                            documentCharacters = content.Length;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                status = "Read error: " + ex.Message;
            }

            if (lines.Count == 0)
                lines.Add("");
        }

        private void UpdateLayout()
        {
            if (editor == null) return;
            editor.X = Window.X + 10;
            editor.Y = Window.Y + 40;
            editor.Width = Math.Max(300, Window.Width - 20);
            editor.Height = Math.Max(150, Window.Height - 85);
        }

        public override void Update()
        {
            UpdateLayout();
        }

        private void Save()
        {
            if (File.Exists(filePath) && !PermissionManager.CanWrite(filePath, SecurityContext.CurrentUser))
            {
                status = "Permission denied";
                SecurityLogger.LogEvent("WARN", $"Unauthorized nano write attempt on {filePath} by {SecurityContext.CurrentUser}");
                return;
            }

            try
            {
                Disk.CreateFile(filePath, string.Join("\n", lines));
                status = "Saved " + filePath;
            }
            catch (Exception ex)
            {
                status = "Save error: " + ex.Message;
            }
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            status = "";

            if (key.Key == ConsoleKeyEx.Escape || key.Key == ConsoleKeyEx.F3)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.F2)
            {
                Save();
                return;
            }

            if (key.Key == ConsoleKeyEx.UpArrow)
            {
                if (cursorY > 0) cursorY--;
            }
            else if (key.Key == ConsoleKeyEx.DownArrow)
            {
                if (cursorY < lines.Count - 1) cursorY++;
            }
            else if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                if (cursorX > 0) cursorX--;
                else if (cursorY > 0)
                {
                    cursorY--;
                    cursorX = lines[cursorY].Length;
                }
            }
            else if (key.Key == ConsoleKeyEx.RightArrow)
            {
                if (cursorX < lines[cursorY].Length) cursorX++;
                else if (cursorY < lines.Count - 1)
                {
                    cursorY++;
                    cursorX = 0;
                }
            }
            else if (key.Key == ConsoleKeyEx.Backspace)
            {
                if (cursorX > 0)
                {
                    lines[cursorY] = lines[cursorY].Remove(cursorX - 1, 1);
                    cursorX--;
                    if (documentCharacters > 0) documentCharacters--;
                }
                else if (cursorY > 0)
                {
                    int previousLength = lines[cursorY - 1].Length;
                    lines[cursorY - 1] += lines[cursorY];
                    lines.RemoveAt(cursorY);
                    cursorY--;
                    cursorX = previousLength;
                    if (documentCharacters > 0) documentCharacters--;
                }
            }
            else if (key.Key == ConsoleKeyEx.Enter)
            {
                if (!CanGrowDocument()) return;

                string remainder = lines[cursorY].Substring(cursorX);
                lines[cursorY] = lines[cursorY].Substring(0, cursorX);
                lines.Insert(cursorY + 1, remainder);
                cursorY++;
                cursorX = 0;
                documentCharacters++;
            }
            else if (key.KeyChar != '\0' && !char.IsControl(key.KeyChar))
            {
                if (!CanGrowDocument()) return;

                lines[cursorY] = lines[cursorY].Insert(cursorX, key.KeyChar.ToString());
                cursorX++;
                documentCharacters++;
            }

            if (cursorX > lines[cursorY].Length)
                cursorX = lines[cursorY].Length;

            if (cursorY < scrollY) scrollY = cursorY;
            int visible = Math.Max(1, (Window.Height - 105) / 32);
            if (cursorY >= scrollY + visible)
                scrollY = cursorY - visible + 1;
        }

        private bool CanGrowDocument()
        {
            if (documentCharacters < MaxDocumentCharacters)
                return true;

            status = $"Document limit: {MaxDocumentCharacters} characters";
            return false;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }
    }

    internal class NanoView : Widget
    {
        private readonly List<string> lines;
        private readonly Func<int> getCursorX;
        private readonly Func<int> getCursorY;
        private readonly Func<int> getScrollY;
        private readonly Func<string> getStatus;
        private readonly Font font = PCScreenFont.DefaultFont;

        public NanoView(int x, int y, int width, int height, List<string> textLines,
            Func<int> cursorX, Func<int> cursorY, Func<int> scrollY, Func<string> status)
            : base(x, y, width, height)
        {
            lines = textLines;
            getCursorX = cursorX;
            getCursorY = cursorY;
            getScrollY = scrollY;
            getStatus = status;
        }

        public override void Render(Canvas canvas)
        {
            if (!Visible) return;

            canvas.DrawFilledRectangle(Color.Black, X, Y, Width, Height);
            canvas.DrawRectangle(Color.DimGray, X, Y, Width, Height);

            int lineHeight = 32;
            int visibleLines = Math.Max(1, (Height - 42) / lineHeight);
            int scroll = getScrollY();
            int cy = getCursorY();
            int cx = getCursorX();

            for (int i = 0; i < visibleLines; i++)
            {
                int index = scroll + i;
                if (index >= lines.Count) break;

                string text = lines[index] ?? "";
                int maxChars = Math.Max(1, (Width - 16) / 16);
                if (text.Length > maxChars)
                    text = text.Substring(0, maxChars);

                canvas.DrawString(text, font, Color.White, X + 8, Y + 6 + i * lineHeight);
            }

            int cursorScreenY = cy - scroll;
            if (cursorScreenY >= 0 && cursorScreenY < visibleLines)
            {
                int cursorScreenX = Math.Min(cx, Math.Max(0, (Width - 20) / 16));
                canvas.DrawFilledRectangle(Color.LightGray,
                    X + 8 + cursorScreenX * 16,
                    Y + 6 + cursorScreenY * lineHeight,
                    2,
                    30);
            }

            string footer = string.IsNullOrEmpty(getStatus())
                ? "F2 Save    F3/Esc Exit"
                : getStatus();
            canvas.DrawFilledRectangle(Color.DimGray, X, Y + Height - 34, Width, 34);
            canvas.DrawString(footer, font, Color.White, X + 8, Y + Height - 30);
        }
    }
}
