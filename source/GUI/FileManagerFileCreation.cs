using System.IO;

namespace ZonderqOS.GUI
{
    /// <summary>
    /// Creates a new, empty file without ever truncating an existing entry.
    /// A directory listing or File.Exists check cannot close the race between
    /// the GUI's collision preflight and the actual FAT32 write.
    /// </summary>
    public static class FileManagerFileCreation
    {
        public static void CreateEmptyFile(string path)
        {
            // CreateNew is an exclusive filesystem operation. File.WriteAllText
            // uses Create and can destroy an existing document after a race.
            using (FileStream stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write))
            {
                // The file is intentionally empty until opened in an editor.
            }
        }
    }
}
