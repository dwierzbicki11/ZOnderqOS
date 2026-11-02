using System;
using System.IO;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Fail-closed dimension preflight before the Cosmos PNG/BMP decoders
    /// allocate their pixel arrays. The decoder still validates image data;
    /// this guard only limits the maximum allocation requested by a header.
    /// </summary>
    public static class ImageHeaderGuard
    {
        // A decoded ARGB image at the pixel limit already needs 64 MiB.
        // Keep the GUI responsive on machines with limited available RAM.
        public const int MaxDimension = 16384;
        public const long MaxPixels = 16L * 1024 * 1024;

        public static bool IsSafeToDecode(string path, bool png)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                using (Stream stream = File.OpenRead(path))
                {
                    byte[] header = new byte[26];
                    int read = 0;
                    while (read < header.Length)
                    {
                        int count = stream.Read(header, read, header.Length - read);
                        if (count <= 0)
                            break;
                        read += count;
                    }
                    return HasSafeDimensions(header, read, png);
                }
            }
            catch (Exception)
            {
                // Removed volumes, broken files and permission failures must
                // never reach the decoder or crash the desktop.
                return false;
            }
        }

        // Pure parser for host C# regression tests. Does not trust length,
        // signature, IHDR type, DIB size or signed BMP dimensions.
        public static bool HasSafeDimensions(byte[] header, int length, bool png)
        {
            if (header == null || length < 0 || length > header.Length)
                return false;

            long width;
            long height;
            if (png)
            {
                if (length < 24 ||
                    header[0] != 137 || header[1] != 80 ||
                    header[2] != 78 || header[3] != 71 ||
                    header[4] != 13 || header[5] != 10 ||
                    header[6] != 26 || header[7] != 10 ||
                    header[8] != 0 || header[9] != 0 ||
                    header[10] != 0 || header[11] != 13 ||
                    header[12] != 73 || header[13] != 72 ||
                    header[14] != 68 || header[15] != 82)
                    return false;

                width = ReadBigEndian32(header, 16);
                height = ReadBigEndian32(header, 20);
            }
            else
            {
                if (length < 26 || header[0] != 66 || header[1] != 77 ||
                    ReadLittleEndian32(header, 14) < 40)
                    return false;

                int signedWidth = unchecked((int)ReadLittleEndian32(header, 18));
                int signedHeight = unchecked((int)ReadLittleEndian32(header, 22));
                width = signedWidth;
                height = signedHeight < 0 ? -(long)signedHeight : signedHeight;
            }

            return width > 0 && height > 0 &&
                   width <= MaxDimension && height <= MaxDimension &&
                   width * height <= MaxPixels;
        }

        private static uint ReadBigEndian32(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24) |
                   ((uint)bytes[offset + 1] << 16) |
                   ((uint)bytes[offset + 2] << 8) |
                   bytes[offset + 3];
        }

        private static uint ReadLittleEndian32(byte[] bytes, int offset)
        {
            return (uint)bytes[offset] |
                   ((uint)bytes[offset + 1] << 8) |
                   ((uint)bytes[offset + 2] << 16) |
                   ((uint)bytes[offset + 3] << 24);
        }
    }
}
