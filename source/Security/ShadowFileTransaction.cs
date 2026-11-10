using System;
using System.IO;

namespace ZonderqOS
{
    /// <summary>
    /// Crash-tolerant replacement of the password hash database. The existing
    /// file stays readable until the fully written replacement is staged.
    /// Recover a missing primary from .bak on the next boot after power loss.
    /// This is NOT a filesystem transaction or an fsync guarantee on FAT32.
    /// </summary>
    internal static class ShadowFileTransaction
    {
        internal static bool RecoverMissingPrimary(string path)
        {
            if (!File.Exists(path))
            {
                string backup = path + ".bak";
                if (!File.Exists(backup))
                    return false;

                File.Move(backup, path);
            }

            // A .new file never wins over a committed primary/backup.
            // It could contain sensitive credential hashes from a torn write.
            string staged = path + ".new";
            if (File.Exists(staged))
                File.Delete(staged);

            return File.Exists(path);
        }

        internal static void Replace(string path, string[] lines, Action<string> protectPath)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Shadow database path is required.", nameof(path));
            if (lines == null || lines.Length == 0)
                throw new ArgumentException("Refusing to write an empty shadow database.", nameof(lines));
            if (!File.Exists(path))
                throw new FileNotFoundException("The shadow database is missing.", path);

            string staged = path + ".new";
            string backup = path + ".bak";

            try
            {
                // Apply root-only ACL before creating temporary credential data.
                protectPath?.Invoke(staged);
                protectPath?.Invoke(backup);

                if (File.Exists(staged))
                    File.Delete(staged);
                File.WriteAllLines(staged, lines);

                // Old backup may be discarded only while primary still exists.
                if (File.Exists(backup))
                    File.Delete(backup);
                File.Move(path, backup);

                try
                {
                    File.Move(staged, path);
                }
                catch
                {
                    // Restore original credentials after an ordinary IO error.
                    if (!File.Exists(path) && File.Exists(backup))
                        File.Move(backup, path);
                    throw;
                }

                // The new database has been committed. A stale backup should
                // not retain older password hashes for longer than necessary.
                try
                {
                    if (File.Exists(backup))
                        File.Delete(backup);
                }
                catch
                {
                    // Authentication data is already committed; backup cleanup
                    // failure must not report a false password-change failure.
                }
            }
            finally
            {
                try
                {
                    if (File.Exists(staged))
                        File.Delete(staged);
                }
                catch
                {
                    // A leftover .new file is never considered authoritative.
                }
            }
        }
    }
}
