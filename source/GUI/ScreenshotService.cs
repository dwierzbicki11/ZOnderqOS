using System;
using System.Drawing;
using System.IO;
using Cosmos.Kernel.System.Graphics;

namespace ZonderqOS.GUI
{
    public static class ScreenshotService
    {
        private static int counter = 1;

        public static string CaptureAndSave(Canvas canvas, string targetDir = null)
        {
            if (canvas == null || !SecurityContext.IsAuthenticated)
                return null;

            int width = (int)canvas.Width;
            int height = (int)canvas.Height;
            if (width <= 0 || height <= 0)
                return null;

            try
            {
                string user = SecurityContext.CurrentUser ?? "root";
                string home = string.IsNullOrWhiteSpace(SecurityContext.CurrentHome)
                    ? (string.Equals(user, "root", StringComparison.Ordinal) ? "/root" : "/home/" + user)
                    : SecurityContext.CurrentHome;
                if (string.IsNullOrWhiteSpace(targetDir) || targetDir.StartsWith("0:", StringComparison.Ordinal))
                    targetDir = home.TrimEnd('/') + "/Screenshots";

                if (!PermissionManager.CanCreate(targetDir, user))
                    return null;

                byte[] bmpBytes = BmpEncoder.EncodeCanvas(canvas);
                if (bmpBytes.Length == 0)
                    return null;

                if (!Directory.Exists(targetDir))
                    Directory.CreateDirectory(targetDir);

                string fileName = "screenshot_" + counter + ".bmp";
                string fullPath = Path.Combine(targetDir, fileName);
                while (File.Exists(fullPath) && counter < 9999)
                {
                    counter++;
                    fileName = "screenshot_" + counter + ".bmp";
                    fullPath = Path.Combine(targetDir, fileName);
                }

                if (!PermissionManager.CanCreate(fullPath, user))
                    return null;

                File.WriteAllBytes(fullPath, bmpBytes);
                PermissionManager.SetPermission(fullPath, user, 600);
                counter++;

                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Info,
                    "SCREENSHOT",
                    "Screenshot saved",
                    "File: " + fullPath);

                return fullPath;
            }
            catch (Exception ex)
            {
                global::ZonderqOS.NotificationService.Post(
                    global::ZonderqOS.NotificationKind.Error,
                    "SCREENSHOT",
                    "Failed to save screenshot",
                    ex.Message);
                return null;
            }
        }
    }
}
