using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using Cosmos.Kernel.System.Keyboard;
using ZonderqOS.GUI.Icons;
using CosmosBitmap = Cosmos.Kernel.System.Graphics.Bitmap;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Native PNG/BMP viewer for ZOnderqOS. The source image is decoded only when a
    /// file is opened/reloaded. A scaled preview Bitmap is rebuilt only after an
    /// explicit zoom/rotate/navigation action or after window resizing has finished.
    /// Stable rendering uses the unscaled DrawImage overload, so leaving this window
    /// open does not create a new scaled pixel buffer every GUI frame.
    /// </summary>
    public sealed class ImageViewerApp : Application
    {
        private static readonly Color Surface = Color.FromArgb(19, 24, 30);
        private static readonly Color Toolbar = Color.FromArgb(27, 34, 41);
        private static readonly Color ToolbarHover = Color.FromArgb(39, 52, 64);
        private static readonly Color Border = Color.FromArgb(56, 69, 81);
        private static readonly Color Text = Color.FromArgb(229, 235, 240);
        private static readonly Color Muted = Color.FromArgb(132, 149, 164);
        private static readonly Color ViewBackground = Color.FromArgb(14, 18, 23);

        private const int ToolbarHeight = 48;
        private const int FooterHeight = 30;
        private const int MaxSiblingImages = 128;
        private const int MaxPreviewWidth = 1600;
        private const int MaxPreviewHeight = 900;

        private int[] sourcePixels;
        private int sourceWidth;
        private int sourceHeight;
        private CosmosBitmap previewBitmap;
        private int previewWidth;
        private int previewHeight;
        private int preparedAreaWidth = -1;
        private int preparedAreaHeight = -1;
        private bool previewDirty = true;
        private bool leftButtonDown;

        private string currentPath = string.Empty;
        private string displayName = "BRAK OBRAZU";
        private string status = "Otworz plik PNG lub BMP z File Managera";
        private readonly string[] siblingImages = new string[MaxSiblingImages];
        private int siblingCount;
        private int siblingIndex = -1;

        // Rotation: 0, 90, 180, 270 degrees clockwise.
        private int rotation;
        private int zoomPercent = 100;
        private bool fitToWindow = true;

        public ImageViewerApp(int x, int y, string path, Action onClose) : base("Zdjecia")
        {
            Window = new Window(x, y, 980, 650, "Zdjecia - ZOnderqOS");
            Window.CloseAction = Close;

            if (!string.IsNullOrEmpty(path))
                LoadImage(path, true);
        }

        public override void HandleKeyboard(KeyEvent key)
        {
            if (key == null)
                return;

            if (key.Key == ConsoleKeyEx.Escape)
            {
                Close();
                return;
            }

            if (key.Key == ConsoleKeyEx.LeftArrow)
            {
                NavigateSibling(-1);
                return;
            }

            if (key.Key == ConsoleKeyEx.RightArrow)
            {
                NavigateSibling(1);
                return;
            }

            if (key.Key == ConsoleKeyEx.F5)
            {
                ReloadImage();
                return;
            }

            char ch = key.KeyChar;
            if (ch == '+' || ch == '=')
            {
                ChangeZoom(25);
                return;
            }
            if (ch == '-' || ch == '_')
            {
                ChangeZoom(-25);
                return;
            }
            if (ch == 'f' || ch == 'F')
            {
                fitToWindow = true;
                zoomPercent = 100;
                previewDirty = true;
                status = "Dopasowanie do okna";
                return;
            }
            if (ch == 'r' || ch == 'R')
            {
                RotateClockwise();
            }
        }

        public override void HandleMouse(int mouseX, int mouseY, bool left, bool oldLeft)
        {
            Window.HandleMouse(mouseX, mouseY, left, oldLeft);
            leftButtonDown = left;

            if (!Window.Visible || Window.IsMinimized)
                return;

            if (!left && oldLeft)
            {
                int areaWidth = GetViewportWidth() - 16;
                int areaHeight = GetViewportHeight() - 16;
                if (areaWidth != preparedAreaWidth || areaHeight != preparedAreaHeight)
                    previewDirty = true;
            }

            if (!left || oldLeft)
                return;

            int toolbarY = Window.Y + 38;
            if (mouseY < toolbarY || mouseY >= toolbarY + ToolbarHeight)
                return;

            int x = mouseX - (Window.X + 14);
            if (x < 0)
                return;

            if (x < 38)
                NavigateSibling(-1);
            else if (x < 78)
                NavigateSibling(1);
            else if (x < 126)
                ChangeZoom(-25);
            else if (x < 174)
                ChangeZoom(25);
            else if (x < 222)
                RotateClockwise();
            else if (x < 270)
            {
                fitToWindow = true;
                zoomPercent = 100;
                previewDirty = true;
                status = "Dopasowanie do okna";
            }
            else if (x < 318)
                ReloadImage();
        }

        public override void Render(Canvas canvas)
        {
            if (!Window.Visible || !IsRunning)
                return;

            base.Render(canvas);
            if (Window.IsMinimized)
                return;

            RenderToolbar(canvas);
            RenderViewport(canvas);
            RenderFooter(canvas);
        }

        private void RenderToolbar(Canvas canvas)
        {
            int x = Window.X + 10;
            int y = Window.Y + 38;
            int width = Window.Width - 20;

            canvas.DrawFilledRectangle(Toolbar, x, y, width, ToolbarHeight);
            canvas.DrawRectangle(Border, x, y, width, ToolbarHeight);

            DrawToolbarButton(canvas, x + 4, y + 5, 36, IconType.ArrowUp, "<");
            DrawToolbarButton(canvas, x + 44, y + 5, 36, IconType.ArrowUp, ">");
            DrawToolbarButton(canvas, x + 84, y + 5, 44, IconType.ZoomOut, null);
            DrawToolbarButton(canvas, x + 132, y + 5, 44, IconType.ZoomIn, null);
            DrawToolbarButton(canvas, x + 180, y + 5, 44, IconType.Rotate, null);
            DrawToolbarButton(canvas, x + 228, y + 5, 44, IconType.ImageViewer, null);
            DrawToolbarButton(canvas, x + 276, y + 5, 44, IconType.Refresh, null);

            int infoX = x + 334;
            if (infoX < x + width - 120)
            {
                SmallTextRenderer.DrawClipped(canvas, displayName, infoX, y + 10,
                    System.Math.Max(20, x + width - infoX - 92), Text);
                SmallTextRenderer.DrawUIntWithSuffix(canvas, (ulong)zoomPercent, "%", infoX, y + 28, Muted);
                if (fitToWindow)
                    SmallTextRenderer.Draw(canvas, " FIT", infoX + SmallTextRenderer.WidthUInt((ulong)zoomPercent) + 10,
                        y + 28, SystemTheme.Accent);
            }
        }

        private static void DrawToolbarButton(Canvas canvas, int x, int y, int width, IconType icon, string text)
        {
            canvas.DrawFilledRectangle(ToolbarHover, x, y, width, 38);
            canvas.DrawRectangle(Border, x, y, width, 38);

            if (!string.IsNullOrEmpty(text))
            {
                SmallTextRenderer.DrawCentered(canvas, text, x, y + 16, width, Text);
                return;
            }

            IconManager.DrawScaled(canvas, icon, x + (width - 20) / 2, y + 9, 20, 20);
        }

        private void RenderViewport(Canvas canvas)
        {
            int x = Window.X + 10;
            int y = Window.Y + 92;
            int width = GetViewportWidth();
            int height = GetViewportHeight();

            canvas.DrawFilledRectangle(ViewBackground, x, y, width, height);
            canvas.DrawRectangle(Border, x, y, width, height);

            if (sourcePixels == null || sourceWidth <= 0 || sourceHeight <= 0)
            {
                IconManager.DrawScaled(canvas, IconType.ImageViewer,
                    x + (width - 56) / 2, y + System.Math.Max(22, height / 2 - 64), 56, 56);
                SmallTextRenderer.DrawCentered(canvas, "BRAK OTWARTEGO OBRAZU", x + 20,
                    y + height / 2 + 4, System.Math.Max(40, width - 40), Text);
                SmallTextRenderer.DrawCentered(canvas, "Otworz PNG/BMP dwuklikiem w File Managerze", x + 20,
                    y + height / 2 + 24, System.Math.Max(40, width - 40), Muted);
                return;
            }

            int areaWidth = System.Math.Max(1, width - 16);
            int areaHeight = System.Math.Max(1, height - 16);

            // During live window resize keep the existing preview. The bitmap is
            // rebuilt once after button release instead of allocating at every size.
            if (!leftButtonDown &&
                (previewDirty || previewBitmap == null ||
                 (fitToWindow && (preparedAreaWidth != areaWidth || preparedAreaHeight != areaHeight))))
            {
                PreparePreview(areaWidth, areaHeight);
            }

            if (previewBitmap == null || previewWidth <= 0 || previewHeight <= 0)
                return;

            int drawX = x + (width - previewWidth) / 2;
            int drawY = y + (height - previewHeight) / 2;
            canvas.DrawImage(previewBitmap, drawX, drawY);
        }

        private void RenderFooter(Canvas canvas)
        {
            int x = Window.X + 10;
            int y = Window.Y + Window.Height - FooterHeight - 6;
            int width = Window.Width - 20;
            canvas.DrawFilledRectangle(Toolbar, x, y, width, FooterHeight);
            canvas.DrawLine(Border, x, y, x + width, y);
            SmallTextRenderer.DrawClipped(canvas, status, x + 8, y + 11,
                System.Math.Max(20, width - 16), Muted);
        }

        private int GetViewportWidth()
        {
            return System.Math.Max(160, Window.Width - 20);
        }

        private int GetViewportHeight()
        {
            return System.Math.Max(120, Window.Height - 132);
        }

        private void ChangeZoom(int delta)
        {
            if (sourcePixels == null)
                return;

            fitToWindow = false;
            zoomPercent = System.Math.Max(25, System.Math.Min(300, zoomPercent + delta));
            previewDirty = true;
            status = "Zoom zmieniony";
        }

        private void RotateClockwise()
        {
            if (sourcePixels == null)
                return;

            rotation = (rotation + 1) & 3;
            previewDirty = true;
            status = "Obrot 90 stopni";
        }

        private void ReloadImage()
        {
            if (string.IsNullOrEmpty(currentPath))
            {
                status = "Brak pliku do odswiezenia";
                return;
            }

            LoadImage(currentPath, false);
        }

        private void NavigateSibling(int delta)
        {
            if (siblingCount <= 0 || siblingIndex < 0)
            {
                status = "Brak sasiedniego obrazu";
                return;
            }

            int next = siblingIndex + delta;
            if (next < 0)
                next = siblingCount - 1;
            else if (next >= siblingCount)
                next = 0;

            LoadImage(siblingImages[next], false);
        }

        private void LoadImage(string path, bool rebuildSiblings)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                status = "Nie znaleziono pliku obrazu";
                return;
            }

            try
            {
                int[] pixels;
                int width;
                int height;

                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    Png image = new Png(path);
                    width = (int)image.Width;
                    height = (int)image.Height;
                    pixels = image.RawData;
                }
                else if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    CosmosBitmap image = new CosmosBitmap(path);
                    width = (int)image.Width;
                    height = (int)image.Height;
                    pixels = image.RawData;
                }
                else
                {
                    status = "Obslugiwane formaty: PNG i BMP";
                    return;
                }

                if (width <= 0 || height <= 0 || pixels == null || pixels.Length < width * height)
                {
                    status = "Nieprawidlowy obraz";
                    return;
                }

                sourcePixels = pixels;
                sourceWidth = width;
                sourceHeight = height;
                currentPath = path;
                displayName = Path.GetFileName(path);
                if (string.IsNullOrEmpty(displayName))
                    displayName = path;
                Window.Title = "Zdjecia - " + displayName;

                rotation = 0;
                zoomPercent = 100;
                fitToWindow = true;
                previewDirty = true;
                preparedAreaWidth = -1;
                preparedAreaHeight = -1;
                status = "Obraz zaladowany";

                if (rebuildSiblings || siblingCount == 0 || !IsCurrentDirectoryInSiblingList())
                    RebuildSiblingList(path);
                else
                    FindSiblingIndex(path);
            }
            catch (Exception ex)
            {
                sourcePixels = null;
                sourceWidth = 0;
                sourceHeight = 0;
                previewBitmap = null;
                previewWidth = 0;
                previewHeight = 0;
                status = "Blad obrazu: " + ex.Message;
            }
        }

        private bool IsCurrentDirectoryInSiblingList()
        {
            if (siblingCount <= 0 || string.IsNullOrEmpty(currentPath))
                return false;

            string currentDirectory = Path.GetDirectoryName(currentPath) ?? string.Empty;
            string siblingDirectory = Path.GetDirectoryName(siblingImages[0]) ?? string.Empty;
            return string.Equals(currentDirectory, siblingDirectory, StringComparison.Ordinal);
        }

        private void RebuildSiblingList(string path)
        {
            for (int i = 0; i < siblingImages.Length; i++)
                siblingImages[i] = null;
            siblingCount = 0;
            siblingIndex = -1;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                    return;

                string[] files = Directory.GetFiles(directory);
                for (int i = 0; i < files.Length && siblingCount < MaxSiblingImages; i++)
                {
                    string file = files[i];
                    if (!IsImagePath(file))
                        continue;
                    siblingImages[siblingCount++] = file;
                }

                FindSiblingIndex(path);
            }
            catch
            {
                siblingCount = 0;
                siblingIndex = -1;
            }
        }

        private void FindSiblingIndex(string path)
        {
            siblingIndex = -1;
            for (int i = 0; i < siblingCount; i++)
            {
                if (string.Equals(siblingImages[i], path, StringComparison.Ordinal))
                {
                    siblingIndex = i;
                    return;
                }
            }
        }

        private static bool IsImagePath(string path)
        {
            return !string.IsNullOrEmpty(path) &&
                   (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase));
        }

        private void PreparePreview(int areaWidth, int areaHeight)
        {
            if (sourcePixels == null || sourceWidth <= 0 || sourceHeight <= 0)
                return;

            areaWidth = System.Math.Max(1, areaWidth);
            areaHeight = System.Math.Max(1, areaHeight);

            int rotatedWidth = (rotation & 1) == 0 ? sourceWidth : sourceHeight;
            int rotatedHeight = (rotation & 1) == 0 ? sourceHeight : sourceWidth;

            int fitWidth;
            int fitHeight;
            if ((long)rotatedWidth * areaHeight > (long)rotatedHeight * areaWidth)
            {
                fitWidth = areaWidth;
                fitHeight = System.Math.Max(1, (int)((long)rotatedHeight * areaWidth / rotatedWidth));
            }
            else
            {
                fitHeight = areaHeight;
                fitWidth = System.Math.Max(1, (int)((long)rotatedWidth * areaHeight / rotatedHeight));
            }

            int targetWidth = fitToWindow
                ? fitWidth
                : System.Math.Max(1, (int)((long)fitWidth * zoomPercent / 100L));
            int targetHeight = fitToWindow
                ? fitHeight
                : System.Math.Max(1, (int)((long)fitHeight * zoomPercent / 100L));

            if (targetWidth > MaxPreviewWidth || targetHeight > MaxPreviewHeight)
            {
                if ((long)targetWidth * MaxPreviewHeight > (long)targetHeight * MaxPreviewWidth)
                {
                    targetHeight = System.Math.Max(1, (int)((long)targetHeight * MaxPreviewWidth / targetWidth));
                    targetWidth = MaxPreviewWidth;
                }
                else
                {
                    targetWidth = System.Math.Max(1, (int)((long)targetWidth * MaxPreviewHeight / targetHeight));
                    targetHeight = MaxPreviewHeight;
                }
            }

            CosmosBitmap preview = new CosmosBitmap((uint)targetWidth, (uint)targetHeight, ColorDepth.ColorDepth32);
            int[] destination = preview.RawData;
            if (destination == null || destination.Length < targetWidth * targetHeight)
                return;

            int background = ViewBackground.ToArgb();
            for (int dy = 0; dy < targetHeight; dy++)
            {
                int ry = (int)((long)dy * rotatedHeight / targetHeight);
                if (ry >= rotatedHeight) ry = rotatedHeight - 1;
                int row = dy * targetWidth;

                for (int dx = 0; dx < targetWidth; dx++)
                {
                    int rx = (int)((long)dx * rotatedWidth / targetWidth);
                    if (rx >= rotatedWidth) rx = rotatedWidth - 1;

                    int sx;
                    int sy;
                    if (rotation == 0)
                    {
                        sx = rx;
                        sy = ry;
                    }
                    else if (rotation == 1)
                    {
                        sx = ry;
                        sy = sourceHeight - 1 - rx;
                    }
                    else if (rotation == 2)
                    {
                        sx = sourceWidth - 1 - rx;
                        sy = sourceHeight - 1 - ry;
                    }
                    else
                    {
                        sx = sourceWidth - 1 - ry;
                        sy = rx;
                    }

                    int argb = sourcePixels[sy * sourceWidth + sx];
                    destination[row + dx] = CompositeOnBackground(argb, background);
                }
            }

            previewBitmap = preview;
            previewWidth = targetWidth;
            previewHeight = targetHeight;
            preparedAreaWidth = areaWidth;
            preparedAreaHeight = areaHeight;
            previewDirty = false;
        }

        private static int CompositeOnBackground(int foreground, int background)
        {
            int alpha = (int)((uint)foreground >> 24);
            if (alpha >= 255)
                return foreground;
            if (alpha <= 0)
                return background;

            int inverse = 255 - alpha;
            int fr = (foreground >> 16) & 255;
            int fg = (foreground >> 8) & 255;
            int fb = foreground & 255;
            int br = (background >> 16) & 255;
            int bg = (background >> 8) & 255;
            int bb = background & 255;

            int r = (fr * alpha + br * inverse) / 255;
            int g = (fg * alpha + bg * inverse) / 255;
            int b = (fb * alpha + bb * inverse) / 255;
            return unchecked((int)0xFF000000) | (r << 16) | (g << 8) | b;
        }
    }
}
