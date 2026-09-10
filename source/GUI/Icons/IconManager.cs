using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI.Icons
{
    public static class IconManager
    {
        private const int IconSize = 18;
        private const string CacheDirectory = "/root/.zonderq-icons";
        private static readonly Png[] imageCache = new Png[10];

        public static void Draw(Canvas canvas, IconType type, int x, int y, Color color)
        {
            Png image = GetImage(type);
            if (image == null)
                return;

            canvas.DrawImage(image, x, y, IconSize, IconSize);
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
                case IconType.Terminal:
                    return IconResources.Get("Icons.terminal-2.png");
                case IconType.FileManager:
                case IconType.Folder:
                    return IconResources.Get("Icons.folder.png");
                case IconType.File:
                    return IconResources.Get("Icons.file.png");
                case IconType.Settings:
                    return IconResources.Get("Icons.settings-2.png");
                case IconType.About:
                    return IconResources.Get("Icons.info-circle.png");
                case IconType.Close:
                    return IconResources.Get("Icons.square-rounded-x.png");
                case IconType.Maximize:
                    return IconResources.Get("Icons.arrows-maximize.png");
                case IconType.Restore:
                    return IconResources.Get("Icons.restore.png");
                case IconType.Start:
                    return IconResources.Get("Icons.home.png");
                default:
                    return null;
            }
        }

        private static string GetFileName(IconType type)
        {
            switch (type)
            {
                case IconType.Terminal:
                    return "terminal-2.png";
                case IconType.FileManager:
                case IconType.Folder:
                    return "folder.png";
                case IconType.File:
                    return "file.png";
                case IconType.Settings:
                    return "settings-2.png";
                case IconType.About:
                    return "info-circle.png";
                case IconType.Close:
                    return "square-rounded-x.png";
                case IconType.Maximize:
                    return "arrows-maximize.png";
                case IconType.Restore:
                    return "restore.png";
                case IconType.Start:
                    return "home.png";
                default:
                    return "icon.png";
            }
        }

        public static int Size
        {
            get { return IconSize; }
        }
    }
}
