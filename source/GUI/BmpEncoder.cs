using System;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Pure managed 32-bit BMP encoder with zero external dependencies.
    /// Produces standard top-down 32bpp BGRA Windows Bitmap files.
    /// </summary>
    public static class BmpEncoder
    {
        public static byte[] Encode(int width, int height, uint[] argbPixels)
        {
            if (width <= 0 || height <= 0 || argbPixels == null)
                return Array.Empty<byte>();

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
    }
}
