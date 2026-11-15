using System;
using System.IO;
using ZonderqOS.SystemCore;

namespace ZonderqOS
{
    public static partial class Disk
    {
        public static void Tree(string path, string indent = "")
        {
            CommandIO.LastCommandSuccess = false;
            try
            {
                TreeCore(path, indent);
                CommandIO.LastCommandSuccess = true;
            }
            catch (Exception ex)
            {
                CommandIO.LastCommandSuccess = false;
                WriteMessage.WriteError($"Error reading structure at {path}: {ex.Message}", "FS");
            }
        }

        // A failed descendant must abort the whole listing, not be hidden by
        // a later successful sibling. Only the outer operation publishes status.
        private static void TreeCore(string path, string indent)
        {
            foreach (string file in Directory.GetFiles(path))
                CommandIO.WriteLine($"{indent}--- {Path.GetFileName(file)}");
            foreach (string dir in Directory.GetDirectories(path))
            {
                CommandIO.WriteLine($"{indent}+-- {Path.GetFileName(dir)}");
                TreeCore(dir, indent + "    ");
            }
        }
    }
}
