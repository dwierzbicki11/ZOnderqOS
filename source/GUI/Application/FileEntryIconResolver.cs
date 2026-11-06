using System;
using ZonderqOS.GUI.Icons;

namespace ZonderqOS.GUI.Apps
{
    /// <summary>
    /// Resolve a directory entry icon once when the directory snapshot is built.
    /// The file-manager paint loop must not allocate extension/lowercase strings
    /// for every visible tile on every frame.
    /// </summary>
    internal static class FileEntryIconResolver
    {
        internal static IconType Resolve(string name, bool isDirectory)
        {
            if (isDirectory)
                return IconType.Folder;
            if (string.IsNullOrEmpty(name))
                return IconType.File;

            // Only the final 3, 4 or 5 characters can be a recognized
            // extension. Check their dot positions in O(1) instead of scanning
            // a potentially long path with LastIndexOf or trying all suffixes.
            // The EndsWith checks retain the original OrdinalIgnoreCase rules.
            int length = name.Length;
            int suffixLength = length >= 3 && name[length - 3] == '.' ? 3
                : length >= 4 && name[length - 4] == '.' ? 4
                : length >= 5 && name[length - 5] == '.' ? 5 : 0;
            switch (suffixLength)
            {
                case 3:
                    if (name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                        return IconType.FileTxt;
                    break;
                case 4:
                    if (name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) return IconType.FileBmp;
                    if (name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)) return IconType.FileDoc;
                    if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)) return IconType.FileJpg;
                    if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return IconType.FilePdf;
                    if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return IconType.FilePng;
                    if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return IconType.FileSvg;
                    if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase))
                        return IconType.FileTxt;
                    break;
                case 5:
                    if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return IconType.FileDocx;
                    if (name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return IconType.FileJpg;
                    if (name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase)) return IconType.FileTxt;
                    break;
            }
            return IconType.File;
        }
    }
}
