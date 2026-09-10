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

        // Direct reference arrays are intentional. OrionGC Gen3 is still young and
        // keeping managed image references inside an array of structs can make those
        // references harder for the collector to preserve correctly. Plain reference
        // arrays give the collector an unambiguous root and stop cached icons from
        // disappearing after a collection.
        private static readonly Png[] imageCache = new Png[14];
        private static readonly CosmosBitmap[] scaledImages = new CosmosBitmap[MaxScaledCacheEntries];
        private static readonly IconType[] scaledTypes = new IconType[MaxScaledCacheEntries];
        private static readonly int[] scaledWidths = new int[MaxScaledCacheEntries];
        private static readonly int[] scaledHeights = new int[MaxScaledCacheEntries];
        private static int scaledImageCacheCount;

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            DrawScaled(canvas, type, x, y, IconSize, IconSize);
        }

        /// <summary>
        /// Draw an icon with zero per-frame scaling allocations.
        ///
        /// Cosmos Gen3 Canvas.DrawImage(image, x, y, width, height) allocates a new
        /// int[] every call through ScaleImage(). We never use that overload here.
        /// Every icon/size pair is scaled once, stored strongly in a fixed cache, and
        /// every later frame only blits the already-scaled bitmap.
        /// </summary>
        public static void DrawScaled(Canvas canvas, IconType type, int x, int y, int width, int height)
        {
            if (canvas == null || width <= 0 || height <= 0)
                return;

            type = CanonicalizeType(type);
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
                // Non-scaling overload: reuses RawData and does not allocate a new
                // pixel buffer. Alpha is still handled by Canvas.DrawPoint(Color,...).
                canvas.DrawImage(scaled, x, y);
                return;
            }

            // The cache is deliberately bounded. If it were ever full, draw the
            // original image instead of calling the allocating scaling overload.
            // This keeps icons visible without turning rendering into a heap leak.
            canvas.DrawImage(image, x, y);
        }

        private static CosmosBitmap GetScaledImage(IconType type, Png source, int width, int height)
        {
            for (int i = 0; i < scaledImageCacheCount; i++)
            {
                if (scaledTypes[i] == type && scaledWidths[i] == width && scaledHeights[i] == height)
                    return scaledImages[i];
            }

            if (scaledImageCacheCount >= MaxScaledCacheEntries)
                return null;

            try
            {
                CosmosBitmap scaled = ScaleOnce(source, width, height);
                if (scaled == null)
                    return null;

                int slot = scaledImageCacheCount;
                scaledTypes[slot] = type;
                scaledWidths[slot] = width;
                scaledHeights[slot] = height;
                scaledImages[slot] = scaled; // strong GC root
                scaledImageCacheCount = slot + 1;
                return scaled;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Scale exactly once on a cache miss. The only pixel allocation is the
        /// destination Bitmap.RawData that remains cached for the lifetime of the GUI.
        /// There is no temporary ScaleImage buffer.
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

            CosmosBitmap destination = new CosmosBitmap((uint)width, (uint)height, ColorDepth.ColorDepth32);
            int[] destinationPixels = destination.RawData;

            // Match Cosmos' nearest-neighbour scaling layout, but write straight into
            // the persistent destination buffer instead of allocating a temporary one.
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
                    destinationPixels[destinationRow + dx] = sourcePixels[sourceRow + sx];
                }
            }

            return destination;
        }

        private static IconType CanonicalizeType(IconType type)
        {
            // FileManager and Folder use the same PNG. Keep a single decoded source
            // and a single family of scaled copies instead of duplicating both.
            return type == IconType.FileManager ? IconType.Folder : type;
        }

        private static Png GetImage(IconType type)
        {
            type = CanonicalizeType(type);
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

                Png decoded = new Png(path);
                imageCache[index] = decoded; // strong GC root
                return decoded;
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
