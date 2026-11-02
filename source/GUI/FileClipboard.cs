using System;
using System.IO;

using ZonderqOS.SystemCore;
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

            // Clipboard operations are GUI user actions, never boot-time
            // root services. A missing/expired session must fail closed
            // instead of silently borrowing root permissions.
            if (!SecurityContext.IsAuthenticated ||
                string.IsNullOrEmpty(SecurityContext.CurrentUser))
            {
                error = "Authentication required for clipboard paste";
                return false;
            }

            if (!HasItem)
            {
                error = "Clipboard is empty";
                return false;
            }

            string source = Normalize(sourcePath);
            string destinationRoot = Normalize(destinationDirectory);
            string user = SecurityContext.CurrentUser;

            // Never rely on a textual home-prefix check for paths containing
            // dot segments. /home/alice/../etc is outside alice's home even
            // though it begins with "/home/alice/".
            if (!IsSafeAbsolutePath(source) || !IsSafeAbsolutePath(destinationRoot))
            {
                error = "Invalid clipboard path";
                return false;
            }

            if (VirtualFs.IsReadOnlyPath(source) || VirtualFs.IsReadOnlyPath(destinationRoot))
            {
                error = "Read-only virtual filesystem";
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
                if (!IsSafeAbsolutePath(home) || !IsInside(destinationRoot, home))
                {
                    error = "Permission denied: paste outside user home";
                    SecurityLogger.LogEvent("WARN", $"Unauthorized clipboard destination by {user}: {destinationRoot}");
                    return false;
                }

                // Home containment alone is insufficient: the custom ACL
                // may mark a nested destination read-only even though the
                // underlying FAT driver does not enforce Unix permissions.
                if (!PermissionManager.CanWrite(destinationRoot, user))
                {
                    error = "Permission denied: destination is not writable";
                    SecurityLogger.LogEvent("WARN", $"Unauthorized clipboard destination write by {user}: {destinationRoot}");
                    return false;
                }

                if (cutMode && sourceIsDirectory && !IsInside(source, home))
                {
                    error = "Permission denied: cannot move system directory";
                    SecurityLogger.LogEvent("WARN", $"Unauthorized directory move by {user}: {source}");
                    return false;
                }
            }

            bool sourceExists = sourceIsDirectory ? Directory.Exists(source) : File.Exists(source);
            if (!sourceExists)
            {
                Clear();
                error = "Clipboard source no longer exists";
                return false;
            }

            if (!HasSourceAccess(source, sourceIsDirectory, cutMode, user, out string deniedPath))
            {
                error = "Permission denied: " + deniedPath;
                SecurityLogger.LogEvent("WARN", $"Unauthorized clipboard access by {user}: {deniedPath}");
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

            // A cross-volume cut may copy successfully and then fail while
            // removing its source. Once removal begins, deleting the complete
            // destination in the catch block would lose the user's files.
            // A successful rename also commits the destination before
            // permission metadata is updated.
            bool preserveDestination = false;
            try
            {
                if (cutMode)
                {
                    MoveItem(source, destinationPath, sourceIsDirectory, ref preserveDestination);
                    PermissionManager.MovePermissionsUnder(source, destinationPath);
                    Clear();
                }
                else
                {
                    CopyItem(source, destinationPath, sourceIsDirectory);
                    PermissionManager.CopyPermissionsUnder(source, destinationPath, user);
                }

                return true;
            }
            catch (Exception ex)
            {
                if (preserveDestination)
                {
                    // The source may be intact, partially removed, or already
                    // renamed. Retain the complete destination in every case.
                    // Keep the cut clipboard only if the source still exists.
                    if (!Exists(source))
                        Clear();
                    error = "Move incomplete; destination retained: " + ex.Message;
                }
                else
                {
                    // A failed copy (or copy phase of a cross-volume move)
                    // has not touched the source; discard its partial target.
                    TryRemoveDestination(destinationPath, sourceIsDirectory);
                    destinationPath = null;
                    error = ex.Message;
                }
                return false;
            }
        }

        private static bool HasSourceAccess(string source, bool directory, bool requireWrite, string user, out string deniedPath)
        {
            deniedPath = null;

            if (!directory)
            {
                if (!PermissionManager.CanRead(source, user) ||
                    (requireWrite && !PermissionManager.CanWrite(source, user)))
                {
                    deniedPath = source;
                    return false;
                }

                return true;
            }

            try
            {
                // Directory metadata itself is protected by the custom ACL;
                // checking only its descendants could expose a denied folder.
                if (!PermissionManager.CanRead(source, user) ||
                    (requireWrite && !PermissionManager.CanWrite(source, user)))
                {
                    deniedPath = source;
                    return false;
                }

                string[] files = Directory.GetFiles(source);
                for (int i = 0; i < files.Length; i++)
                {
                    string file = Normalize(files[i]);
                    if (!PermissionManager.CanRead(file, user) ||
                        (requireWrite && !PermissionManager.CanWrite(file, user)))
                    {
                        deniedPath = file;
                        return false;
                    }
                }

                string[] directories = Directory.GetDirectories(source);
                for (int i = 0; i < directories.Length; i++)
                {
                    if (!HasSourceAccess(Normalize(directories[i]), true, requireWrite, user, out deniedPath))
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

        private static void CopyItem(string source, string destination, bool directory)
        {
            if (!directory)
            {
                File.Copy(source, destination);
                return;
            }

            CopyDirectory(source, destination);
        }

        private static void MoveItem(string source, string destination, bool directory,
            ref bool preserveDestination)
        {
            try
            {
                if (directory)
                    Directory.Move(source, destination);
                else
                    File.Move(source, destination);
                // A successful rename cannot be rolled back by deleting the
                // target if the later permission metadata update fails.
                preserveDestination = true;
                return;
            }
            catch
            {
                // Moving across mounted filesystems may not be supported directly.
                // Fall back to copy + delete while keeping the source until copy succeeds.
            }

            CopyItem(source, destination, directory);
            // The destination is now a complete recovery copy. Source deletion
            // can fail partway through a directory; NEVER delete both copies.
            preserveDestination = true;
            if (directory)
                Directory.Delete(source, true);
            else
                File.Delete(source);
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

        // A small lexical check is used instead of Path.GetFullPath because
        // the Cosmos guest filesystem has different mount semantics from the
        // host OS. All GUI clipboard paths must be absolute and must not
        // contain "." or ".." components.
        internal static bool IsSafeAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path[0] != '/')
                return false;

            int start = 1;
            for (int i = 1; i <= path.Length; i++)
            {
                if (i < path.Length && path[i] != '/')
                    continue;

                int length = i - start;
                if ((length == 1 && path[start] == '.') ||
                    (length == 2 && path[start] == '.' && path[start + 1] == '.'))
                    return false;
                start = i + 1;
            }

            return true;
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

            // Cleanup of a failed copy must not throw a second exception
            // that escapes the GUI paste handler and hides the original error.
            try
            {
                PermissionManager.RemovePermissionsUnder(destination);
            }
            catch
            {
            }
        }
    }
}
