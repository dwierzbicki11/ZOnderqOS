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
                // Unicode bidirectional controls can make a filename
                // appear to have a different extension in the GUI.
                // Refuse invisible direction overrides.
                if (c == '\u061C' || c == '\u200E' || c == '\u200F' ||
                    c == '\u00AD' || c == '\u034F' || c == '\u180E' ||
                    c == '\u200B' || c == '\u200C' || c == '\u200D' ||
                    (c >= '\u2060' && c <= '\u2064') || c == '\uFEFF' ||
                    c == '\u2028' || c == '\u2029' ||
                    // Hangul fillers render as blank glyphs in many fonts.
                    c == '\u115F' || c == '\u1160' ||
                    c == '\u3164' || c == '\uFFA0' ||
                    // Variation selectors change glyph presentation without
                    // changing the visible filename text in many GUI fonts.
                    (c >= '\uFE00' && c <= '\uFE0F') ||
                    (c >= '\u202A' && c <= '\u202E') ||
                    (c >= '\u2066' && c <= '\u206F'))
                    return false;

                // Unicode noncharacters have no interoperable text meaning and
                // should never become ambiguous VFAT directory entries.
                if ((c >= '\uFDD0' && c <= '\uFDEF') ||
                    c == '\uFFFE' || c == '\uFFFF')
                    return false;

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
                    // Plane-14 tag characters (U+E0000..U+E007F) are
                    // invisible in the desktop font and can spoof filenames.
                    // They encode as DB40 DC00..DC7F in UTF-16.
                    if (c == '\uDB40' && name[i + 1] >= '\uDC00' &&
                        name[i + 1] <= '\uDC7F')
                        return false;
                    // Supplementary-plane noncharacters end in FFFE/FFFF.
                    // Check the low surrogate of the valid pair.
                    if ((c == '\uDB40' && name[i + 1] >= '\uDD00' &&
                         name[i + 1] <= '\uDDEF') ||
                        name[i + 1] == '\uDFFE' || name[i + 1] == '\uDFFF')
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
            // Inspect the reserved DOS device stem in place. Avoid allocating
            // Substring/TrimEnd for every name typed in the file manager.
            int dot = name.IndexOf('.');
            int stemEnd = dot >= 0 ? dot : name.Length;
            while (stemEnd > 0 && name[stemEnd - 1] == ' ')
                stemEnd--;
            if ((stemEnd == 3 &&
                 (PrefixEquals(name, "CON") || PrefixEquals(name, "PRN") ||
                  PrefixEquals(name, "AUX") || PrefixEquals(name, "NUL"))) ||
                (stemEnd == 6 && PrefixEquals(name, "CONIN$")) ||
                (stemEnd == 7 && PrefixEquals(name, "CONOUT$")))
                return false;
            // Windows also treats superscript ¹²³ as device numbers.
            if (stemEnd == 4 &&
                ((name[3] >= '1' && name[3] <= '9') ||
                 name[3] == '\u00B9' || name[3] == '\u00B2' || name[3] == '\u00B3') &&
                (PrefixEquals(name, "COM") || PrefixEquals(name, "LPT")))
                return false;

            return true;
        }

        private static bool PrefixEquals(string name, string prefix)
        {
            return string.Compare(name, 0, prefix, 0, prefix.Length,
                StringComparison.OrdinalIgnoreCase) == 0;
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
                // Cosmos directory enumeration may include a trailing
                // separator. Match FileManagerApp.Add's normalization so
                // "Report/" still collides with a requested "report".
                int end = path.Length;
                while (end > 0 && (path[end - 1] == '/' || path[end - 1] == '\\'))
                    end--;
                if (end == 0)
                    continue;
                int slash = -1;
                for (int j = end - 1; j >= 0; j--)
                {
                    if (path[j] == '/' || path[j] == '\\')
                    {
                        slash = j;
                        break;
                    }
                }
                // Compare the final path segment in place. Creating a file
                // in a large FAT32 directory used to allocate one substring
                // for every existing entry, even when no name matched.
                // Length equality prevents a prefix ("note" vs "notes")
                // from being mistaken for a collision.
                if (end - slash - 1 == name.Length &&
                    string.Compare(path, slash + 1, name, 0, name.Length,
                        StringComparison.OrdinalIgnoreCase) == 0)
                    return true;
            }
            return false;
        }
    }
}
