using System;

namespace ZonderqOS.GUI
{
    // Shared with the host-only smoke test: keep the FAT32 filename policy
    // independent of Cosmos graphics, storage and user-session APIs.
    public static class FileManagerNamePolicy
    {
        public static bool IsSafeNewEntryName(string name)
        {
            // VFAT long names have a 255 UTF-16 code-unit limit. Do not
            // attempt a partially written directory entry on an overlong name.
            if (string.IsNullOrEmpty(name) || name.Length > 255 ||
                name == "." || name == ".." ||
                (name[name.Length - 1] == '.' || name[name.Length - 1] == ' '))
                return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                // C1/DEL controls are invisible in the file manager and can
                // make names impossible to select or distinguish reliably.
                if (char.IsControl(c) || c == '/' || c == '\\' || c == ':' ||
                    c == '*' || c == '?' || c == '"' || c == '<' ||
                    c == '>' || c == '|')
                    return false;

                // Reject malformed UTF-16 before handing the name to FAT.
                // A valid surrogate pair is two of the 255 code units.
                if (c >= '\uD800' && c <= '\uDBFF')
                {
                    if (i + 1 >= name.Length || name[i + 1] < '\uDC00' ||
                        name[i + 1] > '\uDFFF')
                        return false;
                    i++;
                }
                else if (c >= '\uDC00' && c <= '\uDFFF')
                    return false;
            }
            // FAT volumes are frequently shared with Windows. Names such
            // as CON.txt and LPT1 are reserved there even with extensions;
            // reject them rather than creating files that cannot be managed
            // consistently after moving the volume between systems.
            int dot = name.IndexOf('.');
            string stem = (dot >= 0 ? name.Substring(0, dot) : name).TrimEnd(' ');
            if (string.Equals(stem, "CON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "PRN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "AUX", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "NUL", StringComparison.OrdinalIgnoreCase))
                return false;
            // Windows also treats superscript ¹²³ as device numbers.
            if (stem.Length == 4 &&
                ((stem[3] >= '1' && stem[3] <= '9') ||
                 stem[3] == '\u00B9' || stem[3] == '\u00B2' || stem[3] == '\u00B3') &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }

        // FAT32 names are case-insensitive. Some host/runtime adapters may
        // implement File.Exists with different casing semantics; check the
        // freshly enumerated directory before issuing a create mutation.
        public static bool HasCaseInsensitiveCollision(string name,
            string[] files, string[] directories)
        {
            return ContainsName(files, name) || ContainsName(directories, name);
        }

        private static bool ContainsName(string[] paths, string name)
        {
            if (string.IsNullOrEmpty(name) || paths == null)
                return false;

            for (int i = 0; i < paths.Length; i++)
            {
                string path = paths[i];
                if (string.IsNullOrEmpty(path))
                    continue;
                int slash = System.Math.Max(path.LastIndexOf('/'), path.LastIndexOf('\\'));
                string existing = slash >= 0 ? path.Substring(slash + 1) : path;
                if (string.Equals(existing, name, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
