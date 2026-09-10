using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;
        private const int LargeIconSize = 40;
        private const string CacheDirectory = "/root/.zonderq-icons";
        private static readonly Png[] imageCache = new Png[14];

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            DrawScaled(canvas, type, x, y, IconSize, IconSize);
        }

        // Draw directly from the single cached Png. No resized bitmap/cache is created per frame.
        public static void DrawScaled(Canvas canvas, IconType type, int x, int y, int width, int height)
        {
            Png image = GetImage(type);
            if (image == null)
                return;
            canvas.DrawImage(image, x, y, width, height);
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