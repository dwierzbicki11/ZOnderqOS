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

            // OrdinalIgnoreCase preserves the old extension switch for the
            // supported ASCII suffixes without allocating Path.GetExtension()
            // and ToLowerInvariant() results. The suffix includes its dot.
            if (name.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)) return IconType.FileBmp;
            if (name.EndsWith(".doc", StringComparison.OrdinalIgnoreCase)) return IconType.FileDoc;
            if (name.EndsWith(".docx", StringComparison.OrdinalIgnoreCase)) return IconType.FileDocx;
            if (name.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)) return IconType.FileJpg;
            if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return IconType.FilePdf;
            if (name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return IconType.FilePng;
            if (name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)) return IconType.FileSvg;
            if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".cfg", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".conf", StringComparison.OrdinalIgnoreCase) ||
                name.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return IconType.FileTxt;
            return IconType.File;
        }
    }
}
