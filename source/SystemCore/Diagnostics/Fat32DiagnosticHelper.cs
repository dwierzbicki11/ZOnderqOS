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
            string[] candidates = { @"0:\", @"1:\" };
            for (int i = 0; i < candidates.Length; i++)
            {
                string root = candidates[i];
                try
                {
                    if (!Directory.Exists(root))
                        continue;

                    // Never touch EFI or Kali partitions
                    if (Directory.Exists(Path.Combine(root, "EFI")) ||
                        Directory.Exists(Path.Combine(root, "boot")))
                        continue;

                    rootPath = root;
                    return true;
                }
                catch
                {
                    // Skip unreadable root
                }
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

            try
            {
                if (!Directory.Exists(diagDir))
                    Directory.CreateDirectory(diagDir);

                using (var fs = new FileStream(testFile, FileMode.Create, FileAccess.Write))
                {
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
                    if (File.Exists(testFile)) File.Delete(testFile);
                    if (Directory.Exists(diagDir)) Directory.Delete(diagDir, false);
                }
                catch
                {
                    // Best-effort cleanup
                }
            }
        }
    }
}
