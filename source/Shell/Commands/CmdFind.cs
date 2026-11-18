using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public sealed class CmdFind : ICommand
    {
        private const int MaxDepth = 16;
        private const int MaxResults = 512;
        private const int MaxVisitedDirectories = 1024;
        private const int MaxPathLength = 512;
        private const int MaxParentSegments = 32;

        public string Name => "find";
        public string Description => "Find files/directories by name with bounded traversal";

        public void Execute(string[] args, ref string currentPath)
        {
            if (!SecurityContext.IsAuthenticated)
            {
                WriteMessage.WriteError("Access denied. Authentication required.", "SEC");
                SecurityLogger.LogEvent("WARN", "Unauthenticated find attempt.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (args.Length < 2 || args.Length > 3)
            {
                WriteMessage.WriteError("Usage: find <name> [start-path]", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string pattern = args[1];
            if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 128)
            {
                WriteMessage.WriteError("Search name must be 1..128 characters.", "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string root;
            if (args.Length > 2)
            {
                if (!TryResolveBoundedPath(currentPath, args[2], out root))
                {
                    WriteMessage.WriteError("Invalid or unbounded search path: " + args[2], "SEC");
                    SecurityLogger.LogEvent("WARN", $"Rejected unbounded find path '{args[2]}' by {SecurityContext.CurrentUser ?? "?"}");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }
            }
            else
            {
                root = string.IsNullOrEmpty(currentPath) ? "/" : currentPath;
            }

            if (!Directory.Exists(root))
            {
                WriteMessage.WriteError("Directory does not exist: " + root, "CMD");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!PermissionManager.CanRead(root, SecurityContext.CurrentUser))
            {
                WriteMessage.WriteError($"Permission denied: Cannot access {root}", "SEC");
                SecurityLogger.LogEvent("WARN", $"Unauthorized find attempt on {root} by {SecurityContext.CurrentUser ?? "?"}");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            try
            {
                int results;
                int visited;
                SearchIterative(root, pattern, out results, out visited);

                if (results == MaxResults)
                    CommandIO.WriteLine("[find] result limit reached (" + MaxResults + ")");
                if (visited == MaxVisitedDirectories)
                    CommandIO.WriteLine("[find] directory scan limit reached (" + MaxVisitedDirectories + ")");

                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                WriteMessage.WriteError("find failed: " + ex.Message, "CMD");
                CommandIO.LastCommandSuccess = false;
            }
        }

        private static void SearchIterative(string root, string pattern, out int results, out int visited)
        {
            // The traversal stack is explicitly bounded by the same directory budget as
            // the scan itself. This avoids consuming the managed call stack on deep or
            // adversarial directory trees while keeping memory use deterministic.
            string[] paths = new string[MaxVisitedDirectories];
            int[] depths = new int[MaxVisitedDirectories];
            int pending = 1;
            paths[0] = root;
            depths[0] = 0;

            results = 0;
            visited = 0;
            string currentUser = SecurityContext.CurrentUser;

            while (pending > 0 && results < MaxResults && visited < MaxVisitedDirectories)
            {
                pending--;
                string path = paths[pending];
                int depth = depths[pending];
                paths[pending] = null;

                if (depth > MaxDepth || string.IsNullOrEmpty(path))
                    continue;

                if (!PermissionManager.CanRead(path, currentUser))
                    continue;

                visited++;

                string[] directories;
                string[] files;
                try
                {
                    directories = Directory.GetDirectories(path);
                    files = Directory.GetFiles(path);
                }
                catch
                {
                    continue;
                }

                string[] readableDirs = new string[directories.Length];
                int readableCount = 0;
                for (int i = 0; i < directories.Length; i++)
                {
                    string name = BaseName(directories[i]);
                    if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOf('/') >= 0)
                        continue;

                    string childDir = JoinChildPath(path, name);
                    if (!PermissionManager.CanRead(childDir, currentUser))
                        continue;

                    readableDirs[readableCount++] = childDir;
                    if (results < MaxResults && Matches(name, pattern))
                    {
                        CommandIO.WriteLine(childDir);
                        results++;
                    }
                }

                for (int i = 0; i < files.Length && results < MaxResults; i++)
                {
                    string name = BaseName(files[i]);
                    if (string.IsNullOrEmpty(name) || name == "." || name == ".." || name.IndexOf('/') >= 0)
                        continue;

                    string childFile = JoinChildPath(path, name);
                    if (!PermissionManager.CanRead(childFile, currentUser))
                        continue;

                    if (Matches(name, pattern))
                    {
                        CommandIO.WriteLine(childFile);
                        results++;
                    }
                }

                if (depth >= MaxDepth)
                    continue;

                // Push in reverse order so traversal remains compatible with the old
                // depth-first recursive implementation for deterministic shell output.
                for (int i = readableCount - 1;
                     i >= 0 && pending < MaxVisitedDirectories;
                     i--)
                {
                    paths[pending] = readableDirs[i];
                    depths[pending] = depth + 1;
                    pending++;
                }
            }
        }

        private static bool TryResolveBoundedPath(string currentPath, string rawTarget, out string resolved)
        {
            resolved = null;
            if (string.IsNullOrWhiteSpace(rawTarget) || rawTarget.Length > MaxPathLength)
                return false;

            string normalizedCurrent = string.IsNullOrEmpty(currentPath) ? "/" : currentPath.Replace('\\', '/');
            string normalizedTarget = rawTarget.Replace('\\', '/').Trim();
            if (normalizedTarget == "/")
            {
                resolved = "/";
                return true;
            }

            bool isAbsolute = normalizedTarget[0] == '/';
            string[] baseParts = isAbsolute
                ? Array.Empty<string>()
                : normalizedCurrent.Split('/', StringSplitOptions.RemoveEmptyEntries);
            string[] targetParts = normalizedTarget.Split('/', StringSplitOptions.RemoveEmptyEntries);

            var stack = new System.Collections.Generic.List<string>(baseParts.Length + targetParts.Length);
            for (int i = 0; i < baseParts.Length; i++)
            {
                if (baseParts[i] != "." && baseParts[i] != "..")
                    stack.Add(baseParts[i]);
            }

            int parentCount = 0;
            for (int i = 0; i < targetParts.Length; i++)
            {
                string part = targetParts[i];
                if (part == ".")
                    continue;

                if (part == "..")
                {
                    parentCount++;
                    if (parentCount > MaxParentSegments || stack.Count == 0)
                        return false;
                    stack.RemoveAt(stack.Count - 1);
                    continue;
                }

                stack.Add(part);
            }

            resolved = stack.Count == 0 ? "/" : "/" + string.Join("/", stack);
            return resolved.Length <= MaxPathLength;
        }

        private static string JoinChildPath(string parentDir, string leafName)
        {
            if (string.IsNullOrEmpty(parentDir) || parentDir == "/")
                return "/" + leafName;
            return parentDir.TrimEnd('/') + "/" + leafName;
        }

        private static bool Matches(string name, string pattern)
        {
            if (pattern == "*")
                return true;

            bool prefix = pattern.EndsWith("*", StringComparison.Ordinal);
            bool suffix = pattern.StartsWith("*", StringComparison.Ordinal);

            if (prefix && suffix && pattern.Length > 2)
                return name.IndexOf(pattern.Substring(1, pattern.Length - 2),
                    StringComparison.OrdinalIgnoreCase) >= 0;

            if (prefix && pattern.Length > 1)
                return name.StartsWith(pattern.Substring(0, pattern.Length - 1),
                    StringComparison.OrdinalIgnoreCase);

            if (suffix && pattern.Length > 1)
                return name.EndsWith(pattern.Substring(1),
                    StringComparison.OrdinalIgnoreCase);

            return string.Equals(name, pattern, StringComparison.OrdinalIgnoreCase);
        }

        private static string BaseName(string path)
        {
            string normalized = (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
            int index = normalized.LastIndexOf('/');
            return index >= 0 ? normalized.Substring(index + 1) : normalized;
        }
    }
}
