using System;
using System.IO;

namespace ZonderqOS.SystemCore.Diagnostics
{
    public static class Fat32DiagnosticHelper
    {
        private const string DiagDirName = "diag_sysbench_tmp";
        private const string DiagFileName = "sysbench_4k.bin";

        public static bool TryFindZonderqDataRoot(out string rootPath)
        {
            // Disk.Initialize() binds ONLY the uniquely approved ZONDERQDATA
            // FAT32 partition to "/"; never guess 0:/1: or scan foreign disks.
            const string root = "/";
            try
            {
                string state = Path.Combine(root, "boot", "boot_state.cfg");
                if (Directory.Exists(root) && File.Exists(state))
                {
                    using (var reader = new StreamReader(state))
                    {
                        if (reader.ReadLine() == "# ZonderqOS A/B Bootloader State Configuration")
                        {
                            rootPath = root;
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Fail closed without touching any disk.
            }
            rootPath = null;
            return false;
        }

        public static (bool ok, string reason, string metrics) RunIsolatedFat32Test(string rootPath)
        {
            if (string.IsNullOrEmpty(rootPath))
                return (false, "Brak zweryfikowanej partycji ZONDERQDATA", "root=null");

            string diagDir = Path.Combine(rootPath, DiagDirName);
            string testFile = Path.Combine(diagDir, DiagFileName);
            const int size = 4096;
            byte[] writeBuf = new byte[size];
            uint expectedSum = 0;
            for (int i = 0; i < size; i++)
            {
                byte b = (byte)((i * 31 + 7) & 0xFF);
                writeBuf[i] = b;
                expectedSum += b;
            }

            bool directoryCreated = false;
            bool fileCreated = false;
            try
            {
                // A previous run's files or unknown user data must never be overwritten.
                if (Directory.Exists(diagDir) || File.Exists(diagDir))
                    return (false, "Istniejacy katalog diagnostyczny: odmowa nadpisania", "existing_diag_path");
                Directory.CreateDirectory(diagDir);
                directoryCreated = true;

                using (var fs = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write))
                {
                    fileCreated = true;
                    fs.Write(writeBuf, 0, size);
                    fs.Flush();
                }

                byte[] readBuf = new byte[size];
                int totalRead = 0;
                using (var fs = new FileStream(testFile, FileMode.Open, FileAccess.Read))
                {
                    while (totalRead < size)
                    {
                        int n = fs.Read(readBuf, totalRead, size - totalRead);
                        if (n <= 0) break;
                        totalRead += n;
                    }
                }

                if (totalRead != size)
                    return (false, $"Odczytano {totalRead}/{size} B", $"bytes={totalRead}");

                uint actualSum = 0;
                for (int i = 0; i < size; i++)
                    actualSum += readBuf[i];

                if (actualSum != expectedSum)
                    return (false, "Niezgodna suma kontrolna FAT32", $"exp={expectedSum},act={actualSum}");

                return (true, "Zapis, odczyt i suma kontrolna 4KB OK", $"4096B,sum={actualSum}");
            }
            catch (Exception ex)
            {
                return (false, "Wyjatek FAT32: " + ex.Message, "error");
            }
            finally
            {
                try
                {
                    if (fileCreated && File.Exists(testFile)) File.Delete(testFile);
                    if (directoryCreated && Directory.Exists(diagDir)) Directory.Delete(diagDir, false);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }
    }
}
