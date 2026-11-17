using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;
using ZonderqOS.SystemCore;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Pure managed 32-bit BMP encoder with zero external dependencies.
    /// Produces standard top-down 32bpp BGRA Windows Bitmap files.
    /// </summary>
    public static class BmpEncoder
    {
        private static byte[] AllocateAndWriteHeaders(int width, int height)
        {
            int imageSize = width * height * 4;
            int fileSize = 54 + imageSize;
            byte[] bmp = new byte[fileSize];

            // --- BITMAPFILEHEADER (14 bytes) ---
            bmp[0] = (byte)'B';
            bmp[1] = (byte)'M';
            bmp[2] = (byte)(fileSize);
            bmp[3] = (byte)(fileSize >> 8);
            bmp[4] = (byte)(fileSize >> 16);
            bmp[5] = (byte)(fileSize >> 24);
            bmp[10] = 54; // Pixel data offset

            // --- BITMAPINFOHEADER (40 bytes) ---
            bmp[14] = 40; // Header size
            bmp[18] = (byte)(width);
            bmp[19] = (byte)(width >> 8);
            bmp[20] = (byte)(width >> 16);
            bmp[21] = (byte)(width >> 24);

            // Top-down image is represented by negative height
            int negHeight = -height;
            bmp[22] = (byte)(negHeight);
            bmp[23] = (byte)(negHeight >> 8);
            bmp[24] = (byte)(negHeight >> 16);
            bmp[25] = (byte)(negHeight >> 24);

            bmp[26] = 1;  // Color planes
            bmp[28] = 32; // Bits per pixel (BGRA)
            bmp[34] = (byte)(imageSize);
            bmp[35] = (byte)(imageSize >> 8);
            bmp[36] = (byte)(imageSize >> 16);
            bmp[37] = (byte)(imageSize >> 24);

            return bmp;
        }

        public static byte[] EncodeCanvas(Canvas canvas)
        {
            if (canvas == null)
                return Array.Empty<byte>();

            int width = (int)canvas.Width;
            int height = (int)canvas.Height;
            if (width <= 0 || height <= 0)
                return Array.Empty<byte>();

            byte[] bmp = AllocateAndWriteHeaders(width, height);
            int outIdx = 54;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color c = canvas.GetPointColor(x, y);
                    bmp[outIdx] = c.B;
                    bmp[outIdx + 1] = c.G;
                    bmp[outIdx + 2] = c.R;
                    bmp[outIdx + 3] = c.A;
                    outIdx += 4;
                }
            }
            return bmp;
        }

        public static byte[] Encode(int width, int height, uint[] argbPixels)
        {
            if (width <= 0 || height <= 0 || argbPixels == null)
                return Array.Empty<byte>();

            byte[] bmp = AllocateAndWriteHeaders(width, height);

            // Copy pixels in standard little-endian BGRA format
            int outIdx = 54;
            int total = width * height;
            int len = Math.Min(total, argbPixels.Length);

            for (int i = 0; i < len; i++)
            {
                uint c = argbPixels[i];
                bmp[outIdx]     = (byte)(c & 0xFF);         // Blue
                bmp[outIdx + 1] = (byte)((c >> 8) & 0xFF);  // Green
                bmp[outIdx + 2] = (byte)((c >> 16) & 0xFF); // Red
                bmp[outIdx + 3] = (byte)((c >> 24) & 0xFF); // Alpha
                outIdx += 4;
            }

            return bmp;
        }

        public static unsafe bool EncodeToStream(Stream output, int width, int height, uint* argbPixels)
        {
            if (output == null || width <= 0 || height <= 0 || argbPixels == null)
                return false;

            int imageSize = width * height * 4;
            int fileSize = 54 + imageSize;
            const int chunkBytes = 16384;
            NativeScratchBuffer<byte> chunk = default;
            try
            {
                chunk.Allocate(chunkBytes);
                Span<byte> buf = chunk.AsSpan();
                for (int i = 0; i < 54; i++)
                    buf[i] = 0;

                buf[0] = (byte)'B';
                buf[1] = (byte)'M';
                buf[2] = (byte)fileSize;
                buf[3] = (byte)(fileSize >> 8);
                buf[4] = (byte)(fileSize >> 16);
                buf[5] = (byte)(fileSize >> 24);
                buf[10] = 54;

                buf[14] = 40;
                buf[18] = (byte)width;
                buf[19] = (byte)(width >> 8);
                buf[20] = (byte)(width >> 16);
                buf[21] = (byte)(width >> 24);

                int negHeight = -height;
                buf[22] = (byte)negHeight;
                buf[23] = (byte)(negHeight >> 8);
                buf[24] = (byte)(negHeight >> 16);
                buf[25] = (byte)(negHeight >> 24);

                buf[26] = 1;
                buf[28] = 32;
                buf[34] = (byte)imageSize;
                buf[35] = (byte)(imageSize >> 8);
                buf[36] = (byte)(imageSize >> 16);
                buf[37] = (byte)(imageSize >> 24);

                int pos = 54;
                int total = width * height;
                for (int i = 0; i < total; i++)
                {
                    if (pos + 4 > chunkBytes)
                    {
                        output.Write(buf.Slice(0, pos));
                        pos = 0;
                    }
                    uint c = argbPixels[i];
                    buf[pos] = (byte)(c & 0xFF);
                    buf[pos + 1] = (byte)((c >> 8) & 0xFF);
                    buf[pos + 2] = (byte)((c >> 16) & 0xFF);
                    buf[pos + 3] = (byte)((c >> 24) & 0xFF);
                    pos += 4;
                }
                if (pos > 0)
                    output.Write(buf.Slice(0, pos));
                output.Flush();
                return true;
            }
            finally
            {
                chunk.Dispose();
            }
        }
    }
}
