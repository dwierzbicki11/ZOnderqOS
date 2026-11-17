using System;
using System.Collections.Generic;

namespace ZonderqOS
{
    public static class PathResolver
    {
        public static string GetAbsolutePath(string currentPath, string targetPath)
        {
            string normalizedCurrent = string.IsNullOrEmpty(currentPath) ? "/" : currentPath.Replace('\\', '/');
            if (string.IsNullOrEmpty(targetPath))
                return normalizedCurrent;

            string normalizedTarget = targetPath.Replace('\\', '/');
            if (normalizedTarget == ".")
                return normalizedCurrent;
            if (normalizedTarget == "/")
                return "/";

            string rawPath = normalizedTarget.StartsWith("/", StringComparison.Ordinal)
                ? normalizedTarget
                : normalizedCurrent + "/" + normalizedTarget;
            string[] parts = rawPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            var resolvedParts = new List<string>();

            foreach (string part in parts)
            {
                if (part == ".") continue;
                else if (part == "..") { if (resolvedParts.Count > 0) resolvedParts.RemoveAt(resolvedParts.Count - 1); }
                else resolvedParts.Add(part);
            }

            string finalPath = "/" + string.Join("/", resolvedParts);
            return string.IsNullOrEmpty(finalPath) ? "/" : finalPath;
        }
    }
}
