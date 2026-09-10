using System;
using System.IO;

namespace ZonderqOS.GUI
{
    public static class FileClipboard
    {
        private static string sourcePath;
        private static bool sourceIsDirectory;
        private static bool cutMode;

        public static bool HasItem
        {
            get { return !string.IsNullOrEmpty(sourcePath); }
        }

        public static bool IsCut
        {
            get { return cutMode; }
        }

        public static string SourcePath
        {
            get { return sourcePath; }
        }

        public static string DisplayName
        {
            get
            {
                if (string.IsNullOrEmpty(sourcePath))
                    return "";

                string trimmed = sourcePath.TrimEnd('/', '\\');
                string name = Path.GetFileName(trimmed);
                return string.IsNullOrEmpty(name) ? trimmed : name;
            }
        }

        public static void Set(string path, bool isDirectory, bool cut)
        {
            if (string.IsNullOrEmpty(path))
                return;

            sourcePath = Normalize(path);
            sourceIsDirectory = isDirectory;
            cutMode = cut;
        }

        public static void Clear()
        {
            sourcePath = null;
            sourceIsDirectory = false;
            cutMode = false;
        }

        public static bool TryPaste(string destinationDirectory, out string destinationPath, out string error)
        {
            destinationPath = null;
            error = null;

            if (!HasItem)
            {
                error = "Clipboard is empty";
                return false;
            }

            string source = Normalize(sourcePath);
            string destinationRoot = Normalize(destinationDirectory);

            if (!Directory.Exists(destinationRoot))
            {
                error = "Destination does not exist";
                return false;
            }

            bool sourceExists = sourceIsDirectory ? Directory.Exists(source) : File.Exists(source);
            if (!sourceExists)
            {
                Clear();
                error = "Clipboard source no longer exists";
                return false;
            }

            string name = DisplayName;
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
                    Clear();
                }
                else
                {
                    CopyItem(source, destinationPath, sourceIsDirectory);
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

        private static void CopyItem(string source, string destination, bool directory)
        {
            if (!directory)
            {
                File.Copy(source, destination);
                return;
            }

            CopyDirectory(source, destination);
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
                // Moving across mounted filesystems may not be supported directly.
                // Fall back to copy + delete while keeping the source until copy succeeds.
            }

            CopyItem(source, destination, directory);

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

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            string[] files = Directory.GetFiles(source);
            for (int i = 0; i < files.Length; i++)
            {
                string file = files[i];
                string target = Path.Combine(destination, Path.GetFileName(file));
                File.Copy(file, target);
            }

            string[] directories = Directory.GetDirectories(source);
            for (int i = 0; i < directories.Length; i++)
            {
                string directory = directories[i];
                string target = Path.Combine(destination, Path.GetFileName(directory.TrimEnd('/', '\\')));
                CopyDirectory(directory, target);
            }
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

        private static string Normalize(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            string normalized = path.Replace('\\', '/');
            while (normalized.Length > 1 && normalized.EndsWith("/", StringComparison.Ordinal))
                normalized = normalized.Substring(0, normalized.Length - 1);
            return normalized;
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
        }
    }
}
