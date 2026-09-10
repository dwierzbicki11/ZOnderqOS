using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;
        private const int LargeIconSize = 40;
        private const int IconCount = (int)IconType.Play + 1;
        private const int MaxScaledCacheEntries = 256;
        private const string CacheDirectory = "/root/.zonderq-icons";

        // Source PNGs are decoded once during GUI startup and held strongly for the
        // whole GUI session. Raw pixel buffers are used by the hot render path.
        private static readonly Png[] sourceImages = new Png[IconCount];
        private static readonly int[][] sourcePixels = new int[IconCount][];
        private static readonly int[] sourceWidths = new int[IconCount];
        private static readonly int[] sourceHeights = new int[IconCount];
        private static readonly bool[] sourceLoadAttempted = new bool[IconCount];

        // Every requested icon/size pair is scaled once. Later frames only reuse the
        // same persistent int[]; Canvas.DrawImage(image, x, y, w, h) is never used.
        private static readonly int[][] scaledPixels = new int[MaxScaledCacheEntries][];
        private static readonly IconType[] scaledTypes = new IconType[MaxScaledCacheEntries];
        private static readonly int[] scaledWidths = new int[MaxScaledCacheEntries];
        private static readonly int[] scaledHeights = new int[MaxScaledCacheEntries];
        private static int scaledCacheCount;

        public static void Preload()
        {
            try
            {
                Directory.CreateDirectory(CacheDirectory);
            }
            catch
            {
            }

            for (int i = 0; i < IconCount; i++)
                EnsureSource((IconType)i);
        }

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            DrawScaled(canvas, type, x, y, IconSize, IconSize);
        }

        /// <summary>
        /// Allocation-free render path. The source PNG is decoded once and every
        /// requested size is cached after its first use.
        /// </summary>
        public static void DrawScaled(Canvas canvas, IconType type, int x, int y, int width, int height)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;

            type = CanonicalizeType(type);
            if (!EnsureSource(type))
                return;

            int sourceIndex = (int)type;
            int sourceWidth = sourceWidths[sourceIndex];
            int sourceHeight = sourceHeights[sourceIndex];
            int[] source = sourcePixels[sourceIndex];

            if (source == null || sourceWidth <= 0 || sourceHeight <= 0)
                return;

            if (sourceWidth == width && sourceHeight == height)
            {
                Blit(canvas, source, width, height, x, y);
                return;
            }

            int[] pixels = GetScaledPixels(type, source, sourceWidth, sourceHeight, width, height);
            if (pixels != null)
            {
                Blit(canvas, pixels, width, height, x, y);
                return;
            }

            // Fixed cache exhausted: keep the icon visible with direct scaling, still
            // without allocating a temporary frame buffer.
            BlitScaledDirect(canvas, source, sourceWidth, sourceHeight, x, y, width, height);
        }

        private static bool EnsureSource(IconType type)
        {
            type = CanonicalizeType(type);
            int index = (int)type;
            if (index < 0 || index >= IconCount)
                return false;

            if (sourcePixels[index] != null)
                return true;

            if (sourceLoadAttempted[index])
                return false;

            sourceLoadAttempted[index] = true;

            try
            {
                byte[] data = GetResource(type);
                if (data == null || data.Length == 0)
                    return false;

                // Path-based PNG decoding is the proven Cosmos Gen3 path used by the
                // original GUI. Embedded bytes are written once; normal rendering does
                // not perform file IO or decode/scale allocations.
                Directory.CreateDirectory(CacheDirectory);
                string path = Path.Combine(CacheDirectory, GetFileName(type));
                if (!File.Exists(path))
                    File.WriteAllBytes(path, data);

                Png decoded = new Png(path);
                int width = (int)decoded.Width;
                int height = (int)decoded.Height;
                int[] pixels = decoded.RawData;

                if (width <= 0 || height <= 0 || pixels == null || pixels.Length < width * height)
                    return false;

                sourceImages[index] = decoded;
                sourceWidths[index] = width;
                sourceHeights[index] = height;
                sourcePixels[index] = pixels;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int[] GetScaledPixels(IconType type, int[] source, int sourceWidth, int sourceHeight,
            int width, int height)
        {
            for (int i = 0; i < scaledCacheCount; i++)
            {
                if (scaledTypes[i] == type && scaledWidths[i] == width && scaledHeights[i] == height)
                    return scaledPixels[i];
            }

            if (scaledCacheCount >= MaxScaledCacheEntries)
                return null;

            int[] destination;
            try
            {
                destination = new int[width * height];
            }
            catch
            {
                return null;
            }

            int xRatio = ((sourceWidth << 16) / width) + 1;
            int yRatio = ((sourceHeight << 16) / height) + 1;

            for (int dy = 0; dy < height; dy++)
            {
                int sy = (dy * yRatio) >> 16;
                if (sy >= sourceHeight) sy = sourceHeight - 1;
                int sourceRow = sy * sourceWidth;
                int destinationRow = dy * width;

                for (int dx = 0; dx < width; dx++)
                {
                    int sx = (dx * xRatio) >> 16;
                    if (sx >= sourceWidth) sx = sourceWidth - 1;
                    destination[destinationRow + dx] = source[sourceRow + sx];
                }
            }

            int slot = scaledCacheCount;
            scaledTypes[slot] = type;
            scaledWidths[slot] = width;
            scaledHeights[slot] = height;
            scaledPixels[slot] = destination;
            scaledCacheCount = slot + 1;
            return destination;
        }

        private static void Blit(Canvas canvas, int[] pixels, int width, int height, int x, int y)
        {
            int startX = x < 0 ? -x : 0;
            int startY = y < 0 ? -y : 0;
            int endX = width;
            int endY = height;

            if (x + endX > canvas.Width) endX = canvas.Width - x;
            if (y + endY > canvas.Height) endY = canvas.Height - y;
            if (startX >= endX || startY >= endY)
                return;

            for (int py = startY; py < endY; py++)
            {
                int row = py * width;
                int destinationY = y + py;
                for (int px = startX; px < endX; px++)
                {
                    int argb = pixels[row + px];
                    if (((uint)argb >> 24) == 0)
                        continue;

                    canvas.DrawPoint(Color.FromArgb(argb), x + px, destinationY);
                }
            }
        }

        private static void BlitScaledDirect(Canvas canvas, int[] source, int sourceWidth, int sourceHeight,
            int x, int y, int width, int height)
        {
            int startX = x < 0 ? -x : 0;
            int startY = y < 0 ? -y : 0;
            int endX = width;
            int endY = height;

            if (x + endX > canvas.Width) endX = canvas.Width - x;
            if (y + endY > canvas.Height) endY = canvas.Height - y;
            if (startX >= endX || startY >= endY)
                return;

            for (int dy = startY; dy < endY; dy++)
            {
                int sy = (dy * sourceHeight) / height;
                int sourceRow = sy * sourceWidth;
                int destinationY = y + dy;

                for (int dx = startX; dx < endX; dx++)
                {
                    int sx = (dx * sourceWidth) / width;
                    int argb = source[sourceRow + sx];
                    if (((uint)argb >> 24) == 0)
                        continue;

                    canvas.DrawPoint(Color.FromArgb(argb), x + dx, destinationY);
                }
            }
        }

        private static IconType CanonicalizeType(IconType type)
        {
            return type == IconType.FileManager ? IconType.Folder : type;
        }

        private static byte[] GetResource(IconType type)
        {
            switch (type)
            {
                case IconType.Terminal: return IconResources.Terminal;
                case IconType.FileManager:
                case IconType.Folder: return IconResources.Folder;
                case IconType.File: return IconResources.File;
                case IconType.Settings: return IconResources.Settings;
                case IconType.About: return IconResources.About;
                case IconType.Close: return IconResources.Close;
                case IconType.Maximize: return IconResources.Maximize;
                case IconType.Restore: return IconResources.Restore;
                case IconType.Start: return IconResources.Start;
                case IconType.ArrowUp: return IconResources.ArrowUp;
                case IconType.Refresh: return IconResources.Refresh;
                case IconType.Search: return IconResources.Search;
                case IconType.Trash: return IconResources.Trash;
                case IconType.Reboot: return IconResources.Reboot;
                case IconType.Shutdown: return IconResources.Shutdown;
                case IconType.ImageViewer: return IconResources.ImageViewer;
                case IconType.Calculator: return IconResources.Calculator;
                case IconType.Paint: return IconResources.Paint;
                case IconType.Network: return IconResources.Network;
                case IconType.DiskManager: return IconResources.DiskManager;
                case IconType.HexViewer: return IconResources.HexViewer;
                case IconType.Calendar: return IconResources.Calendar;
                case IconType.AppCenter: return IconResources.AppCenter;
                case IconType.Open: return IconResources.Open;
                case IconType.Save: return IconResources.Save;
                case IconType.ZoomIn: return IconResources.ZoomIn;
                case IconType.ZoomOut: return IconResources.ZoomOut;
                case IconType.Rotate: return IconResources.Rotate;
                case IconType.Pencil: return IconResources.Pencil;
                case IconType.Eraser: return IconResources.Eraser;
                case IconType.Wifi: return IconResources.Wifi;
                case IconType.Ethernet: return IconResources.Ethernet;
                case IconType.Install: return IconResources.Install;
                case IconType.Play: return IconResources.Play;
                default: return null;
            }
        }

        private static string GetFileName(IconType type)
        {
            switch (type)
            {
                case IconType.Terminal: return "terminal-2.png";
                case IconType.FileManager:
                case IconType.Folder: return "folder.png";
                case IconType.File: return "file.png";
                case IconType.Settings: return "settings-2.png";
                case IconType.About: return "info-circle.png";
                case IconType.Close: return "square-rounded-x.png";
                case IconType.Maximize: return "arrows-maximize.png";
                case IconType.Restore: return "restore.png";
                case IconType.Start: return "home.png";
                case IconType.ArrowUp: return "arrow-up.png";
                case IconType.Refresh: return "refresh.png";
                case IconType.Search: return "search.png";
                case IconType.Trash: return "trash.png";
                case IconType.Reboot: return "reboot.png";
                case IconType.Shutdown: return "shutdown.png";
                case IconType.ImageViewer: return "image.png";
                case IconType.Calculator: return "calc.png";
                case IconType.Paint: return "Design-Paint-Palette-Tray--Streamline-Nova.png";
                case IconType.Network: return "Network-Global--Streamline-Nova.png";
                case IconType.DiskManager: return "disk_manager.png";
                case IconType.HexViewer: return "hexviewer.png";
                case IconType.Calendar: return "calendar.png";
                case IconType.AppCenter: return "app.png";
                case IconType.Open: return "open.png";
                case IconType.Save: return "save.png";
                case IconType.ZoomIn: return "zoom-in.png";
                case IconType.ZoomOut: return "zoom-out.png";
                case IconType.Rotate: return "rotate.png";
                case IconType.Pencil: return "pencil.png";
                case IconType.Eraser: return "eraser.png";
                case IconType.Wifi: return "wifi.png";
                case IconType.Ethernet: return "ethernet.png";
                case IconType.Install: return "install.png";
                case IconType.Play: return "play.png";
                default: return "icon.png";
            }
        }

        public static int Size { get { return IconSize; } }
        public static int LargeSize { get { return LargeIconSize; } }
    }
}
