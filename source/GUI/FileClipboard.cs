using System;
using System.IO;

namespace ZonderqOS.GUI
{
    public static class FileClipboard
    {
        private const int MaxTraversalDepth = 32;
        private const int MaxTreeEntries = 4096;
        private const ulong MaxCopyBytes = 512UL * 1024UL * 1024UL;

        private static readonly object Sync = new object();

        private static string sourcePath;
        private static bool sourceIsDirectory;
        private static bool cutMode;

        public static bool HasItem
        {
            get
            {
                lock (Sync)
                    return !string.IsNullOrEmpty(sourcePath);
            }
        }

        public static bool IsCut
        {
            get
            {
                lock (Sync)
                    return cutMode;
            }
        }

        public static string SourcePath
        {
            get
            {
                lock (Sync)
                    return sourcePath;
            }
        }

        public static string DisplayName
        {
            get
            {
                lock (Sync)
                    return GetDisplayNameLocked();
            }
        }

        public static void Set(string path, bool isDirectory, bool cut)
        {
            if (string.IsNullOrEmpty(path))
                return;

            string normalized = Normalize(path);
            if (ContainsTraversalSegment(normalized))
                return;

            lock (Sync)
            {
                sourcePath = normalized;
                sourceIsDirectory = isDirectory;
                cutMode = cut;
            }
        }

        public static void Clear()
        {
            lock (Sync)
                ClearLocked();
        }

        public static bool TryPaste(string destinationDirectory, out string destinationPath, out string error)
        {
            lock (Sync)
            {
                destinationPath = null;
                error = null;

                if (string.IsNullOrEmpty(sourcePath))
                {
                    error = "Clipboard is empty";
                    return false;
                }

                string source = Normalize(sourcePath);
                string destinationRoot = Normalize(destinationDirectory);
                string user = SecurityContext.CurrentUser ?? "root";

                if (ContainsTraversalSegment(source) || ContainsTraversalSegment(destinationRoot))
                {
                    error = "Unsafe clipboard path";
                    SecurityLogger.LogEvent("WARN", "Clipboard traversal path rejected for " + user + ".");
                    return false;
                }

                if (!Directory.Exists(destinationRoot))
                {
                    error = "Destination does not exist";
                    return false;
                }

                if (user != "root")
                {
                    string home = Normalize(UserManager.GetHomeDirectory(user));
                    if (string.IsNullOrEmpty(home) || ContainsTraversalSegment(home) ||
                        !IsInside(destinationRoot, home))
                    {
                        error = "Permission denied: paste outside user home";
                        SecurityLogger.LogEvent("WARN",
                            "Unauthorized clipboard destination by " + user + ": " + destinationRoot);
                        return false;
                    }

                    if (cutMode && sourceIsDirectory && !IsInside(source, home))
                    {
                        error = "Permission denied: cannot move system directory";
                        SecurityLogger.LogEvent("WARN",
                            "Unauthorized directory move by " + user + ": " + source);
                        return false;
                    }
                }

                bool sourceExists = sourceIsDirectory ? Directory.Exists(source) : File.Exists(source);
                if (!sourceExists)
                {
                    ClearLocked();
                    error = "Clipboard source no longer exists";
                    return false;
                }

                int entries = 0;
                ulong bytes = 0;
                string deniedPath;
                string validationError;
                if (!ValidateSourceTree(
                        source,
                        sourceIsDirectory,
                        cutMode,
                        user,
                        0,
                        ref entries,
                        ref bytes,
                        out deniedPath,
                        out validationError))
                {
                    if (!string.IsNullOrEmpty(deniedPath))
                    {
                        error = "Permission denied: " + deniedPath;
                        SecurityLogger.LogEvent("WARN",
                            "Unauthorized clipboard access by " + user + ": " + deniedPath);
                    }
                    else
                    {
                        error = validationError;
                    }

                    return false;
                }

                string name = GetDisplayNameLocked();
                if (string.IsNullOrEmpty(name))
                {
                    error = "Invalid clipboard item";
                    return false;
                }

                if (sourceIsDirectory && IsInside(destinationRoot, source))
                {
                    error = "Cannot paste a folder inside itself";
                    return false;
                }

                string requestedPath = Normalize(Path.Combine(destinationRoot, name));
                if (ContainsTraversalSegment(requestedPath))
                {
                    error = "Unsafe destination path";
                    return false;
                }

                if (cutMode && string.Equals(requestedPath, source, StringComparison.Ordinal))
                {
                    error = "Item is already in this location";
                    return false;
                }

                destinationPath = FindFreeDestination(requestedPath, sourceIsDirectory);
                if (string.IsNullOrEmpty(destinationPath))
                {
                    error = "Could not create a unique destination name";
                    return false;
                }

                try
                {
                    if (cutMode)
                    {
                        MoveItem(source, destinationPath, sourceIsDirectory);
                        PermissionManager.MovePermissionsUnder(source, destinationPath);
                        ClearLocked();
                    }
                    else
                    {
                        int copiedEntries = 0;
                        ulong copiedBytes = 0;
                        CopyItem(
                            source,
                            destinationPath,
                            sourceIsDirectory,
                            0,
                            ref copiedEntries,
                            ref copiedBytes);
                        PermissionManager.CopyPermissionsUnder(source, destinationPath, user);
                    }

                    return true;
                }
                catch (Exception ex)
                {
                    TryRemoveDestination(destinationPath, sourceIsDirectory);
                    destinationPath = null;
                    error = ex.Message;
                    return false;
                }
            }
        }

