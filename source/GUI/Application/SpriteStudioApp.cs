using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.GUI;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    public sealed class SpriteStudioApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(20, 24, 30);
        private static readonly Color Panel = Color.FromArgb(28, 36, 46);
        private static readonly Color PanelHover = Color.FromArgb(40, 52, 66);
        private static readonly Color Border = Color.FromArgb(56, 70, 88);
        private static readonly Color TextColor = Color.FromArgb(240, 245, 252);
        private static readonly Color MutedColor = Color.FromArgb(140, 155, 172);
        private static readonly Color AccentColor = Color.FromArgb(45, 145, 235);
        private static readonly Color GreenColor = Color.FromArgb(60, 215, 110);
        private static readonly Color GoldColor = Color.FromArgb(245, 205, 50);

        private const int GridSize = 16;
        private const int CellSize = 14;
        private readonly byte[,] pixels = new byte[GridSize, GridSize];

        // 16-color VGA/Retro palette
        private static readonly Color[] Palette = new Color[]
        {
            Color.FromArgb(0, 0, 0),         // 0: Black / Transparent
            Color.FromArgb(0, 0, 168),       // 1: Dark Blue
            Color.FromArgb(0, 168, 0),       // 2: Dark Green
            Color.FromArgb(0, 168, 168),     // 3: Dark Cyan
            Color.FromArgb(168, 0, 0),       // 4: Dark Red
            Color.FromArgb(168, 0, 168),     // 5: Dark Magenta
            Color.FromArgb(168, 84, 0),      // 6: Brown
            Color.FromArgb(168, 168, 168),   // 7: Light Gray
            Color.FromArgb(84, 84, 84),      // 8: Dark Gray
            Color.FromArgb(84, 84, 252),     // 9: Bright Blue
            Color.FromArgb(84, 252, 84),     // 10: Bright Green
            Color.FromArgb(84, 252, 252),    // 11: Bright Cyan
            Color.FromArgb(252, 84, 84),     // 12: Bright Red
            Color.FromArgb(252, 84, 252),    // 13: Bright Magenta
            Color.FromArgb(252, 252, 84),    // 14: Yellow
            Color.FromArgb(255, 255, 255)    // 15: White
        };

        private byte selectedColor = 15; // White by default
        private int currentTool = 0; // 0 = Pencil, 1 = Eraser, 2 = Bucket Fill
        private string statusMessage = "Gotowy do rysowania. Kliknij siatke aby dodac piksel.";
        private readonly Action closeCallback;

        // Flood fill queue
        private readonly int[] fillQueueX = new int[GridSize * GridSize];
        private readonly int[] fillQueueY = new int[GridSize * GridSize];

        public SpriteStudioApp(int x, int y, Action onClose) : base("Studio Sprite'ów")
        {
            closeCallback = onClose;
            Window = new Window(x, y, 590, 425, "Studio Sprite'ów i Ikon 16x16");
            Window.CloseAction = Close;

            // Load an initial demo smiley/star sprite
            InitDemoSprite();
        }

        private void InitDemoSprite()
        {
            // Simple 16x16 star / shield icon
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    pixels[r, c] = 0;
                }
            }

            // Draw a demo friendly sword/gem
            for (int i = 3; i <= 12; i++)
            {
                pixels[i, 7] = 11;
                pixels[i, 8] = 11;
            }
            for (int i = 5; i <= 10; i++)
            {
                pixels[7, i] = 14;
                pixels[8, i] = 14;
            }
            pixels[2, 7] = 15;
            pixels[2, 8] = 15;
            pixels[13, 7] = 12;
            pixels[13, 8] = 12;
        }

        public override void Close()
        {
            base.Close();
            closeCallback?.Invoke();
        }

        public override void HandleMouse(int mouseX, int mouseY, bool leftClicked, bool leftWasClicked,
            bool rightClicked, bool rightWasClicked)
        {
            base.HandleMouse(mouseX, mouseY, leftClicked, leftWasClicked, rightClicked, rightWasClicked);

            if (!Window.Visible || Window.IsMinimized)
                return;

            int wx = Window.X;
            int wy = Window.Y + Window.HeaderHeight;

            int gridX = wx + 16;
            int gridY = wy + 16;
            int gridPixelSize = GridSize * CellSize;

            // Canvas drawing (Left click or drag)
            if (leftClicked && Hit(mouseX, mouseY, gridX, gridY, gridPixelSize, gridPixelSize))
            {
                int col = (mouseX - gridX) / CellSize;
                int row = (mouseY - gridY) / CellSize;
                if (col >= 0 && col < GridSize && row >= 0 && row < GridSize)
                {
                    if (currentTool == 0) // Pencil
                    {
                        pixels[row, col] = selectedColor;
                    }
                    else if (currentTool == 1) // Eraser
                    {
                        pixels[row, col] = 0;
                    }
                    else if (currentTool == 2 && !leftWasClicked) // Flood fill on single click
                    {
                        FloodFill(row, col, selectedColor);
                    }
                }
                return;
            }

            // Right click = erase pixel
            if (rightClicked && Hit(mouseX, mouseY, gridX, gridY, gridPixelSize, gridPixelSize))
            {
                int col = (mouseX - gridX) / CellSize;
                int row = (mouseY - gridY) / CellSize;
                if (col >= 0 && col < GridSize && row >= 0 && row < GridSize)
                {
                    pixels[row, col] = 0;
                }
                return;
            }

            // Button clicks (only on fresh click)
            if (leftClicked && !leftWasClicked)
            {
                // Palette selection (2 columns of 8 swatches)
                int palX = wx + 260;
                int palY = wy + 16;
                for (int i = 0; i < 16; i++)
                {
                    int px = palX + (i % 2) * 26;
                    int py = palY + (i / 2) * 24;
                    if (Hit(mouseX, mouseY, px, py, 22, 20))
                    {
                        selectedColor = (byte)i;
                        statusMessage = "Wybrano kolor: #" + i;
                        return;
                    }
                }

                // Tool selection
                int toolX = wx + 330;
                int toolY = wy + 16;
                int toolW = 100;
                int toolH = 24;

                if (Hit(mouseX, mouseY, toolX, toolY, toolW, toolH)) { currentTool = 0; statusMessage = "Narzedzie: Ołówek"; return; }
                if (Hit(mouseX, mouseY, toolX, toolY + 28, toolW, toolH)) { currentTool = 1; statusMessage = "Narzedzie: Gumka"; return; }
                if (Hit(mouseX, mouseY, toolX, toolY + 56, toolW, toolH)) { currentTool = 2; statusMessage = "Narzedzie: Wypełnienie"; return; }

                // Canvas transformations
                int actX = wx + 440;
                int actY = wy + 16;
                int actW = 125;
                if (Hit(mouseX, mouseY, actX, actY, actW, toolH)) { FlipHorizontal(); statusMessage = "Odbito w poziomie (Flip X)."; return; }
                if (Hit(mouseX, mouseY, actX, actY + 28, actW, toolH)) { FlipVertical(); statusMessage = "Odbito w pionie (Flip Y)."; return; }
                if (Hit(mouseX, mouseY, actX, actY + 56, actW, toolH)) { InvertColors(); statusMessage = "Odwrócono kolory."; return; }
                if (Hit(mouseX, mouseY, actX, actY + 84, actW, toolH)) { ClearCanvas(); statusMessage = "Wyczyszczono siatke robocza."; return; }

                // Export and File Operations
                int expY = wy + 260;
                if (Hit(mouseX, mouseY, wx + 16, expY, 130, 28)) { ExportCSharp(); return; }
                if (Hit(mouseX, mouseY, wx + 154, expY, 130, 28)) { ExportBasic(); return; }
                if (Hit(mouseX, mouseY, wx + 292, expY, 135, 28)) { SaveSpriteFile(); return; }
                if (Hit(mouseX, mouseY, wx + 434, expY, 135, 28)) { LoadSpriteFile(); return; }
            }
        }

        private static bool Hit(int mx, int my, int x, int y, int w, int h)
        {
            return mx >= x && mx < x + w && my >= y && my < y + h;
        }

        private void FloodFill(int startRow, int startCol, byte newColor)
        {
            byte targetColor = pixels[startRow, startCol];
            if (targetColor == newColor)
                return;

            int head = 0;
            int tail = 0;

            fillQueueX[tail] = startCol;
            fillQueueY[tail] = startRow;
            tail++;

            pixels[startRow, startCol] = newColor;

            while (head < tail)
            {
                int cx = fillQueueX[head];
                int cy = fillQueueY[head];
                head++;

                // 4 neighbors
                int[] dx = { -1, 1, 0, 0 };
                int[] dy = { 0, 0, -1, 1 };

                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + dx[d];
                    int ny = cy + dy[d];

                    if (nx >= 0 && nx < GridSize && ny >= 0 && ny < GridSize)
                    {
                        if (pixels[ny, nx] == targetColor)
                        {
                            pixels[ny, nx] = newColor;
                            if (tail < fillQueueX.Length)
                            {
                                fillQueueX[tail] = nx;
                                fillQueueY[tail] = ny;
                                tail++;
                            }
                        }
                    }
                }
            }
        }

        private void FlipHorizontal()
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize / 2; c++)
                {
                    byte tmp = pixels[r, c];
                    pixels[r, c] = pixels[r, GridSize - 1 - c];
                    pixels[r, GridSize - 1 - c] = tmp;
                }
            }
        }

        private void FlipVertical()
        {
            for (int c = 0; c < GridSize; c++)
            {
                for (int r = 0; r < GridSize / 2; r++)
                {
                    byte tmp = pixels[r, c];
                    pixels[r, c] = pixels[GridSize - 1 - r, c];
                    pixels[GridSize - 1 - r, c] = tmp;
                }
            }
        }

        private void InvertColors()
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    if (pixels[r, c] != 0)
                    {
                        pixels[r, c] = (byte)(15 - pixels[r, c]);
                    }
                }
            }
        }

        private void ClearCanvas()
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    pixels[r, c] = 0;
                }
            }
        }

        private void ExportCSharp()
        {
            try
            {
                using (var sw = new StreamWriter("/root/sprite.cs", false))
                {
                    sw.WriteLine("// Wygenerowano przez ZOnderqOS Sprite Studio");
                    sw.WriteLine("public static readonly byte[,] Sprite16x16 = new byte[16, 16] {");
                    for (int r = 0; r < GridSize; r++)
                    {
                        sw.Write("    { ");
                        for (int c = 0; c < GridSize; c++)
                        {
                            sw.Write(pixels[r, c].ToString());
                            if (c < GridSize - 1) sw.Write(", ");
                        }
                        sw.WriteLine(r < GridSize - 1 ? " }," : " }");
                    }
                    sw.WriteLine("};");
                }
                statusMessage = "Zapisano tablice C# w: /root/sprite.cs";
            }
            catch (Exception ex)
            {
                statusMessage = "Blad zapisu C#: " + ex.Message;
            }
        }

        private void ExportBasic()
        {
            try
            {
                using (var sw = new StreamWriter("/root/sprite.bas", false))
                {
                    sw.WriteLine("10 REM SPRITE 16X16 DLA BASIC");
                    int lineNum = 20;
                    for (int r = 0; r < GridSize; r++)
                    {
                        sw.Write(lineNum + " DATA ");
                        for (int c = 0; c < GridSize; c++)
                        {
                            sw.Write(pixels[r, c].ToString());
                            if (c < GridSize - 1) sw.Write(",");
                        }
                        sw.WriteLine();
                        lineNum += 10;
                    }
                }
                statusMessage = "Zapisano kod BASIC DATA w: /root/sprite.bas";
            }
            catch (Exception ex)
            {
                statusMessage = "Blad zapisu BASIC: " + ex.Message;
            }
        }

        private void SaveSpriteFile()
        {
            try
            {
                byte[] raw = new byte[GridSize * GridSize];
                int idx = 0;
                for (int r = 0; r < GridSize; r++)
                {
                    for (int c = 0; c < GridSize; c++)
                    {
                        raw[idx++] = pixels[r, c];
                    }
                }
                File.WriteAllBytes("/root/icon.spr", raw);
                statusMessage = "Zapisano plik binarny: /root/icon.spr (256 bajtow)";
            }
            catch (Exception ex)
            {
                statusMessage = "Blad zapisu .spr: " + ex.Message;
            }
        }

        private void LoadSpriteFile()
        {
            try
            {
                if (!File.Exists("/root/icon.spr"))
                {
                    statusMessage = "Brak pliku /root/icon.spr do wczytania.";
                    return;
                }
                byte[] raw = File.ReadAllBytes("/root/icon.spr");
                if (raw.Length >= GridSize * GridSize)
                {
                    int idx = 0;
                    for (int r = 0; r < GridSize; r++)
                    {
                        for (int c = 0; c < GridSize; c++)
                        {
                            pixels[r, c] = (byte)(raw[idx++] % 16);
                        }
                    }
                    statusMessage = "Wczytano pomyslnie: /root/icon.spr";
                }
            }
            catch (Exception ex)
            {
                statusMessage = "Blad odczytu: " + ex.Message;
            }
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || Window.IsMinimized)
                return;

            base.Render(canvas);

            int wx = Window.X;
            int wy = Window.Y + Window.HeaderHeight;
            int ww = Window.Width;
            int wh = Window.Height - Window.HeaderHeight;

            canvas.DrawFilledRectangle(Surface, wx, wy, ww, wh);

            // 1. Grid Editor (16x16)
            int gridX = wx + 16;
            int gridY = wy + 16;

            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    int px = gridX + c * CellSize;
                    int py = gridY + r * CellSize;

                    byte colIndex = pixels[r, c];
                    Color cellColor = colIndex == 0 ? ((r + c) % 2 == 0 ? Color.FromArgb(24, 28, 36) : Color.FromArgb(16, 20, 26)) : Palette[colIndex];

                    canvas.DrawFilledRectangle(cellColor, px, py, CellSize, CellSize);
                    canvas.DrawRectangle(Border, px, py, CellSize, CellSize);
                }
            }

            // Outline around the entire grid
            canvas.DrawRectangle(AccentColor, gridX - 1, gridY - 1, GridSize * CellSize + 2, GridSize * CellSize + 2);

            // 2. Color Palette (16 colors)
            int palX = wx + 260;
            int palY = wy + 16;
            SmallTextRenderer.Draw(canvas, "PALETA", palX, palY - 12, GoldColor);

            for (int i = 0; i < 16; i++)
            {
                int px = palX + (i % 2) * 26;
                int py = palY + (i / 2) * 24;

                canvas.DrawFilledRectangle(Palette[i], px, py, 22, 20);
                if (i == selectedColor)
                {
                    canvas.DrawRectangle(Color.White, px - 1, py - 1, 24, 22);
                    canvas.DrawRectangle(AccentColor, px - 2, py - 2, 26, 24);
                }
                else
                {
                    canvas.DrawRectangle(Border, px, py, 22, 20);
                }
            }

            // 3. Tools
            int toolX = wx + 330;
            int toolY = wy + 16;
            int toolW = 100;
            int toolH = 24;

            SmallTextRenderer.Draw(canvas, "NARZEDZIA", toolX, toolY - 12, AccentColor);
            DrawToolBtn(canvas, "✏ Olowek", toolX, toolY, toolW, toolH, currentTool == 0);
            DrawToolBtn(canvas, "⌧ Gumka", toolX, toolY + 28, toolW, toolH, currentTool == 1);
            DrawToolBtn(canvas, "▼ Wypelnij", toolX, toolY + 56, toolW, toolH, currentTool == 2);

            // 4. Transform Actions
            int actX = wx + 440;
            int actY = wy + 16;
            int actW = 125;
            SmallTextRenderer.Draw(canvas, "OPERACJE", actX, actY - 12, GreenColor);
            DrawToolBtn(canvas, "⇄ Odbij Poz.", actX, actY, actW, toolH, false);
            DrawToolBtn(canvas, "⇅ Odbij Pion.", actX, actY + 28, actW, toolH, false);
            DrawToolBtn(canvas, "◐ Inwertuj", actX, actY + 56, actW, toolH, false);
            DrawToolBtn(canvas, "🗑 Wyczysc", actX, actY + 84, actW, toolH, false);

            // 5. Live Sprite Previews (1x, 2x, 3x)
            int prevX = wx + 330;
            int prevY = wy + 130;
            UiPainter.RoundedPanel(canvas, prevX, prevY, 235, 95, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "PODGLAD NA ZYWO (1X / 2X / 3X):", prevX + 10, prevY + 10, GoldColor);

            // 1x Preview (16x16)
            int p1X = prevX + 12;
            int p1Y = prevY + 30;
            DrawPreview(canvas, p1X, p1Y, 1);

            // 2x Preview (32x32)
            int p2X = prevX + 40;
            int p2Y = prevY + 30;
            DrawPreview(canvas, p2X, p2Y, 2);

            // 3x Preview (48x48)
            int p3X = prevX + 85;
            int p3Y = prevY + 30;
            DrawPreview(canvas, p3X, p3Y, 3);

            // 6. Action and Export Buttons
            int expY = wy + 260;
            DrawToolBtn(canvas, "💾 Eksport C#", wx + 16, expY, 130, 28, false);
            DrawToolBtn(canvas, "📜 Eksport BASIC", wx + 154, expY, 130, 28, false);
            DrawToolBtn(canvas, "⬆ Zapisz .SPR", wx + 292, expY, 135, 28, false);
            DrawToolBtn(canvas, "⬇ Wczytaj .SPR", wx + 434, expY, 135, 28, false);

            // 7. Status Footer
            int footerY = wy + 300;
            UiPainter.RoundedPanel(canvas, wx + 16, footerY, ww - 32, 40, 4, Panel, Border);
            SmallTextRenderer.Draw(canvas, "STATUS:", wx + 24, footerY + 8, AccentColor);
            SmallTextRenderer.Draw(canvas, statusMessage, wx + 24, footerY + 22, TextColor);
        }

        private void DrawToolBtn(Canvas canvas, string text, int x, int y, int w, int h, bool active)
        {
            Color fill = active ? PanelHover : Panel;
            Color bdr = active ? AccentColor : Border;
            UiPainter.RoundedPanel(canvas, x, y, w, h, 3, fill, bdr);
            SmallTextRenderer.DrawCentered(canvas, text, x, y + (h / 2) - 4, w, active ? AccentColor : TextColor);
        }

        private void DrawPreview(Canvas canvas, int ox, int oy, int scale)
        {
            for (int r = 0; r < GridSize; r++)
            {
                for (int c = 0; c < GridSize; c++)
                {
                    byte colIndex = pixels[r, c];
                    if (colIndex != 0)
                    {
                        canvas.DrawFilledRectangle(Palette[colIndex], ox + c * scale, oy + r * scale, scale, scale);
                    }
                }
            }
            canvas.DrawRectangle(Border, ox - 1, oy - 1, GridSize * scale + 2, GridSize * scale + 2);
        }
    }
}
