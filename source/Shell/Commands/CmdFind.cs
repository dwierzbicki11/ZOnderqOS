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
        public string Description => "Find files/directories by name with bounded recursion";

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

            int results = 0;
            int visited = 0;

            try
            {
                Search(root, pattern, 0, ref results, ref visited);
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

        private static void Search(
            string path,
            string pattern,
            int depth,
            ref int results,
            ref int visited)
        {
            if (depth > MaxDepth || results >= MaxResults || visited >= MaxVisitedDirectories)
                return;

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
                return;
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

            for (int i = 0;
                 i < directories.Length &&
                 results < MaxResults &&
                 visited < MaxVisitedDirectories;
                 i++)
            {
                Search(directories[i], pattern, depth + 1, ref results, ref visited);
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