        private static bool ValidateSourceTree(
            string source,
            bool directory,
            bool requireWrite,
            string user,
            int depth,
            ref int entries,
            ref ulong bytes,
            out string deniedPath,
            out string error)
        {
            deniedPath = null;
            error = null;

            if (depth > MaxTraversalDepth)
            {
                error = "Clipboard tree exceeds maximum depth (" + MaxTraversalDepth + ")";
                return false;
            }

            if (!directory)
            {
                entries++;
                if (entries > MaxTreeEntries)
                {
                    error = "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ")";
                    return false;
                }

                if (!PermissionManager.CanRead(source, user) ||
                    (requireWrite && !PermissionManager.CanWrite(source, user)))
                {
                    deniedPath = source;
                    return false;
                }

                long length;
                try
                {
                    length = new FileInfo(source).Length;
                }
                catch
                {
                    error = "Cannot inspect clipboard source: " + source;
                    return false;
                }

                if (length < 0)
                {
                    error = "Clipboard source has invalid length";
                    return false;
                }

                ulong fileBytes = (ulong)length;
                if (fileBytes > MaxCopyBytes || bytes > MaxCopyBytes - fileBytes)
                {
                    error = "Clipboard payload exceeds " + (MaxCopyBytes / (1024UL * 1024UL)) + " MiB limit";
                    return false;
                }

                bytes += fileBytes;
                return true;
            }

            entries++;
            if (entries > MaxTreeEntries)
            {
                error = "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ")";
                return false;
            }

            try
            {
                string[] files = Directory.GetFiles(source);
                string[] directories = Directory.GetDirectories(source);

                if (files.Length + directories.Length > MaxTreeEntries - entries)
                {
                    error = "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ")";
                    return false;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    string file = Normalize(files[i]);
                    if (!ValidateSourceTree(
                            file,
                            false,
                            requireWrite,
                            user,
                            depth + 1,
                            ref entries,
                            ref bytes,
                            out deniedPath,
                            out error))
                        return false;
                }

                for (int i = 0; i < directories.Length; i++)
                {
                    string child = Normalize(directories[i]);
                    if (!ValidateSourceTree(
                            child,
                            true,
                            requireWrite,
                            user,
                            depth + 1,
                            ref entries,
                            ref bytes,
                            out deniedPath,
                            out error))
                        return false;
                }

