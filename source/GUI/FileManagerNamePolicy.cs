using System;

namespace ZonderqOS.GUI
{
    // Shared with the host-only smoke test: keep the FAT32 filename policy
    // independent of Cosmos graphics, storage and user-session APIs.
    public static class FileManagerNamePolicy
    {
        public static bool IsSafeNewEntryName(string name)
        {
            if (string.IsNullOrEmpty(name) || name == "." || name == ".." ||
                name[name.Length - 1] == '.')
                return false;

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c < 32 || c == '/' || c == '\\' || c == ':' ||
                    c == '*' || c == '?' || c == '"' || c == '<' ||
                    c == '>' || c == '|')
                    return false;
            }
            // FAT volumes are frequently shared with Windows. Names such
            // as CON.txt and LPT1 are reserved there even with extensions;
            // reject them rather than creating files that cannot be managed
            // consistently after moving the volume between systems.
            int dot = name.IndexOf('.');
            string stem = dot >= 0 ? name.Substring(0, dot) : name;
            if (string.Equals(stem, "CON", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "PRN", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "AUX", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(stem, "NUL", StringComparison.OrdinalIgnoreCase))
                return false;
            if (stem.Length == 4 && stem[3] >= '1' && stem[3] <= '9' &&
                (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                 stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)))
                return false;

            return true;
        }
    }
}
