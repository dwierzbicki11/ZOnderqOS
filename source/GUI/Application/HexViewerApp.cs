using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI.Apps
{
    public sealed class HexViewerApp : Application
    {
        private const int BytesPerRow = 16;
        private const int VisibleRows = 16;
        private const int BytesPerPage = BytesPerRow * VisibleRows; // 256 bytes

        private static readonly Color Surface = Color.FromArgb(16, 21, 27);
        private static readonly Color HeaderBg = Color.FromArgb(24, 30, 37);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color ButtonNormal = Color.FromArgb(33, 42, 52);
        private static readonly Color ButtonHover = Color.FromArgb(46, 60, 75);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(100, 116, 132);
        private static readonly Color OffsetCol = Color.FromArgb(73, 163, 224);
        private static readonly Color HexByteCol = Color.FromArgb(220, 226, 232);
        private static readonly Color ZeroByteCol = Color.FromArgb(70, 82, 95);
        private static readonly Color AsciiCol = Color.FromArgb(78, 185, 126);

        private static readonly string[] SampleFiles = new string[]
        {
            "/etc/os-release",
            "/etc/hostname",
            "/sys/cpu",
            "/sys/meminfo",
            "/proc/uptime",
            "/proc/version"
        };

        private string currentPath;
        private string cachedTitleText = "Brak pliku (0 B)";
        private string cachedPosText = "Poz: 0x0000 / 0x0000";
        private int cachedPosOffset = -1;
        private int cachedPosTotal = -1;
        private NativeScratchBuffer<byte> fileBuffer;
        private int currentOffset;
        private int sampleIndex;
        private readonly Action closeCallback;
        private int hoveredBtn = -1; // 0: Prev, 1: Next, 2: OpenNext

        public HexViewerApp(int x, int y, Action onClose) : base("Hex Viewer")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 540, 400, "Hex Viewer");
            Window.CloseAction = Close;
            OpenFile(SampleFiles[0]);
        }

        public HexViewerApp(int x, int y, string filePath, Action onClose) : base("Hex Viewer")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 540, 400, "Hex Viewer");
            Window.CloseAction = Close;
            OpenFile(filePath);
        }

        public override void Close()
        {
            fileBuffer.Dispose();
            currentPath = string.Empty;
            cachedTitleText = string.Empty;
            cachedPosText = string.Empty;
            base.Close();
            closeCallback?.Invoke();
        }

        private static string NormalizeSafePath(string path)
        {
            string normalized = (path ?? string.Empty).Trim().Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return FileClipboard.IsSafeAbsolutePath(normalized) ? normalized : string.Empty;
        }

        private void SetStatusBuffer(string titleSuffix, string headerText, string message)
        {
            fileBuffer.Allocate(message.Length);
            for (int i = 0; i < message.Length; i++)
                fileBuffer[i] = (byte)message[i];
            Window.Title = "Hex Viewer - " + titleSuffix;
            cachedTitleText = headerText + " (" + fileBuffer.Length + " B)";
            cachedPosOffset = -1;
        }

        public void OpenFile(string path)
        {
            try
            {
                currentOffset = 0;
                string normalized = NormalizeSafePath(path);
                if (string.IsNullOrEmpty(normalized))
                {
                    currentPath = string.Empty;
                    SetStatusBuffer("Nieprawidlowa sciezka", "Nieprawidlowa sciezka", "Nieprawidlowa sciezka pliku.");
                    return;
                }

                if (!global::ZonderqOS.SecurityContext.IsAuthenticated ||
                    !global::ZonderqOS.PermissionManager.CanRead(normalized, global::ZonderqOS.SecurityContext.CurrentUser))
                {
                    currentPath = string.Empty;
                    global::ZonderqOS.SecurityLogger.LogEvent(
                        "WARN",
                        "Unauthorized HexViewer read attempt on " + normalized + " by " +
                        (global::ZonderqOS.SecurityContext.CurrentUser ?? "?"));
                    SetStatusBuffer("Brak uprawnien", "Brak uprawnien do odczytu", "Brak uprawnien do odczytu pliku.");
                    return;
                }

                currentPath = normalized;
                if (File.Exists(normalized))
                {
                    using (FileStream fs = new FileStream(normalized, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        long rawLen = fs.Length;
                        int length = rawLen <= 0 ? 0 : (int)Math.Min(rawLen, 16L * 1024L * 1024L);
                        fileBuffer.Allocate(length);
                        Span<byte> dest = fileBuffer.AsSpan();
                        int totalRead = 0;
                        while (totalRead < length)
                        {
                            int read = fs.Read(dest.Slice(totalRead, length - totalRead));
                            if (read <= 0)
                                break;
                            totalRead += read;
                        }
                    }
                }
                else
                {
                    // Fallback to placeholder info bytes
                    string fallback = "ZOnderqOS Hex Viewer\nPlik: " + normalized + "\nNie odnaleziono pliku lub sciezka jest wirtualna.";
                    fileBuffer.Allocate(fallback.Length);
                    for (int i = 0; i < fallback.Length; i++)
                        fileBuffer[i] = (byte)fallback[i];
                }
                string fileName = Path.GetFileName(normalized);
                Window.Title = "Hex Viewer - " + (string.IsNullOrEmpty(fileName) ? normalized : fileName);
                int len = fileBuffer.Length;
                cachedTitleText = (currentPath ?? "Brak pliku") + " (" + len + " B)";
                cachedPosOffset = -1;
            }
            catch (Exception ex)
            {
                string err = "Blad odczytu: " + ex.Message;
                fileBuffer.Allocate(err.Length);
                for (int i = 0; i < err.Length; i++)
                    fileBuffer[i] = (byte)err[i];
                int len = fileBuffer.Length;
                cachedTitleText = (currentPath ?? "Brak pliku") + " (" + len + " B)";
                cachedPosOffset = -1;
            }
        }

        private void CycleSample()
        {
            sampleIndex = (sampleIndex + 1) % SampleFiles.Length;
            OpenFile(SampleFiles[sampleIndex]);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.PageUp)
            {
                currentOffset = Math.Max(0, currentOffset - BytesPerPage);
            }
            else if (key.Key == ConsoleKeyEx.PageDown)
            {
                int maxOffset = Math.Max(0, fileBuffer.Length - BytesPerPage);
                currentOffset = Math.Min(maxOffset, currentOffset + BytesPerPage);
            }
            else if (key.Key == ConsoleKeyEx.UpArrow)
            {
                currentOffset = Math.Max(0, currentOffset - BytesPerRow);
            }
            else if (key.Key == ConsoleKeyEx.DownArrow)
            {
                int maxOffset = Math.Max(0, fileBuffer.Length - BytesPerRow);
                currentOffset = Math.Min(maxOffset, currentOffset + BytesPerRow);
            }
            else if (key.Key == ConsoleKeyEx.Home)
            {
                currentOffset = 0;
            }
            else if (key.Key == ConsoleKeyEx.End)
            {
                int maxOffset = Math.Max(0, fileBuffer.Length - BytesPerPage);
                currentOffset = maxOffset;
            }
            else if (key.Key == ConsoleKeyEx.Spacebar)
            {
                CycleSample();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Bottom Nav Bar Check
            int barY = contentY + contentHeight - 32;
            hoveredBtn = -1;

            if (mouseY >= barY && mouseY < barY + 26)
            {
                int b1X = contentX + 8;
                int b2X = contentX + 110;
                int b3X = contentX + contentWidth - 130;

                if (mouseX >= b1X && mouseX < b1X + 96) hoveredBtn = 0; // Prev
                else if (mouseX >= b2X && mouseX < b2X + 96) hoveredBtn = 1; // Next
                else if (mouseX >= b3X && mouseX < b3X + 120) hoveredBtn = 2; // Cycle
            }

            if (leftClicked && !leftWasClicked && hoveredBtn >= 0)
            {
                if (hoveredBtn == 0)
                {
                    currentOffset = Math.Max(0, currentOffset - BytesPerPage);
                }
                else if (hoveredBtn == 1)
                {
                    int maxOffset = Math.Max(0, fileBuffer.Length - BytesPerPage);
                    currentOffset = Math.Min(maxOffset, currentOffset + BytesPerPage);
                }
                else if (hoveredBtn == 2)
                {
                    CycleSample();
                }
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int contentX = Window.X + 8;
            int contentY = Window.Y + 38;
            int contentWidth = Math.Max(1, Window.Width - 16);
            int contentHeight = Math.Max(1, Window.Height - 46);

            // Background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // Header Panel
            canvas.DrawFilledRectangle(HeaderBg, contentX, contentY, contentWidth, 30);
            canvas.DrawFilledRectangle(Border, contentX, contentY + 29, contentWidth, 1);

            SmallTextRenderer.Draw(canvas, cachedTitleText, contentX + 8, contentY + 11, TextColor);

            // Table Header Row
            int gridTop = contentY + 34;
            SmallTextRenderer.Draw(canvas, "OFFSET", contentX + 8, gridTop, MutedColor);
            SmallTextRenderer.Draw(canvas, "00 01 02 03 04 05 06 07  08 09 0A 0B 0C 0D 0E 0F", contentX + 80, gridTop, MutedColor);
            SmallTextRenderer.Draw(canvas, "DEKODOWANIE ASCII", contentX + 380, gridTop, MutedColor);

            canvas.DrawFilledRectangle(Border, contentX + 4, gridTop + 13, contentWidth - 8, 1);

            // Hex and ASCII rows
            int rowY = gridTop + 18;
            int totalBytes = fileBuffer.Length;

            for (int r = 0; r < VisibleRows; r++)
            {
                int rowOffset = currentOffset + r * BytesPerRow;
                if (rowOffset >= totalBytes && totalBytes > 0)
                    break;

                // 1. Draw Offset hex string (e.g. 00000000)
                DrawHex32(canvas, (uint)rowOffset, contentX + 8, rowY, OffsetCol);

                // 2. Draw 16 hex bytes
                int hx = contentX + 80;
                for (int b = 0; b < BytesPerRow; b++)
                {
                    int byteIdx = rowOffset + b;
                    if (byteIdx < totalBytes)
                    {
                        byte val = fileBuffer[byteIdx];
                        Color col = (val == 0) ? ZeroByteCol : HexByteCol;
                        DrawHex8(canvas, val, hx, rowY, col);
                    }
                    else
                    {
                        SmallTextRenderer.Draw(canvas, "  ", hx, rowY, MutedColor);
                    }

                    hx += 18;
                    if (b == 7) // Gap between 8-byte blocks
                        hx += 6;
                }

                // 3. Draw ASCII representation
                int ax = contentX + 380;
                for (int b = 0; b < BytesPerRow; b++)
                {
                    int byteIdx = rowOffset + b;
                    if (byteIdx < totalBytes)
                    {
                        byte val = fileBuffer[byteIdx];
                        char ch = (val >= 32 && val <= 126) ? (char)val : '.';
                        SmallTextRenderer.DrawSingleChar(canvas, ch, ax, rowY, AsciiCol);
                    }
                    ax += 8;
                }

                rowY += 16;
            }

            // Bottom Navigation Bar
            int barY = contentY + contentHeight - 30;
            canvas.DrawFilledRectangle(HeaderBg, contentX, barY - 2, contentWidth, 32);
            canvas.DrawFilledRectangle(Border, contentX, barY - 2, contentWidth, 1);

            DrawNavButton(canvas, contentX + 8, barY + 1, 96, 24, "POPRZEDNIE", hoveredBtn == 0);
            DrawNavButton(canvas, contentX + 110, barY + 1, 96, 24, "NASTEPNE", hoveredBtn == 1);
            DrawNavButton(canvas, contentX + contentWidth - 130, barY + 1, 120, 24, "INNY PLIK [SPACJA]", hoveredBtn == 2);

            if (currentOffset != cachedPosOffset || totalBytes != cachedPosTotal)
            {
                cachedPosOffset = currentOffset;
                cachedPosTotal = totalBytes;
                cachedPosText = "Poz: 0x" + currentOffset.ToString("X4") + " / 0x" + totalBytes.ToString("X4");
            }
            SmallTextRenderer.Draw(canvas, cachedPosText, contentX + 220, barY + 9, MutedColor);
        }

        private static void DrawNavButton(Canvas canvas, int x, int y, int w, int h, string text, bool hovered)
        {
            Color bg = hovered ? ButtonHover : ButtonNormal;
            canvas.DrawFilledRectangle(bg, x, y, w, h);
            canvas.DrawFilledRectangle(Border, x, y, w, 1);
            canvas.DrawFilledRectangle(Border, x, y + h - 1, w, 1);
            canvas.DrawFilledRectangle(Border, x, y, 1, h);
            canvas.DrawFilledRectangle(Border, x + w - 1, y, 1, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + 8, w, TextColor);
        }

        private static readonly char[] HexDigits = "0123456789ABCDEF".ToCharArray();

        private static void DrawHex8(Canvas canvas, byte val, int x, int y, Color color)
        {
            char h1 = HexDigits[(val >> 4) & 0x0F];
            char h2 = HexDigits[val & 0x0F];
            SmallTextRenderer.DrawSingleChar(canvas, h1, x, y, color);
            SmallTextRenderer.DrawSingleChar(canvas, h2, x + 6, y, color);
        }

        private static void DrawHex32(Canvas canvas, uint val, int x, int y, Color color)
        {
            for (int i = 7; i >= 0; i--)
            {
                SmallTextRenderer.DrawSingleChar(canvas, HexDigits[val & 0x0F], x + i * 6, y, color);
                val >>= 4;
            }
            SmallTextRenderer.DrawSingleChar(canvas, ':', x + 48, y, color);
        }
    }
}