                return true;
            }
            catch
            {
                deniedPath = source;
                return false;
            }
        }

        private static void CopyItem(
            string source,
            string destination,
            bool directory,
            int depth,
            ref int entries,
            ref ulong bytes)
        {
            if (!directory)
            {
                CopyFileBounded(source, destination, ref entries, ref bytes);
                return;
            }

            CopyDirectory(source, destination, depth, ref entries, ref bytes);
        }

        private static void MoveItem(string source, string destination, bool directory)
        {
            try
            {
                if (directory)
                    Directory.Move(source, destination);
                else
                    File.Move(source, destination);
                return;
            }
            catch
            {
                // Cross-filesystem rename may be unavailable. Copy is bounded and
                // source is kept intact until the copy finishes successfully.
            }

            int copiedEntries = 0;
            ulong copiedBytes = 0;
            CopyItem(source, destination, directory, 0, ref copiedEntries, ref copiedBytes);

            try
            {
                if (directory)
                    Directory.Delete(source, true);
                else
                    File.Delete(source);
            }
            catch
            {
                TryRemoveDestination(destination, directory);
                throw;
            }
        }

        private static void CopyDirectory(
            string source,
            string destination,
            int depth,
            ref int entries,
            ref ulong bytes)
        {
            if (depth > MaxTraversalDepth)
                throw new InvalidOperationException(
                    "Clipboard tree exceeds maximum depth (" + MaxTraversalDepth + ").");

            entries++;
            if (entries > MaxTreeEntries)
                throw new InvalidOperationException(
                    "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ").");

            Directory.CreateDirectory(destination);

            string[] files = Directory.GetFiles(source);
            string[] directories = Directory.GetDirectories(source);

            if (files.Length + directories.Length > MaxTreeEntries - entries)
                throw new InvalidOperationException(
                    "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ").");

            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string target = Path.Combine(destination, Path.GetFileName(file));
                CopyFileBounded(file, target, ref entries, ref bytes);
            }

            for (int i = 0; i < directories.Length; i++)
            {
                string directory = directories[i];
                string target = Path.Combine(
                    destination,
                    Path.GetFileName(directory.TrimEnd('/', '\\')));
                CopyDirectory(directory, target, depth + 1, ref entries, ref bytes);
            }
        }

        private static void CopyFileBounded(
            string source,
            string destination,
            ref int entries,
            ref ulong bytes)
        {
            entries++;
            if (entries > MaxTreeEntries)
                throw new InvalidOperationException(
                    "Clipboard tree exceeds entry limit (" + MaxTreeEntries + ").");

            long length = new FileInfo(source).Length;
            if (length < 0)
                throw new InvalidOperationException("Clipboard source has invalid length.");

            ulong fileBytes = (ulong)length;
            if (fileBytes > MaxCopyBytes || bytes > MaxCopyBytes - fileBytes)
                throw new InvalidOperationException(
                    "Clipboard payload exceeds " +
                    (MaxCopyBytes / (1024UL * 1024UL)) +
                    " MiB limit.");

            File.Copy(source, destination);
            bytes += fileBytes;
        }

        private static string FindFreeDestination(string requestedPath, bool directory)
        {
            if (!Exists(requestedPath))
                return requestedPath;

            string parent = Path.GetDirectoryName(requestedPath);
            if (string.IsNullOrEmpty(parent))
                parent = "/";

            string name = Path.GetFileName(requestedPath.TrimEnd('/', '\\'));
            string baseName = name;
            string extension = "";

            if (!directory)
            {
                extension = Path.GetExtension(name);
                if (!string.IsNullOrEmpty(extension) && name.Length > extension.Length)
                    baseName = name.Substring(0, name.Length - extension.Length);
            }

            for (int i = 1; i <= 99; i++)
            {
                string suffix = i == 1 ? " - Copy" : " - Copy " + i;
                string candidate = Normalize(Path.Combine(parent, baseName + suffix + extension));
                if (!Exists(candidate))
                    return candidate;
            }

            return null;
        }

        private static bool Exists(string path)
        {
            return File.Exists(path) || Directory.Exists(path);
        }

        private static bool IsInside(string path, string root)
        {
            if (string.Equals(path, root, StringComparison.Ordinal))
                return true;

            if (root == "/")
                return path.StartsWith("/", StringComparison.Ordinal);

            return path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        private static bool ContainsTraversalSegment(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            int start = 0;
            for (int i = 0; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '/')
                    continue;

                int length = i - start;
                if (length == 2 &&
                    path[start] == '.' &&
                    path[start + 1] == '.')
                    return true;

                start = i + 1;
            }

            return false;
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string normalized = path.Replace('\\', '/');
            while (normalized.Contains("//", StringComparison.Ordinal))
                normalized = normalized.Replace("//", "/", StringComparison.Ordinal);
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
        }

        private static string GetDisplayNameLocked()
        {
            if (string.IsNullOrEmpty(sourcePath))
                return "";

            string trimmed = sourcePath.TrimEnd('/', '\\');
            string name = Path.GetFileName(trimmed);
            return string.IsNullOrEmpty(name) ? trimmed : name;
        }

        private static void ClearLocked()
        {
            sourcePath = null;
            sourceIsDirectory = false;
            cutMode = false;
        }

        private static void TryRemoveDestination(string destination, bool directory)
        {
            if (string.IsNullOrEmpty(destination))
                return;

            try
            {
                if (directory && Directory.Exists(destination))
                    Directory.Delete(destination, true);
                else if (!directory && File.Exists(destination))
                    File.Delete(destination);
            }
            catch
            {
            }

            PermissionManager.RemovePermissionsUnder(destination);
        }
    }
}
