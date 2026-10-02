using System;
using System.IO;

namespace ZonderqOS.Commands
{
    public sealed class CmdFind : ICommand
    {
        private const int MaxDepth = 16;
        private const int MaxResults = 512;
        private const int MaxVisitedDirectories = 1024;

        public string Name => "find";
        public string Description => "Find files/directories by name with bounded traversal";

        public void Execute(string[] args, ref string currentPath)
        {
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

            string root = args.Length > 2
                ? PathResolver.GetAbsolutePath(currentPath, args[2])
                : currentPath;

            if (!Directory.Exists(root))
            {
                WriteMessage.WriteError("Directory does not exist: " + root, "CMD");
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

            while (pending > 0 && results < MaxResults && visited < MaxVisitedDirectories)
            {
                pending--;
                string path = paths[pending];
                int depth = depths[pending];
                paths[pending] = null;

                if (depth > MaxDepth)
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

                for (int i = 0; i < directories.Length && results < MaxResults; i++)
                {
                    string name = BaseName(directories[i]);
                    if (Matches(name, pattern))
                    {
                        CommandIO.WriteLine(directories[i]);
                        results++;
                    }
                }

                for (int i = 0; i < files.Length && results < MaxResults; i++)
                {
                    if (!PermissionManager.CanRead(files[i], SecurityContext.CurrentUser))
                        continue;

                    string name = BaseName(files[i]);
                    if (Matches(name, pattern))
                    {
                        CommandIO.WriteLine(files[i]);
                        results++;
                    }
                }

                if (depth >= MaxDepth)
                    continue;

                // Push in reverse order so traversal remains compatible with the old
                // depth-first recursive implementation for deterministic shell output.
                for (int i = directories.Length - 1;
                     i >= 0 && pending < MaxVisitedDirectories;
                     i--)
                {
                    paths[pending] = directories[i];
                    depths[pending] = depth + 1;
                    pending++;
                }
            }
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
