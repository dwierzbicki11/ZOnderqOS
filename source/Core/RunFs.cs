using System;
using System.Collections.Generic;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Small bounded RAM-only runtime filesystem. It is intentionally ephemeral:
    /// all entries disappear on reboot and nothing is written to the FAT root.
    /// </summary>
    public static class RunFs
    {
        private sealed class Entry
        {
            public string Path;
            public bool Directory;
            public string Content;
        }

        private const int MaxEntries = 256;
        private const int MaxPathLength = 192;
        private const int MaxFileChars = 64 * 1024;
        private const int MaxTotalChars = 512 * 1024;

        private static readonly object Sync = new object();
        private static readonly List<Entry> Entries = new List<Entry>();
        private static int totalChars;
        private static bool initialized;

        public static bool IsRunPath(string path)
        {
            string normalized = Normalize(path);
            return normalized == "/run" || normalized.StartsWith("/run/", StringComparison.Ordinal);
        }

        public static bool FileExists(string path)
        {
            string normalized = Normalize(path);
            lock (Sync)
            {
                EnsureInitialized();
                Entry entry = Find(normalized);
                return entry != null && !entry.Directory;
            }
        }

        public static bool DirectoryExists(string path)
        {
            string normalized = Normalize(path);
            lock (Sync)
            {
                EnsureInitialized();
                return IsDirectory(normalized);
            }
        }

        public static bool TryRead(string path, out string content)
        {
            string normalized = Normalize(path);
            content = null;

            lock (Sync)
            {
                EnsureInitialized();
                Entry entry = Find(normalized);
                if (entry == null || entry.Directory)
                    return false;

                content = entry.Content ?? string.Empty;
                return true;
            }
        }

        public static bool TryList(string path, out string[] entries)
        {
            string normalized = Normalize(path);
            entries = null;

            lock (Sync)
            {
                EnsureInitialized();
                if (!IsDirectory(normalized))
                    return false;

                var result = new List<string>();
                string prefix = normalized == "/run" ? "/run/" : normalized + "/";

                for (int i = 0; i < Entries.Count; i++)
                {
                    string child = Entries[i].Path;
                    if (!child.StartsWith(prefix, StringComparison.Ordinal))
                        continue;

                    string tail = child.Substring(prefix.Length);
                    if (tail.Length == 0 || tail.IndexOf('/') >= 0)
                        continue;

                    result.Add(tail);
                }

                result.Sort(StringComparer.Ordinal);
                entries = result.ToArray();
                return true;
            }
        }

        public static bool TryWrite(string path, string content, bool append, out string error)
        {
            string normalized = Normalize(path);
            error = null;

            if (!IsValidFilePath(normalized, out error))
                return false;

            string value = content ?? string.Empty;

            lock (Sync)
            {
                EnsureInitialized();

                if (!IsDirectory(ParentOf(normalized)))
                {
                    error = "parent directory does not exist";
                    return false;
                }

                Entry existing = Find(normalized);
                if (existing != null && existing.Directory)
                {
                    error = "path is a directory";
                    return false;
                }

                string newContent = append && existing != null
                    ? (existing.Content ?? string.Empty) + value
                    : value;

                if (newContent.Length > MaxFileChars)
                {
                    error = "file exceeds runtime filesystem limit";
                    return false;
                }

                int oldChars = existing == null || existing.Content == null ? 0 : existing.Content.Length;
                int projected = totalChars - oldChars + newContent.Length;
                if (projected > MaxTotalChars)
                {
                    error = "runtime filesystem memory limit reached";
                    return false;
                }

                if (existing == null)
                {
                    if (Entries.Count >= MaxEntries)
                    {
                        error = "runtime filesystem entry limit reached";
                        return false;
                    }

                    existing = new Entry { Path = normalized, Directory = false, Content = string.Empty };
                    Entries.Add(existing);
                }

                existing.Content = newContent;
                totalChars = projected;
                return true;
            }
        }

        public static bool TryCreateDirectory(string path, out string error)
        {
            string normalized = Normalize(path);
            error = null;

            if (normalized == "/run")
                return true;

            if (!IsValidPath(normalized, out error))
                return false;

            lock (Sync)
            {
                EnsureInitialized();

                if (Find(normalized) != null)
                {
                    error = "path already exists";
                    return false;
                }

                if (!IsDirectory(ParentOf(normalized)))
                {
                    error = "parent directory does not exist";
                    return false;
                }

                if (Entries.Count >= MaxEntries)
                {
                    error = "runtime filesystem entry limit reached";
                    return false;
                }

                Entries.Add(new Entry { Path = normalized, Directory = true, Content = null });
                return true;
            }
        }

        public static bool TryCopyFile(string sourcePath, string destinationPath, out string error)
        {
            string source = Normalize(sourcePath);
            string destination = Normalize(destinationPath);
            error = null;

            lock (Sync)
            {
                EnsureInitialized();
                Entry sourceEntry = Find(source);
                if (sourceEntry == null || sourceEntry.Directory)
                {
                    error = "source file does not exist";
                    return false;
                }

                if (Find(destination) != null)
                {
                    error = "destination already exists";
                    return false;
                }

                if (!IsDirectory(ParentOf(destination)))
                {
                    error = "destination parent directory does not exist";
                    return false;
                }

                if (Entries.Count >= MaxEntries)
                {
                    error = "runtime filesystem entry limit reached";
                    return false;
                }

                string value = sourceEntry.Content ?? string.Empty;
                if (totalChars + value.Length > MaxTotalChars)
                {
                    error = "runtime filesystem memory limit reached";
                    return false;
                }

                Entries.Add(new Entry { Path = destination, Directory = false, Content = value });
                totalChars += value.Length;
                return true;
            }
        }

        public static bool TryMoveFile(string sourcePath, string destinationPath, out string error)
        {
            string source = Normalize(sourcePath);
            string destination = Normalize(destinationPath);
            error = null;

            lock (Sync)
            {
                EnsureInitialized();
                Entry sourceEntry = Find(source);
                if (sourceEntry == null || sourceEntry.Directory)
                {
                    error = "source file does not exist";
                    return false;
                }

                if (Find(destination) != null)
                {
                    error = "destination already exists";
                    return false;
                }

                if (!IsDirectory(ParentOf(destination)))
                {
                    error = "destination parent directory does not exist";
                    return false;
                }

                sourceEntry.Path = destination;
                return true;
            }
        }

        public static bool TryDeleteFile(string path, out string error)
        {
            string normalized = Normalize(path);
            error = null;

            lock (Sync)
            {
                EnsureInitialized();
                Entry entry = Find(normalized);
                if (entry == null || entry.Directory)
                {
                    error = "file does not exist";
                    return false;
                }

                totalChars -= entry.Content == null ? 0 : entry.Content.Length;
                Entries.Remove(entry);
                return true;
            }
        }

        public static bool TryDeleteDirectory(string path, bool recursive, out string error)
        {
            string normalized = Normalize(path);
            error = null;

            if (normalized == "/run")
            {
                error = "cannot remove runtime filesystem root";
                return false;
            }

            lock (Sync)
            {
                EnsureInitialized();
                Entry entry = Find(normalized);
                if (entry == null || !entry.Directory)
                {
                    error = "directory does not exist";
                    return false;
                }

                string prefix = normalized + "/";
                bool hasChildren = false;
                for (int i = 0; i < Entries.Count; i++)
                {
                    if (Entries[i].Path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        hasChildren = true;
                        break;
                    }
                }

                if (hasChildren && !recursive)
                {
                    error = "directory is not empty";
                    return false;
                }

                for (int i = Entries.Count - 1; i >= 0; i--)
                {
                    Entry current = Entries[i];
                    if (current.Path == normalized || current.Path.StartsWith(prefix, StringComparison.Ordinal))
                    {
                        if (!current.Directory && current.Content != null)
                            totalChars -= current.Content.Length;
                        Entries.RemoveAt(i);
                    }
                }

                return true;
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized)
                return;

            initialized = true;
            Entries.Add(new Entry { Path = "/run/lock", Directory = true });
            Entries.Add(new Entry { Path = "/run/services", Directory = true });
            Entries.Add(new Entry { Path = "/run/user", Directory = true });
        }

        private static Entry Find(string normalized)
        {
            for (int i = 0; i < Entries.Count; i++)
            {
                if (string.Equals(Entries[i].Path, normalized, StringComparison.Ordinal))
                    return Entries[i];
            }
            return null;
        }

        private static bool IsDirectory(string normalized)
        {
            if (normalized == "/run")
                return true;

            Entry entry = Find(normalized);
            return entry != null && entry.Directory;
        }

        private static bool IsValidFilePath(string path, out string error)
        {
            if (!IsValidPath(path, out error))
                return false;
            if (path == "/run")
            {
                error = "path is a directory";
                return false;
            }
            return true;
        }

        private static bool IsValidPath(string path, out string error)
        {
            error = null;
            if (!IsRunPath(path))
            {
                error = "not a runtime filesystem path";
                return false;
            }
            if (path.Length > MaxPathLength)
            {
                error = "path too long";
                return false;
            }
            if (path.Contains("/../", StringComparison.Ordinal) ||
                path.EndsWith("/..", StringComparison.Ordinal) ||
                path.Contains("/./", StringComparison.Ordinal) ||
                path.EndsWith("/.", StringComparison.Ordinal))
            {
                error = "relative path components are not allowed";
                return false;
            }
            return true;
        }

        private static string ParentOf(string path)
        {
            int slash = path.LastIndexOf('/');
            return slash <= 0 ? "/" : path.Substring(0, slash);
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return "/";

            string value = path.Replace('\\', '/').Trim();
            while (value.Contains("//", StringComparison.Ordinal))
                value = value.Replace("//", "/", StringComparison.Ordinal);
            while (value.Length > 1 && value.EndsWith("/", StringComparison.Ordinal))
                value = value.Substring(0, value.Length - 1);
            return value;
        }
    }
}
