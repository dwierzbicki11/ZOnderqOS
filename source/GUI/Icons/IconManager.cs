using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using CosmosBitmap = Cosmos.Kernel.System.Graphics.Bitmap;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;
        private const int LargeIconSize = 40;
        private const int MaxScaledCacheEntries = 192;
        private const string CacheDirectory = "/root/.zonderq-icons";

        private static readonly Png[] imageCache = new Png[14];
        private static readonly ScaledIconCacheEntry[] scaledImageCache = new ScaledIconCacheEntry[MaxScaledCacheEntries];
        private static int scaledImageCacheCount;

        private struct ScaledIconCacheEntry
        {
            public IconType Type;
            public int Width;
            public int Height;
            public CosmosBitmap Image;
        }

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            DrawScaled(canvas, type, x, y, IconSize, IconSize);
        }

        /// <summary>
        /// Draws an icon without allocating a temporary scaling buffer on every frame.
        ///
        /// Cosmos Gen3 Canvas.DrawImage(image, x, y, width, height) calls ScaleImage(),
        /// which creates a fresh int[] for every invocation. A task-manager frame draws
        /// many icons, so that old path continuously expanded the GC heap while the
        /// window was simply left open.
        ///
        /// We scale each icon/size combination once into a bounded Bitmap cache and then
        /// use the non-scaling DrawImage overload. That overload reuses RawData directly.
        /// The cache is intentionally fixed-size so it can never become another leak.
        /// </summary>
        public static void DrawScaled(Canvas canvas, IconType type, int x, int y, int width, int height)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;

            Png image = GetImage(type);
            if (image == null)
                return;

            if ((int)image.Width == width && (int)image.Height == height)
            {
                canvas.DrawImage(image, x, y);
                return;
            }

            CosmosBitmap scaled = GetScaledImage(type, image, width, height);
            if (scaled != null)
            {
                canvas.DrawImage(scaled, x, y);
                return;
            }

            // Never fall back to the scaling overload here: it allocates a new int[]
            // every frame. If the bounded cache is ever exhausted, drawing the original
            // PNG is preferable to turning a cosmetic icon into a RAM leak.
            canvas.DrawImage(image, x, y);
        }

        private static CosmosBitmap GetScaledImage(IconType type, Png source, int width, int height)
        {
            IconType cacheType = CanonicalizeType(type);

            for (int i = 0; i < scaledImageCacheCount; i++)
            {
                ScaledIconCacheEntry entry = scaledImageCache[i];
                if (entry.Type == cacheType && entry.Width == width && entry.Height == height)
                    return entry.Image;
            }

            if (scaledImageCacheCount >= MaxScaledCacheEntries)
                return null;

            try
            {
                CosmosBitmap scaled = ScaleOnce(source, width, height);
                if (scaled == null)
                    return null;

                scaledImageCache[scaledImageCacheCount] = new ScaledIconCacheEntry
                {
                    Type = cacheType,
                    Width = width,
                    Height = height,
                    Image = scaled
                };
                scaledImageCacheCount++;
                return scaled;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Allocation happens only on a cache miss. Pixel scaling itself writes directly
        /// into the destination Bitmap.RawData and creates no temporary ScaleImage array.
        /// Alpha is preserved, so the normal DrawImage overload can blend PNG icons over
        /// the current window background exactly as before.
        /// </summary>
        private static CosmosBitmap ScaleOnce(Png source, int width, int height)
        {
            int sourceWidth = (int)source.Width;
            int sourceHeight = (int)source.Height;
            if (sourceWidth <= 0 || sourceHeight <= 0)
                return null;

            int[] sourcePixels = source.RawData;
            if (sourcePixels == null || sourcePixels.Length == 0)
                return null;

            var destination = new CosmosBitmap((uint)width, (uint)height, ColorDepth.ColorDepth32);
            int[] destinationPixels = destination.RawData;

            for (int dy = 0; dy < height; dy++)
            {
                int sy = (dy * sourceHeight) / height;
                int sourceRow = sy * sourceWidth;
                int destinationRow = dy * width;

                for (int dx = 0; dx < width; dx++)
                {
                    int sx = (dx * sourceWidth) / width;
                    destinationPixels[destinationRow + dx] = sourcePixels[sourceRow + sx];
                }
            }

            return destination;
        }

        private static IconType CanonicalizeType(IconType type)
        {
            return type == IconType.FileManager ? IconType.Folder : type;
        }

        private static Png GetImage(IconType type)
        {
            int index = (int)type;
            if (index < 0 || index >= imageCache.Length)
                return null;

            if (imageCache[index] != null)
                return imageCache[index];

            byte[] data = GetResource(type);
            if (data == null || data.Length == 0)
                return null;

            try
            {
                Directory.CreateDirectory(CacheDirectory);
                string path = Path.Combine(CacheDirectory, GetFileName(type));

                if (!File.Exists(path))
                    File.WriteAllBytes(path, data);

                imageCache[index] = new Png(path);
                return imageCache[index];
            }
            catch
            {
                return null;
            }
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
                default: return "icon.png";
            }
        }

        public static int Size { get { return IconSize; } }
        public static int LargeSize { get { return LargeIconSize; } }
    }
}
