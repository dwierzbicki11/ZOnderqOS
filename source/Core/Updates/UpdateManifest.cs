using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS.SystemCore.Updates
{
    public sealed class UpdateFileEntry
    {
        public string RelativePath { get; set; } = string.Empty;
        public string ExpectedSha256 { get; set; } = string.Empty;
        public long SizeBytes { get; set; } = 0;
    }

    /// <summary>
    /// Cryptographically verified update manifest describing system binaries,
    /// kernel images, SHA-256 digests, and package authenticity.
    /// </summary>
    public sealed class UpdateManifest
    {
        public const string ExpectedArchitecture = "x86_64";
        public const string DefaultSignerKey = "ZONDERQ_OFFICIAL_RELEASE_KEY_GEN3";

        public string Version { get; set; } = "1.0.0";
        public int BuildNumber { get; set; } = 3100;
        public string Architecture { get; set; } = ExpectedArchitecture;
        public string Title { get; set; } = "ZonderqOS System Update";
        public string ReleaseDate { get; set; } = "2026-11-08";
        public string Severity { get; set; } = "Zalecana";

        public string KernelImage { get; set; } = "ZonderqOS.elf";
        public string KernelSha256 { get; set; } = string.Empty;

        public string PayloadDirectory { get; set; } = "payload";
        public string Signature { get; set; } = string.Empty;
        public int UpdateFormat { get; set; } = 1;
        public string SignatureAlgorithm { get; set; } = string.Empty;

        public int TargetTheme { get; set; } = 0;
        public string TargetThemeName { get; set; } = "COSMOS BLUE";
        public int TargetWallpaper { get; set; } = 0;

        public List<UpdateFileEntry> Files { get; } = new List<UpdateFileEntry>();
        public List<string> Changelog { get; } = new List<string>();
        public List<string> Components { get; } = new List<string>();

        private static bool IsSha256(string hash)
        {
            if (hash == null || hash.Length != 64) return false;
            for (int i = 0; i < hash.Length; i++)
            {
                char ch = hash[i];
                if (!((ch >= '0' && ch <= '9') ||
                      (ch >= 'a' && ch <= 'f') || (ch >= 'A' && ch <= 'F')))
                    return false;
            }
            return true;
        }

        // Paths in manifests are always slash-separated and relative to the
        // package root. Reject traversal, Windows drive names, UNC paths,
        // empty segments and ambiguous dot/space suffixes.
        internal static bool IsSafeRelativePath(string path)
        {
            if (string.IsNullOrEmpty(path) || path.Length > 240 ||
                path[0] == '/' || path[0] == '\\' || path.IndexOf('\\') >= 0 ||
                path.IndexOf(':') >= 0)
                return false;
            string[] parts = path.Split('/');
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0 || part == "." || part == ".." ||
                    part[part.Length - 1] == '.' || part[part.Length - 1] == ' ')
                    return false;
                for (int j = 0; j < part.Length; j++)
                {
                    char ch = part[j];
                    if (char.IsControl(ch) || ch == '<' || ch == '>' ||
                        ch == '|' || ch == '*' || ch == '?' || ch == '"')
                        return false;
                }
            }
            return true;
        }

        public static bool TryParse(string content, out UpdateManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Manifest jest pusty.";
                return false;
            }

            if (content.Length > 1024 * 1024)
            {
                error = "Manifest przekracza limit 1 MiB.";
                return false;
            }

            var parsed = new UpdateManifest();
            bool hasVersion = false;
            var singletonKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = content.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                {
                    error = "Niepoprawny wpis manifestu w linii " + (i + 1) + ".";
                    return false;
                }

                string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                string val = line.Substring(eq + 1).Trim();
                // Aliases must not allow two conflicting values for one key.
                string canonicalKey = key == "BUILD_ID" ? "BUILD" :
                    key == "ARCHITECTURE" ? "ARCH" :
                    key == "RELEASE_DATE" ? "DATE" :
                    key == "KERNEL" ? "KERNEL_IMAGE" :
                    key == "SHA256" ? "KERNEL_SHA256" :
                    key == "PAYLOAD" ? "PAYLOAD_DIR" :
                    key == "HMAC" ? "SIGNATURE" : key;
                if (canonicalKey != "FILE" && !singletonKeys.Add(canonicalKey))
                {
                    error = "Powtorzone pole manifestu: " + canonicalKey;
                    return false;
                }

                switch (key)
                {
                    case "UPDATE_FORMAT":
                        if (!int.TryParse(val, out int format) || format != 2)
                        {
                            error = "Nieobslugiwany format manifestu.";
                            return false;
                        }
                        parsed.UpdateFormat = format;
                        break;
                    case "SIGNATURE_ALG":
                        parsed.SignatureAlgorithm = val;
                        break;
                    case "VERSION":
                        parsed.Version = val;
                        hasVersion = true;
                        break;
                    case "BUILD":
                    case "BUILD_ID":
                        if (int.TryParse(val, out int bld))
                        {
                            parsed.BuildNumber = bld;
                        }
                        break;
                    case "ARCH":
                    case "ARCHITECTURE":
                        parsed.Architecture = val;
                        break;
                    case "TITLE":
                        parsed.Title = val;
                        break;
                    case "DATE":
                    case "RELEASE_DATE":
                        parsed.ReleaseDate = val;
                        break;
                    case "SEVERITY":
                        parsed.Severity = val;
                        break;
                    case "KERNEL":
                    case "KERNEL_IMAGE":
                        parsed.KernelImage = val;
                        break;
                    case "KERNEL_SHA256":
                    case "SHA256":
                        parsed.KernelSha256 = val;
                        break;
                    case "PAYLOAD":
                    case "PAYLOAD_DIR":
                        parsed.PayloadDirectory = val;
                        break;
                    case "SIGNATURE":
                    case "HMAC":
                        parsed.Signature = val;
                        break;
                    case "THEME":
                        parsed.TargetTheme = UpdateManager.ParseTheme(val);
                        parsed.TargetThemeName = val;
                        break;
                    case "WALLPAPER":
                        parsed.TargetWallpaper = UpdateManager.ParseWallpaper(val);
                        break;
                    case "FILE":
                    {
                        // Strict FILE=relative_path:sha256[:size]. Zero is a
                        // valid size, while a missing size stays legacy 0.
                        string[] parts = val.Split(':');
                        if (parts.Length < 2 || parts.Length > 3 ||
                            !IsSafeRelativePath(parts[0]) || !IsSha256(parts[1]) ||
                            (parts.Length == 3 && (!long.TryParse(parts[2], out long ignoredSize) || ignoredSize < 0)) ||
                            !seenFiles.Add(parts[0]))
                        {
                            error = "Niepoprawny lub powtorzony wpis FILE w linii " + (i + 1) + ".";
                            return false;
                        }
                        if (parsed.Files.Count >= 1024)
                        {
                            error = "Za duzo wpisow FILE w manifeście.";
                            return false;
                        }
                        parsed.Files.Add(new UpdateFileEntry
                        {
                            RelativePath = parts[0],
                            ExpectedSha256 = parts[1].ToLowerInvariant(),
                            SizeBytes = parts.Length == 3 ? long.Parse(parts[2]) : 0
                        });
                        break;
                    }
                    case "CHANGELOG":
                    {
                        string[] items = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        for (int j = 0; j < items.Length; j++)
                            parsed.Changelog.Add(items[j].Trim());
                        break;
                    }
                    case "COMPONENTS":
                    {
                        string[] comps = val.Split(';', StringSplitOptions.RemoveEmptyEntries);
                        for (int c = 0; c < comps.Length; c++)
                            parsed.Components.Add(comps[c].Trim());
                        break;
                    }
                    default:
                        error = "Nieznane pole manifestu: " + key;
                        return false;
                }
            }

            if (!hasVersion)
            {
                error = "Manifest nie zawiera wymaganego pola 'VERSION'.";
                return false;
            }
            // Detached signatures cover the EXACT original manifest bytes.
            // HMAC fields are forbidden in the new asymmetric format.
            if (parsed.UpdateFormat == 2 &&
                (!string.Equals(parsed.SignatureAlgorithm, "ED25519", StringComparison.Ordinal) ||
                 !string.IsNullOrEmpty(parsed.Signature)))
            {
                error = "Format 2 wymaga SIGNATURE_ALG=ED25519 i pliku .sig zamiast HMAC.";
                return false;
            }


            if (!IsSafeRelativePath(parsed.KernelImage) ||
                !IsSafeRelativePath(parsed.PayloadDirectory) ||
                !IsSha256(parsed.KernelSha256))
            {
                error = "Niepoprawna sciezka kernela/payload lub SHA-256.";
                return false;
            }

            if (!string.Equals(parsed.Architecture, ExpectedArchitecture, StringComparison.OrdinalIgnoreCase))
            {
                error = $"Niezgodna architektura pakietu: {parsed.Architecture} (wymagana: {ExpectedArchitecture}).";
                return false;
            }

            manifest = parsed;
            return true;
        }

        /// <summary>
        /// Produces canonical data representation for cryptographic signature calculation.
        /// </summary>
        public string GetSignaturePayload()
        {
            return $"{Version}|{BuildNumber}|{Architecture}|{KernelImage}|{KernelSha256}|{Title}";
        }

        public bool VerifySignature(string secretKey)
        {
            if (string.IsNullOrEmpty(Signature))
                return false;

            string expected = Crypto.ComputeHmacSha256(secretKey, GetSignaturePayload());
            return Crypto.FixedTimeEquals(Signature.ToLowerInvariant(), expected.ToLowerInvariant());
        }

        public void Sign(string secretKey)
        {
            Signature = Crypto.ComputeHmacSha256(secretKey, GetSignaturePayload());
        }

        public string Serialize()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# ZonderqOS Verified Update Package Manifest");
            if (UpdateFormat == 2)
            {
                sb.AppendLine("UPDATE_FORMAT=2");
                sb.AppendLine("SIGNATURE_ALG=ED25519");
            }
            sb.AppendLine("VERSION=" + Version);
            sb.AppendLine("BUILD=" + BuildNumber);
            sb.AppendLine("ARCH=" + Architecture);
            sb.AppendLine("TITLE=" + Title);
            sb.AppendLine("DATE=" + ReleaseDate);
            sb.AppendLine("SEVERITY=" + Severity);
            if (!string.IsNullOrEmpty(KernelImage))
                sb.AppendLine("KERNEL_IMAGE=" + KernelImage);
            if (!string.IsNullOrEmpty(KernelSha256))
                sb.AppendLine("KERNEL_SHA256=" + KernelSha256);
            if (!string.IsNullOrEmpty(PayloadDirectory))
                sb.AppendLine("PAYLOAD_DIR=" + PayloadDirectory);
            if (!string.IsNullOrEmpty(Signature))
                sb.AppendLine("SIGNATURE=" + Signature);
            sb.AppendLine("THEME=" + TargetThemeName);
            sb.AppendLine("WALLPAPER=" + TargetWallpaper);

            for (int i = 0; i < Files.Count; i++)
            {
                sb.AppendLine($"FILE={Files[i].RelativePath}:{Files[i].ExpectedSha256}:{Files[i].SizeBytes}");
            }

            if (Changelog.Count > 0)
                sb.AppendLine("CHANGELOG=" + string.Join(";", Changelog));
            if (Components.Count > 0)
                sb.AppendLine("COMPONENTS=" + string.Join(";", Components));

            return sb.ToString();
        }
    }
}
