using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
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
        // The seven actions share geometry for painting and hit testing.
        private static readonly IconType[] ToolbarIcons =
        {
            IconType.ArrowUp, IconType.ArrowUp, IconType.ZoomOut,
            IconType.ZoomIn, IconType.Rotate, IconType.ImageViewer,
            IconType.Refresh
        };
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
            int localX = mouseX - (Window.X + 10);
            int localY = mouseY - toolbarY;
            if (localX < 0 || localX >= Window.Width - 20 ||
                localY < 5 || localY >= 43)
                return;

            for (int i = 0; i < ToolbarIcons.Length; i++)
            {
                int buttonX;
                int buttonWidth;
                if (!TryToolbarButtonBounds(i, out buttonX, out buttonWidth))
                    break;
                if (localX < buttonX || localX >= buttonX + buttonWidth)
                    continue;

                switch (i)
                {
                    case 0: NavigateSibling(-1); break;
                    case 1: NavigateSibling(1); break;
                    case 2: ChangeZoom(-25); break;
                    case 3: ChangeZoom(25); break;
                    case 4: RotateClockwise(); break;
                    case 5:
                        fitToWindow = true;
                        zoomPercent = 100;
                        previewDirty = true;
                        status = "Dopasowanie do okna";
                        break;
                    case 6: ReloadImage(); break;
                }
                return;
            }
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

            for (int i = 0; i < ToolbarIcons.Length; i++)
            {
                int buttonX;
                int buttonWidth;
                if (!TryToolbarButtonBounds(i, out buttonX, out buttonWidth))
                    break;
                string label = i == 0 ? "<" : (i == 1 ? ">" : null);
                DrawToolbarButton(canvas, x + buttonX, y + 5, buttonWidth, ToolbarIcons[i], label);
            }

            int lastX;
            int lastWidth;
            if (TryToolbarButtonBounds(6, out lastX, out lastWidth))
            {
                int infoX = x + lastX + lastWidth + 14;
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
        }

        private bool TryToolbarButtonBounds(int index, out int left, out int width)
        {
            left = 4;
            width = 0;
            if (index < 0 || index >= ToolbarIcons.Length)
                return false;

            int toolbarWidth = Window.Width - 20;
            bool compact = toolbarWidth < 324;
            for (int i = 0; i < index; i++)
                left += (compact ? 32 : (i < 2 ? 36 : 44)) + 4;

            width = compact ? 32 : (index < 2 ? 36 : 44);
            return left + width <= toolbarWidth - 4;
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
                if (width >= 100 && height >= 180)
                {
                    IconManager.DrawScaled(canvas, IconType.ImageViewer,
                        x + (width - 56) / 2, y + height / 2 - 72, 56, 56);
                    SmallTextRenderer.DrawCentered(canvas, "BRAK OTWARTEGO OBRAZU", x + 8,
                        y + height / 2 + 4, width - 16, Text);
                    SmallTextRenderer.DrawCentered(canvas, "Otworz PNG/BMP dwuklikiem w File Managerze", x + 8,
                        y + height / 2 + 24, width - 16, Muted);
                }
                else if (width >= 40 && height >= 20)
                {
                    SmallTextRenderer.DrawClipped(canvas, "BRAK OTWARTEGO OBRAZU",
                        x + 6, y + (height - 12) / 2, width - 12, Text);
                }
                return;
            }

            int areaWidth = System.Math.Max(1, width - 16);
            int areaHeight = System.Math.Max(1, height - 16);

            // During live window resize keep the existing preview. The bitmap is
            // rebuilt once after button release instead of allocating at every size.
            if ((!leftButtonDown || previewBitmap == null) &&
                (previewDirty || previewBitmap == null ||
                 preparedAreaWidth != areaWidth || preparedAreaHeight != areaHeight))
            {
                PreparePreview(areaWidth, areaHeight);
            }

            // A previously prepared bitmap may be larger than the viewport
            // while a window is being dragged smaller. Never paint it over
            // the footer, taskbar or another window before mouse release.
            if (previewBitmap == null || previewWidth <= 0 || previewHeight <= 0 ||
                previewWidth > areaWidth || previewHeight > areaHeight)
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
            return System.Math.Max(1, Window.Width - 20);
        }

        private int GetViewportHeight()
        {
            // Never let the preview overlap the footer on short desktops.
            return System.Math.Max(1, Window.Height - 132);
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
            if (string.IsNullOrEmpty(path))
            {
                status = "Nie znaleziono pliku obrazu";
                return;
            }

            try
            {
                // Even the existence check can fail on a removed FAT32 volume.
                // Keep the previously decoded image when opening a bad sibling.
                if (!File.Exists(path))
                {
                    status = "Nie znaleziono pliku obrazu";
                    return;
                }

                int[] pixels;
                int width;
                int height;

                if (path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    Png image = new Png(path);
                    width = image.Width;
                    height = image.Height;
                    pixels = image.RawData;
                }
                else if (path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
                {
                    CosmosBitmap image = new CosmosBitmap(path);
                    width = image.Width;
                    height = image.Height;
                    pixels = image.RawData;
                }
                else
                {
                    status = "Obslugiwane formaty: PNG i BMP";
                    return;
                }

                if (width <= 0 || height <= 0 || pixels == null ||
                    (long)width * height > pixels.Length)
                {
                    status = "Nieprawidlowy obraz";
                    return;
                }

                // Finish all potentially throwing path operations before
                // committing the new image. A failed decode/navigation must
                // not blank a previously valid image or corrupt its title.
                string name = Path.GetFileName(path);
                if (string.IsNullOrEmpty(name))
                    name = path;

                sourcePixels = pixels;
                sourceWidth = width;
                sourceHeight = height;
                currentPath = path;
                displayName = name;
                Window.Title = "Zdjecia - " + displayName;

                // A mouse-held toolbar navigation must never display pixels
                // from the previous file while the new preview is pending.
                previewBitmap = null;
                previewWidth = 0;
                previewHeight = 0;
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
                // The current image remains usable after a corrupt file,
                // permission error or transient FAT32 read failure.
                status = "Blad obrazu: " + ex.Message;
            }
        }

        private bool IsCurrentDirectoryInSiblingList()
        {
            if (siblingCount <= 0 || string.IsNullOrEmpty(currentPath))
                return false;

            string currentDirectory = Path.GetDirectoryName(currentPath) ?? string.Empty;
            string siblingDirectory = Path.GetDirectoryName(siblingImages[0]) ?? string.Empty;
            // FAT32 paths are case-insensitive, including their directory names.
            return string.Equals(currentDirectory, siblingDirectory, StringComparison.OrdinalIgnoreCase);
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
                bool currentFound = false;
                for (int i = 0; i < files.Length && siblingCount < MaxSiblingImages; i++)
                {
                    string file = files[i];
                    if (!IsImagePath(file))
                        continue;
                    if (string.Equals(file, path, StringComparison.OrdinalIgnoreCase))
                        currentFound = true;
                    siblingImages[siblingCount++] = file;
                }

                // The directory may contain thousands of images. The capped
                // sibling list must still include the image actually opened,
                // otherwise Left/Right navigation is silently disabled.
                if (!currentFound && IsImagePath(path))
                {
                    if (siblingCount == MaxSiblingImages)
                        siblingImages[siblingCount - 1] = path;
                    else
                        siblingImages[siblingCount++] = path;
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
                if (string.Equals(siblingImages[i], path, StringComparison.OrdinalIgnoreCase))
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

            // A zoomed image can exceed the viewport. Draw a centered crop
            // instead of letting Canvas.DrawImage overwrite other windows or
            // the footer. Only allocate the visible pixel rectangle.
            int visibleWidth = System.Math.Min(targetWidth, areaWidth);
            int visibleHeight = System.Math.Min(targetHeight, areaHeight);
            int cropX = (targetWidth - visibleWidth) / 2;
            int cropY = (targetHeight - visibleHeight) / 2;

            CosmosBitmap preview = new CosmosBitmap(visibleWidth, visibleHeight, ColorDepth.ColorDepth32);
            int[] destination = preview.RawData;
            if (destination == null || (long)destination.Length < (long)visibleWidth * visibleHeight)
                return;

            int background = ViewBackground.ToArgb();
            for (int dy = 0; dy < visibleHeight; dy++)
            {
                int ry = (int)((long)(dy + cropY) * rotatedHeight / targetHeight);
                if (ry >= rotatedHeight) ry = rotatedHeight - 1;
                int row = dy * visibleWidth;

                for (int dx = 0; dx < visibleWidth; dx++)
                {
                    int rx = (int)((long)(dx + cropX) * rotatedWidth / targetWidth);
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
            previewWidth = visibleWidth;
            previewHeight = visibleHeight;
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
