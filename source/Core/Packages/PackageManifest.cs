using System;

namespace ZonderqOS.SystemCore.Packages
{
    public sealed class PackageManifest
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }

        public PackageManifest(string name, string version, string description)
        {
            Name = name;
            Version = version;
            Description = description ?? string.Empty;
        }

        public static bool TryParse(string content, out PackageManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Manifest is empty.";
                return false;
            }

            string name = null;
            string version = null;
            string description = string.Empty;
            bool seenName = false;
            bool seenVersion = false;
            bool seenDescription = false;

            string[] lines = content.Replace("\r", string.Empty).Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                    continue;

                int separator = line.IndexOf('=');
                if (separator <= 0)
                {
                    error = "Invalid manifest line " + (i + 1) + ". Expected key=value.";
                    return false;
                }

                string key = line.Substring(0, separator).Trim().ToLowerInvariant();
                string value = line.Substring(separator + 1).Trim();

                if (key == "name")
                {
                    if (seenName)
                    {
                        error = "Duplicate 'name' field.";
                        return false;
                    }
                    seenName = true;
                    name = value;
                }
                else if (key == "version")
                {
                    if (seenVersion)
                    {
                        error = "Duplicate 'version' field.";
                        return false;
                    }
                    seenVersion = true;
                    version = value;
                }
                else if (key == "description")
                {
                    if (seenDescription)
                    {
                        error = "Duplicate 'description' field.";
                        return false;
                    }
                    seenDescription = true;
                    description = value;
                }
            }

            if (!IsSafeToken(name, 64))
            {
                error = "Invalid package name. Use letters, digits, '.', '-' or '_'.";
                return false;
            }

            if (!IsSafeToken(version, 64))
            {
                error = "Invalid package version. Use letters, digits, '.', '-' or '_'.";
                return false;
            }

            if (description.Length > 256)
            {
                error = "Description is too long (max 256 characters).";
                return false;
            }

            manifest = new PackageManifest(name, version, description);
            return true;
        }

        public string Serialize()
        {
            return "name=" + Name + "\n" +
                   "version=" + Version + "\n" +
                   "description=" + SanitizeSingleLine(Description) + "\n";
        }

        public static bool IsSafeToken(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length > maxLength)
                return false;

            char first = value[0];
            if (!char.IsLetterOrDigit(first))
                return false;

            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_')
                    continue;
                return false;
            }

            return true;
        }

        private static string SanitizeSingleLine(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            return value.Replace("\r", " ").Replace("\n", " ").Trim();
        }
    }

    public sealed class InstalledPackage
    {
        public string Name { get; }
        public string Version { get; }
        public string Description { get; }
        public string InstallPath { get; }

        public InstalledPackage(string name, string version, string description, string installPath)
        {
            Name = name;
            Version = version;
            Description = description ?? string.Empty;
            InstallPath = installPath;
        }
    }
}
