using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Apps
{
    public sealed class PaintApp : Application
    {
        private const int CanvasWidth = 500;
        private const int CanvasHeight = 300;

        private static readonly Color Surface = Color.FromArgb(20, 25, 31);
        private static readonly Color ToolPanelBg = Color.FromArgb(27, 34, 41);
        private static readonly Color Border = Color.FromArgb(57, 70, 82);
        private static readonly Color ButtonNormal = Color.FromArgb(33, 42, 52);
        private static readonly Color ButtonActive = Color.FromArgb(58, 86, 120);
        private static readonly Color ButtonHover = Color.FromArgb(46, 60, 75);
        private static readonly Color TextColor = Color.FromArgb(234, 239, 244);
        private static readonly Color MutedColor = Color.FromArgb(132, 149, 164);
        private static readonly Color AccentColor = Color.FromArgb(78, 185, 126);

        private enum Tool
        {
            Pencil = 0,
            Brush = 1,
            Eraser = 2,
            Line = 3,
            Rectangle = 4,
            FilledRectangle = 5,
            Bucket = 6
        }

        private static readonly uint[] PaletteColors = new uint[]
        {
            0xFF000000, // Czarny
            0xFF404040, // Ciemnoszary
            0xFF808080, // Szary
            0xFFFFFFFF, // Bialy
            0xFFE03030, // Czerwony
            0xFF990000, // Bordowy
            0xFFFF8000, // Pomaranczowy
            0xFFFFD700, // Zolty
            0xFF32CD32, // Limonkowy
            0xFF008000, // Zielony
            0xFF00CED1, // Morski
            0xFF1E90FF, // Blekitny
            0xFF0000CD, // Niebieski
            0xFF8A2BE2, // Fioletowy
            0xFFFF1493, // Rozowy
            0xFF8B4513  // Brazowy
        };

        private readonly uint[] canvasBuffer = new uint[CanvasWidth * CanvasHeight];
        private Tool activeTool = Tool.Pencil;
        private uint currentColor = 0xFF000000; // Black default
        private bool isDrawing;
        private int lastCanvasX = -1;
        private int lastCanvasY = -1;
        private int dragStartX = -1;
        private int dragStartY = -1;
        private string statusMessage = "Gotowy";
        private int saveCounter = 1;
        private readonly Action closeCallback;

        public PaintApp(int x, int y, Action onClose) : base("Paint")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 540, 440, "Paint");
            Window.CloseAction = Close;
            ClearCanvas();
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        private void ClearCanvas()
        {
            for (int i = 0; i < canvasBuffer.Length; i++)
                canvasBuffer[i] = 0xFFFFFFFF; // White
            statusMessage = "Wyczyszczono plotno";
        }

        private void SaveDrawing()
        {
            try
            {
                byte[] bmpBytes = BmpEncoder.Encode(CanvasWidth, CanvasHeight, canvasBuffer);
                if (bmpBytes.Length == 0)
                {
                    statusMessage = "Blad generowania pliku";
                    return;
                }

                string targetDir = "0:/Paint";
                if (!Directory.Exists(targetDir))
                {
                    try { Directory.CreateDirectory(targetDir); }
                    catch { targetDir = "0:/"; }
                }

                string filePath = Path.Combine(targetDir, "rysunek_" + saveCounter + ".bmp");
                while (File.Exists(filePath) && saveCounter < 999)
                {
                    saveCounter++;
                    filePath = Path.Combine(targetDir, "rysunek_" + saveCounter + ".bmp");
                }

                File.WriteAllBytes(filePath, bmpBytes);
                statusMessage = "Zapisano: " + Path.GetFileName(filePath);
                saveCounter++;

                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Info,
                    "PAINT",
                    "Zapisano rysunek",
                    "Plik: " + filePath);
            }
            catch (Exception ex)
            {
                statusMessage = "Blad zapisu: " + ex.Message;
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

            // 1. Tool Bar Click Check (Y: contentY + 6, H: 26)
            int tbY = contentY + 6;
            if (leftClicked && !leftWasClicked && mouseY >= tbY && mouseY < tbY + 26)
            {
                int curX = contentX + 6;
                // Tools 0 to 6
                for (int t = 0; t <= 6; t++)
                {
                    if (mouseX >= curX && mouseX < curX + 28)
                    {
                        activeTool = (Tool)t;
                        statusMessage = "Narzedzie: " + GetToolName(activeTool);
                        return;
                    }
                    curX += 32;
                }

                // Action buttons: [WYCZYSC] (62px), [ZAPISZ] (62px)
                curX += 16;
                if (mouseX >= curX && mouseX < curX + 66)
                {
                    ClearCanvas();
                    return;
                }
                curX += 72;
                if (mouseX >= curX && mouseX < curX + 66)
                {
                    SaveDrawing();
                    return;
                }
            }

            // 2. Palette Click Check (Y: contentY + 36, H: 22)
            int palY = contentY + 36;
            if (leftClicked && !leftWasClicked && mouseY >= palY && mouseY < palY + 22)
            {
                int px = contentX + 6;
                for (int c = 0; c < PaletteColors.Length; c++)
                {
                    if (mouseX >= px && mouseX < px + 18)
                    {
                        currentColor = PaletteColors[c];
                        statusMessage = "Wybrano kolor";
                        return;
                    }
                    px += 22;
                }
            }

            // 3. Canvas Area Interaction
            int canvasOriginX = contentX + (contentWidth - CanvasWidth) / 2;
            int canvasOriginY = contentY + 64;

            int cx = mouseX - canvasOriginX;
            int cy = mouseY - canvasOriginY;
            bool insideCanvas = (cx >= 0 && cx < CanvasWidth && cy >= 0 && cy < CanvasHeight);

            if (leftClicked)
            {
                if (!leftWasClicked)
                {
                    // Mouse Pressed
                    if (insideCanvas)
                    {
                        isDrawing = true;
                        dragStartX = cx;
                        dragStartY = cy;
                        lastCanvasX = cx;
                        lastCanvasY = cy;

                        if (activeTool == Tool.Bucket)
                        {
                            FloodFill(cx, cy, currentColor);
                            isDrawing = false;
                        }
                        else if (activeTool == Tool.Pencil)
                        {
                            SetPixel(cx, cy, currentColor);
                        }
                        else if (activeTool == Tool.Brush)
                        {
                            DrawBrush(cx, cy, currentColor);
                        }
                        else if (activeTool == Tool.Eraser)
                        {
                            DrawEraser(cx, cy);
                        }
                    }
                }
                else if (isDrawing && insideCanvas)
                {
                    // Mouse Dragged
                    if (activeTool == Tool.Pencil)
                    {
                        DrawLine(lastCanvasX, lastCanvasY, cx, cy, currentColor, 1);
                        lastCanvasX = cx;
                        lastCanvasY = cy;
                    }
                    else if (activeTool == Tool.Brush)
                    {
                        DrawLine(lastCanvasX, lastCanvasY, cx, cy, currentColor, 3);
                        lastCanvasX = cx;
                        lastCanvasY = cy;
                    }
                    else if (activeTool == Tool.Eraser)
                    {
                        DrawEraserLine(lastCanvasX, lastCanvasY, cx, cy);
                        lastCanvasX = cx;
                        lastCanvasY = cy;
                    }
                }
            }
            else if (leftWasClicked && isDrawing)
            {
                // Mouse Released: commit geometric shapes
                isDrawing = false;
                int endX = Math.Max(0, Math.Min(CanvasWidth - 1, cx));
                int endY = Math.Max(0, Math.Min(CanvasHeight - 1, cy));

                if (activeTool == Tool.Line)
                {
                    DrawLine(dragStartX, dragStartY, endX, endY, currentColor, 1);
                }
                else if (activeTool == Tool.Rectangle)
                {
                    DrawRect(dragStartX, dragStartY, endX, endY, currentColor, false);
                }
                else if (activeTool == Tool.FilledRectangle)
                {
                    DrawRect(dragStartX, dragStartY, endX, endY, currentColor, true);
                }
                lastCanvasX = -1;
                lastCanvasY = -1;
            }
        }

        private static string GetToolName(Tool tool)
        {
            switch (tool)
            {
                case Tool.Pencil: return "Olowek (1px)";
                case Tool.Brush: return "Pedzel (3px)";
                case Tool.Eraser: return "Gumka";
                case Tool.Line: return "Linia";
                case Tool.Rectangle: return "Prostokat";
                case Tool.FilledRectangle: return "Wypelniony prostokat";
                case Tool.Bucket: return "Wypelnienie";
                default: return string.Empty;
            }
        }

        private void SetPixel(int x, int y, uint color)
        {
            if (x >= 0 && x < CanvasWidth && y >= 0 && y < CanvasHeight)
                canvasBuffer[y * CanvasWidth + x] = color;
        }

        private void DrawBrush(int x, int y, uint color)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dx = -1; dx <= 1; dx++)
                    SetPixel(x + dx, y + dy, color);
            }
        }

        private void DrawEraser(int x, int y)
        {
            for (int dy = -3; dy <= 3; dy++)
            {
                for (int dx = -3; dx <= 3; dx++)
                    SetPixel(x + dx, y + dy, 0xFFFFFFFF);
            }
        }

        private void DrawLine(int x0, int y0, int x1, int y1, uint color, int thickness)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                if (thickness > 1)
                    DrawBrush(x0, y0, color);
                else
                    SetPixel(x0, y0, color);

                if (x0 == x1 && y0 == y1)
                    break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawEraserLine(int x0, int y0, int x1, int y1)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;
            int err = dx - dy;

            while (true)
            {
                DrawEraser(x0, y0);
                if (x0 == x1 && y0 == y1)
                    break;
                int e2 = 2 * err;
                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }
                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private void DrawRect(int x0, int y0, int x1, int y1, uint color, bool filled)
        {
            int left = Math.Min(x0, x1);
            int right = Math.Max(x0, x1);
            int top = Math.Min(y0, y1);
            int bottom = Math.Max(y0, y1);

            if (filled)
            {
                for (int y = top; y <= bottom; y++)
                {
                    for (int x = left; x <= right; x++)
                        SetPixel(x, y, color);
                }
            }
            else
            {
                for (int x = left; x <= right; x++)
                {
                    SetPixel(x, top, color);
                    SetPixel(x, bottom, color);
                }
                for (int y = top; y <= bottom; y++)
                {
                    SetPixel(left, y, color);
                    SetPixel(right, y, color);
                }
            }
        }

        private void FloodFill(int startX, int startY, uint targetColor)
        {
            if (startX < 0 || startX >= CanvasWidth || startY < 0 || startY >= CanvasHeight)
                return;

            uint sourceColor = canvasBuffer[startY * CanvasWidth + startX];
            if (sourceColor == targetColor)
                return;

            // Simple queue BFS flood-fill
            Queue<int> queue = new Queue<int>(256);
            queue.Enqueue(startY * CanvasWidth + startX);
            canvasBuffer[startY * CanvasWidth + startX] = targetColor;

            int iterations = 0;
            int maxIterations = CanvasWidth * CanvasHeight;

            while (queue.Count > 0 && iterations < maxIterations)
            {
                int curr = queue.Dequeue();
                int cx = curr % CanvasWidth;
                int cy = curr / CanvasWidth;
                iterations++;

                // 4-way neighbors
                if (cx > 0 && canvasBuffer[curr - 1] == sourceColor)
                {
                    canvasBuffer[curr - 1] = targetColor;
                    queue.Enqueue(curr - 1);
                }
                if (cx < CanvasWidth - 1 && canvasBuffer[curr + 1] == sourceColor)
                {
                    canvasBuffer[curr + 1] = targetColor;
                    queue.Enqueue(curr + 1);
                }
                if (cy > 0 && canvasBuffer[curr - CanvasWidth] == sourceColor)
                {
                    canvasBuffer[curr - CanvasWidth] = targetColor;
                    queue.Enqueue(curr - CanvasWidth);
                }
                if (cy < CanvasHeight - 1 && canvasBuffer[curr + CanvasWidth] == sourceColor)
                {
                    canvasBuffer[curr + CanvasWidth] = targetColor;
                    queue.Enqueue(curr + CanvasWidth);
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

            // Fill background
            canvas.DrawFilledRectangle(Surface, contentX, contentY, contentWidth, contentHeight);

            // 1. Top Tool Bar Panel
            int tbY = contentY + 4;
            canvas.DrawFilledRectangle(ToolPanelBg, contentX + 4, tbY, contentWidth - 8, 28);
            canvas.DrawFilledRectangle(Border, contentX + 4, tbY + 27, contentWidth - 8, 1);

            int curX = contentX + 6;
            string[] toolLabels = new string[] { "OLW", "PED", "GUM", "LIN", "REC", "WPR", "WIA" };
            for (int t = 0; t <= 6; t++)
            {
                Color btnBg = (activeTool == (Tool)t) ? ButtonActive : ButtonNormal;
                canvas.DrawFilledRectangle(btnBg, curX, tbY + 2, 28, 24);
                canvas.DrawFilledRectangle(Border, curX, tbY + 2, 28, 1);
                canvas.DrawFilledRectangle(Border, curX, tbY + 25, 28, 1);
                canvas.DrawFilledRectangle(Border, curX, tbY + 2, 1, 24);
                canvas.DrawFilledRectangle(Border, curX + 27, tbY + 2, 1, 24);

                Color textColor = (activeTool == (Tool)t) ? AccentColor : TextColor;
                SmallTextRenderer.DrawCentered(canvas, toolLabels[t], curX, tbY + 9, 28, textColor);
                curX += 32;
            }

            // Action buttons
            curX += 16;
            DrawButton(canvas, curX, tbY + 2, 66, 24, "NOWY", TextColor);
            curX += 72;
            DrawButton(canvas, curX, tbY + 2, 66, 24, "ZAPISZ", AccentColor);

            // 2. Color Palette Bar
            int palY = contentY + 34;
            canvas.DrawFilledRectangle(ToolPanelBg, contentX + 4, palY, contentWidth - 8, 24);
            canvas.DrawFilledRectangle(Border, contentX + 4, palY + 23, contentWidth - 8, 1);

            int px = contentX + 6;
            for (int c = 0; c < PaletteColors.Length; c++)
            {
                uint col = PaletteColors[c];
                Color dotColor = Color.FromArgb((int)(col | 0xFF000000));
                canvas.DrawFilledRectangle(dotColor, px, palY + 3, 18, 18);
                canvas.DrawFilledRectangle(Border, px, palY + 3, 18, 1);
                canvas.DrawFilledRectangle(Border, px, palY + 20, 18, 1);
                canvas.DrawFilledRectangle(Border, px, palY + 3, 1, 18);
                canvas.DrawFilledRectangle(Border, px + 17, palY + 3, 1, 18);

                if (col == currentColor)
                {
                    // Selection indicator
                    canvas.DrawFilledRectangle(Color.White, px + 6, palY + 9, 6, 6);
                }
                px += 22;
            }

            // Current color preview box
            int prevX = contentX + contentWidth - 80;
            Color currCol = Color.FromArgb((int)(currentColor | 0xFF000000));
            canvas.DrawFilledRectangle(currCol, prevX, palY + 3, 68, 18);
            canvas.DrawFilledRectangle(Border, prevX, palY + 3, 68, 1);
            canvas.DrawFilledRectangle(Border, prevX, palY + 20, 68, 1);
            canvas.DrawFilledRectangle(Border, prevX, palY + 3, 1, 18);
            canvas.DrawFilledRectangle(Border, prevX + 67, palY + 3, 1, 18);

            // 3. Canvas Frame and Pixels
            int canvasOriginX = contentX + (contentWidth - CanvasWidth) / 2;
            int canvasOriginY = contentY + 62;

            canvas.DrawFilledRectangle(Border, canvasOriginX - 1, canvasOriginY - 1, CanvasWidth + 2, CanvasHeight + 2);

            // Blit canvas buffer to screen
            for (int y = 0; y < CanvasHeight; y++)
            {
                int rowOffset = y * CanvasWidth;
                int screenY = canvasOriginY + y;
                for (int x = 0; x < CanvasWidth; x++)
                {
                    uint argb = canvasBuffer[rowOffset + x];
                    Color c = Color.FromArgb((int)argb);
                    canvas.DrawPoint(c, canvasOriginX + x, screenY);
                }
            }

            // 4. Status Bar at the bottom
            int statY = contentY + contentHeight - 20;
            SmallTextRenderer.Draw(canvas, statusMessage, contentX + 8, statY + 5, MutedColor);
            string dimText = "500 x 300 px";
            int dw = SmallTextRenderer.Width(dimText);
            SmallTextRenderer.Draw(canvas, dimText, contentX + contentWidth - dw - 12, statY + 5, MutedColor);
        }

        private static void DrawButton(Canvas canvas, int x, int y, int w, int h, string text, Color textColor)
        {
            canvas.DrawFilledRectangle(ButtonNormal, x, y, w, h);
            canvas.DrawFilledRectangle(Border, x, y, w, 1);
            canvas.DrawFilledRectangle(Border, x, y + h - 1, w, 1);
            canvas.DrawFilledRectangle(Border, x, y, 1, h);
            canvas.DrawFilledRectangle(Border, x + w - 1, y, 1, h);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + 8, w, textColor);
        }
    }
}
