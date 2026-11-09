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
        public const string FallbackReleaseKey = "ZONDERQ_OFFICIAL_RELEASE_KEY_GEN2";

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

        public int TargetTheme { get; set; } = 0;
        public string TargetThemeName { get; set; } = "COSMOS BLUE";
        public int TargetWallpaper { get; set; } = 0;

        public List<UpdateFileEntry> Files { get; } = new List<UpdateFileEntry>();
        public List<string> Changelog { get; } = new List<string>();
        public List<string> Components { get; } = new List<string>();

        public static bool TryParse(string content, out UpdateManifest manifest, out string error)
        {
            manifest = null;
            error = string.Empty;

            if (string.IsNullOrWhiteSpace(content))
            {
                error = "Manifest jest pusty.";
                return false;
            }

            var parsed = new UpdateManifest();
            bool hasVersion = false;

            string[] lines = content.Replace("\r", "").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line.StartsWith("#"))
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim().ToUpperInvariant();
                string val = line.Substring(eq + 1).Trim();

                switch (key)
                {
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
                        // Format: FILE=relative_path:sha256[:size]
                        string[] parts = val.Split(':');
                        if (parts.Length >= 2)
                        {
                            var fe = new UpdateFileEntry
                            {
                                RelativePath = parts[0].Trim(),
                                ExpectedSha256 = parts[1].Trim().ToLowerInvariant(),
                                SizeBytes = parts.Length >= 3 && long.TryParse(parts[2].Trim(), out long sz) ? sz : 0
                            };
                            parsed.Files.Add(fe);
                        }
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
                }
            }

            if (!hasVersion)
            {
                error = "Manifest nie zawiera wymaganego pola 'VERSION'.";
                return false;
            }

            if (string.IsNullOrEmpty(parsed.KernelSha256))
            {
                error = "Manifest nie zawiera wymaganego pola 'KERNEL_SHA256'.";
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

        public bool VerifySignature(string secretKey = null)
        {
            if (string.IsNullOrEmpty(Signature))
                return false;

            if (!string.IsNullOrEmpty(secretKey))
            {
                string expected = Crypto.ComputeHmacSha256(secretKey, GetSignaturePayload());
                return Crypto.FixedTimeEquals(Signature.ToLowerInvariant(), expected.ToLowerInvariant());
            }

            // Check primary release key
            string primary = Crypto.ComputeHmacSha256(DefaultSignerKey, GetSignaturePayload());
            if (Crypto.FixedTimeEquals(Signature.ToLowerInvariant(), primary.ToLowerInvariant()))
                return true;

            // Check fallback/secondary release key
            string secondary = Crypto.ComputeHmacSha256(FallbackReleaseKey, GetSignaturePayload());
            return Crypto.FixedTimeEquals(Signature.ToLowerInvariant(), secondary.ToLowerInvariant());
        }

        public void Sign(string secretKey)
        {
            Signature = Crypto.ComputeHmacSha256(secretKey, GetSignaturePayload());
        }

        public string Serialize()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("# ZonderqOS Verified Update Package Manifest");
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
