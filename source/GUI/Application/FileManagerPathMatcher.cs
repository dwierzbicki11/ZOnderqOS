using System;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Exact, ordinal directory-prefix matching for the file-manager sidebar.
    /// Avoids allocating entryPath + "/" for every sidebar item every frame.
    /// </summary>
    internal static class FileManagerPathMatcher
    {
        internal static bool IsActive(string currentPath, string entryPath)
        {
            if (string.IsNullOrEmpty(entryPath))
                return false;
            if (entryPath == "/")
                return currentPath == "/";
            if (currentPath == entryPath)
                return true;

            // The separator check prevents /mnt/data from matching /mnt/database.
            return currentPath != null &&
                currentPath.Length > entryPath.Length &&
                currentPath[entryPath.Length] == '/' &&
                currentPath.StartsWith(entryPath, StringComparison.Ordinal);
        }
    }
}
