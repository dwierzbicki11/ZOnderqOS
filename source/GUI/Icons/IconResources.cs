using System;
using System.IO;
using System.Reflection;

namespace ZonderqOS.GUI.Icons
{
    internal static class IconResources
    {
        private const string Prefix = "Icons.";

        public static byte[] Terminal { get { return Load("terminal-2.png"); } }
        public static byte[] Folder { get { return Load("folder.png"); } }
        public static byte[] File { get { return Load("file.png"); } }
        public static byte[] Settings { get { return Load("settings-2.png"); } }
        public static byte[] About { get { return Load("info-circle.png"); } }
        public static byte[] Close { get { return Load("square-rounded-x.png"); } }
        public static byte[] Maximize { get { return Load("arrows-maximize.png"); } }
        public static byte[] Restore { get { return Load("restore.png"); } }
        public static byte[] Start { get { return Load("home.png"); } }
        public static byte[] ArrowUp { get { return Load("arrow-up.png"); } }
        public static byte[] Refresh { get { return Load("refresh.png"); } }
        public static byte[] Search { get { return Load("search.png"); } }
        public static byte[] Trash { get { return Load("trash.png"); } }
        public static byte[] Reboot { get { return Load("reboot.png"); } }
        public static byte[] Shutdown { get { return Load("shutdown.png"); } }

        private static byte[] Load(string fileName)
        {
            Assembly assembly = typeof(IconResources).Assembly;
            string resourceName = Prefix + fileName;

            using (Stream stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null || stream.Length <= 0 || stream.Length > Int32.MaxValue)
                    return null;

                byte[] data = new byte[(int)stream.Length];
                int offset = 0;
                while (offset < data.Length)
                {
                    int read = stream.Read(data, offset, data.Length - offset);
                    if (read <= 0)
                        break;
                    offset += read;
                }

                return offset == data.Length ? data : null;
            }
        }
    }
}
