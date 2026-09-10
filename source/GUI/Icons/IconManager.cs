using System.Drawing;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;
        private const int LargeIconSize = 40;
        private const int IconCount = 14;
        private const int MaxScaledCacheEntries = 192;

        // Keep only raw pixel buffers as permanent roots. We do not depend on a cached
        // Png/Bitmap object surviving OrionGC and we never call Canvas' scaling overload
        // from the render path. Each PNG is decoded once and each requested size is
        // scaled once. Later frames only read these persistent int[] buffers.
        private static readonly int[][] sourcePixels = new int[IconCount][];
        private static readonly int[] sourceWidths = new int[IconCount];
        private static readonly int[] sourceHeights = new int[IconCount];
        private static readonly bool[] sourceLoadAttempted = new bool[IconCount];

        private static readonly int[][] scaledPixels = new int[MaxScaledCacheEntries][];
        private static readonly IconType[] scaledTypes = new IconType[MaxScaledCacheEntries];
        private static readonly int[] scaledWidths = new int[MaxScaledCacheEntries];
        private static readonly int[] scaledHeights = new int[MaxScaledCacheEntries];
        private static int scaledCacheCount;

        /// <summary>
        /// Decode all embedded icon PNGs once while the GUI is starting. This avoids
        /// filesystem access and PNG decoding during later desktop/window renders.
        /// </summary>
        public static void Preload()
        {
            for (int i = 0; i < IconCount; i++)
                EnsureSource((IconType)i);
        }

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            DrawScaled(canvas, type, x, y, IconSize, IconSize);
        }

        /// <summary>
        /// Allocation-free hot path. The first use of an icon/size pair creates one
        /// persistent scaled pixel buffer. Every later call only blits that same buffer.
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

            // The fixed cache should never normally fill. If it does, keep the icon
            // visible with direct nearest-neighbour sampling without allocating memory.
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

                // Decode directly from the embedded resource. No /root cache file is
                // required, so icons cannot disappear because of a VFS/path problem.
                Png decoded = new Png(data);
                int width = (int)decoded.Width;
                int height = (int)decoded.Height;
                int[] pixels = decoded.RawData;

                if (width <= 0 || height <= 0 || pixels == null || pixels.Length < width * height)
                    return false;

                // RawData becomes the permanent cache object. The temporary Png wrapper
                // itself is no longer needed after this one-time decode.
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

        /// <summary>
        /// Draw persistent pixels directly. No Image wrapper, no ScaleImage() and no
        /// temporary arrays are involved. Color is a value type, so this loop does not
        /// create managed objects per pixel/frame. Transparent pixels are skipped.
        /// </summary>
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
                default: return null;
            }
        }

        public static int Size { get { return IconSize; } }
        public static int LargeSize { get { return LargeIconSize; } }
    }
}
