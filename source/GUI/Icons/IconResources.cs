using System;
using System.IO;
using System.Reflection;

namespace ZonderqOS.GUI.Icons
{
    internal static class IconResources
    {
        private static readonly Assembly Assembly = typeof(IconResources).Assembly;

        public static byte[] Get(string resourceName)
        {
            using (Stream stream = Assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    return null;

                int length = (int)stream.Length;
                byte[] data = new byte[length];
                int offset = 0;

                while (offset < length)
                {
                    int read = stream.Read(data, offset, length - offset);
                    if (read <= 0)
                        return null;

                    offset += read;
                }

                return data;
            }
        }
    }
}
