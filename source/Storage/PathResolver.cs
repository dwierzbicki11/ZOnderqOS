using System;
using System.Collections.Generic;

namespace ZonderqOS
{
    public static class PathResolver
    {
        public static string GetAbsolutePath(string currentPath, string targetPath)
        {
            string rawPath = targetPath.StartsWith("/") ? targetPath : currentPath + "/" + targetPath;
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
