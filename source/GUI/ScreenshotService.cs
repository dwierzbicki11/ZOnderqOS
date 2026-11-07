using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class ScreenshotService
    {
        private static int counter = 1;

        public static string CaptureAndSave(Canvas canvas, string targetDir = "0:/Screenshots")
        {
            if (canvas == null)
                return null;

            int width = (int)canvas.Width;
            int height = (int)canvas.Height;
            if (width <= 0 || height <= 0)
                return null;

            try
            {
                uint[] pixels = new uint[width * height];
                for (int y = 0; y < height; y++)
                {
                    int rowOffset = y * width;
                    for (int x = 0; x < width; x++)
                    {
                        Color c = canvas.GetPointColor(x, y);
                        pixels[rowOffset + x] = (uint)c.ToArgb();
                    }
                }

                byte[] bmpBytes = BmpEncoder.Encode(width, height, pixels);
                if (bmpBytes.Length == 0)
                    return null;

                if (!Directory.Exists(targetDir))
                {
                    try { Directory.CreateDirectory(targetDir); }
                    catch { targetDir = "0:/"; }
                }

                string fileName = "zrzut_" + counter + ".bmp";
                string fullPath = Path.Combine(targetDir, fileName);
                while (File.Exists(fullPath) && counter < 9999)
                {
                    counter++;
                    fileName = "zrzut_" + counter + ".bmp";
                    fullPath = Path.Combine(targetDir, fileName);
                }

                File.WriteAllBytes(fullPath, bmpBytes);
                counter++;

                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Info,
                    "ZRZUT EKRANU",
                    "Zapisano zrzut ekranu",
                    "Plik: " + fullPath);

                return fullPath;
            }
            catch (Exception ex)
            {
                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.System,
                    "ZRZUT EKRANU",
                    "Blad zapisu zrzutu",
                    ex.Message);
                return null;
            }
        }
    }
}
