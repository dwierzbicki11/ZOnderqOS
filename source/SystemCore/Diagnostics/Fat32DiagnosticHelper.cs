using System;
using System.IO;

namespace ZonderqOS.SystemCore.Diagnostics
{
    /// <summary>
    /// Tests only the persistent FAT32 root selected and mounted by Disk.
    /// Never guesses a writable volume from 0:\\, 1:\\ or /boot contents.
    /// </summary>
    public static class Fat32DiagnosticHelper
    {
        private const string TestFileName = "sysbench_4k.bin";

        public static bool TryFindZonderqDataRoot(out string rootPath)
        {
            rootPath = null;
            if (!global::ZonderqOS.Disk.IsApprovedPersistentRootMounted)
                return false;

            try
            {
                if (!Directory.Exists("/"))
                    return false;

                rootPath = "/";
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static (bool ok, string reason, string metrics) RunIsolatedFat32Test(string rootPath)
        {
            // Explicit identity + privilege preflight. Never test any other
            // drive even if a caller supplied a valid-looking path.
            if (rootPath != "/" || !TryFindZonderqDataRoot(out string approved) ||
                approved != "/")
                return (false, "ZONDERQDATA root not verified", "root_rejected");
            if (!global::ZonderqOS.SecurityContext.IsAuthenticated ||
                global::ZonderqOS.SecurityContext.CurrentUid != 0)
                return (false, "FAT32 write test requires authenticated root", "permission_denied");

            // A fresh run-specific directory prevents truncating any file
            // left by a previous interrupted benchmark.
            string diagDir = Path.Combine(rootPath, "diag_sysbench_" +
                Environment.TickCount64.ToString("X"));
            string testFile = Path.Combine(diagDir, TestFileName);
            bool createdDirectory = false;
            const int size = 4096;
            byte[] writeBuf = new byte[size];
            uint expectedSum = 0;
            for (int i = 0; i < size; i++)
            {
                byte b = (byte)((i * 31 + 7) & 0xff);
                writeBuf[i] = b;
                expectedSum += b;
            }

            try
            {
                if (Directory.Exists(diagDir) || File.Exists(testFile))
                    return (false, "Temporary test path already exists; refusing overwrite", "collision");

                Directory.CreateDirectory(diagDir);
                createdDirectory = true;

                using (var stream = new FileStream(testFile, FileMode.CreateNew, FileAccess.Write))
                {
                    stream.Write(writeBuf, 0, size);
                    stream.Flush();
                }

                byte[] readBuf = new byte[size];
                int totalRead = 0;
                using (var stream = new FileStream(testFile, FileMode.Open, FileAccess.Read))
                {
                    while (totalRead < size)
                    {
                        int count = stream.Read(readBuf, totalRead, size - totalRead);
                        if (count <= 0)
                            break;
                        totalRead += count;
                    }
                }

                if (totalRead != size)
                    return (false, "FAT32 short read: " + totalRead + "/4096", "bytes=" + totalRead);

                uint actualSum = 0;
                for (int i = 0; i < size; i++)
                {
                    actualSum += readBuf[i];
                    if (readBuf[i] != writeBuf[i])
                        return (false, "FAT32 data mismatch at byte " + i, "first_mismatch=" + i);
                }

                if (actualSum != expectedSum)
                    return (false, "FAT32 checksum mismatch", "checksum_mismatch");

                return (true, "Verified 4 KiB create/write/read/compare", "bytes=4096,sum=" + actualSum);
            }
            catch (Exception ex)
            {
                return (false, "FAT32 diagnostic I/O failed: " + ex.Message, "error");
            }
            finally
            {
                if (createdDirectory)
                {
                    try
                    {
                        if (File.Exists(testFile))
                            File.Delete(testFile);
                        if (Directory.Exists(diagDir))
                            Directory.Delete(diagDir, false);
                    }
                    catch
                    {
                        // Never delete another directory or user file.
                        // Remaining test dir is clearly named for inspection.
                    }
                }
            }
        }
    }
}
