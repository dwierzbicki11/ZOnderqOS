using System;
using System.IO;
using System.Text;
using ZonderqOS.SystemCore.Networking;

namespace ZonderqOS.SystemCore.Updates
{
    public sealed class OtaCheckResult
    {
        public bool Success { get; set; }
        public bool UpdateAvailable { get; set; }
        public UpdateManifest Manifest { get; set; }
        public byte[] RawManifestBytes { get; set; }
        public byte[] RawSignatureBytes { get; set; }
        public string BaseUrl { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Streaming HTTPS Over-The-Air (OTA) update downloader for ZonderqOS.
    /// Verifies Ed25519 manifest signatures BEFORE downloading large kernel images,
    /// streams payloads directly to disk in 16 KiB chunks with incremental SHA-256,
    /// and hands off verified bundles to UpdateManager's P0 trust gate and A/B slots.
    /// </summary>
    public static class OtaUpdateClient
    {
        public const string DefaultGitHubOtaUrl =
            "https://github.com/dwierzbicki11/ZOnderqOS/releases/latest/download";

        private const long MaxManifestBytes = 1024L * 1024L;
        private const long MaxSignatureBytes = 256L;
        private const long MaxKernelBytes = 64L * 1024L * 1024L;
        private const long MaxPayloadFileBytes = 32L * 1024L * 1024L;

        private static Func<bool> isNetworkReadyProvider = () => false;
        private static Func<string, string> dnsResolverProvider = null;
        private static Func<bool> autoConnectProvider = () => false;

        public static void BindNetworkBridge(Func<bool> isReady, Func<string, string> resolveDns,
            Func<bool> autoConnect = null)
        {
            isNetworkReadyProvider = isReady ?? (() => false);
            dnsResolverProvider = resolveDns;
            autoConnectProvider = autoConnect ?? (() => false);
        }

        public static bool IsNetworkReady()
        {
            try { return isNetworkReadyProvider != null && isNetworkReadyProvider(); }
            catch { return false; }
        }

        public static bool TryEnsureNetworkConnected()
        {
            if (IsNetworkReady())
                return true;
            try
            {
                return autoConnectProvider != null && autoConnectProvider() && IsNetworkReady();
            }
            catch
            {
                return false;
            }
        }

        public static HttpDownloadOptions CreateDefaultOptions(long maxBytes = MaxKernelBytes)
        {
            return new HttpDownloadOptions
            {
                TimeoutMs = 15000,
                MaxRedirects = 5,
                MaxBytes = maxBytes,
                AllowInsecureHttp = false,
                DnsResolver = dnsResolverProvider
            };
        }

        public static bool CheckRemoteRelease(string baseUrl, string currentVersion, string currentBuild,
            bool forceRefresh, HttpDownloadOptions baseOptions,
            Func<byte[], byte[], bool> signatureVerifier, out OtaCheckResult result)
        {
            result = new OtaCheckResult { BaseUrl = baseUrl ?? string.Empty };

            HttpDownloadOptions opts = CloneOptions(baseOptions, MaxManifestBytes);
            if (!OtaUrlParser.TryParse(baseUrl, opts.AllowInsecureHttp, out _, out string urlErr))
            {
                result.Error = "Invalid OTA URL: " + urlErr;
                return false;
            }

            string manifestUrl = OtaUrlParser.CombineBaseAndRelative(baseUrl, "update.manifest");
            string sigUrl = OtaUrlParser.CombineBaseAndRelative(baseUrl, "update.manifest.sig");

            HttpDownloadResult manifestDl = HttpStreamClient.DownloadBytes(manifestUrl, opts, out byte[] manifestBytes);
            if (!manifestDl.Success || manifestBytes == null || manifestBytes.Length == 0)
            {
                result.Error = "Failed to download OTA update.manifest: " + manifestDl.Error;
                return false;
            }

            HttpDownloadOptions sigOpts = CloneOptions(baseOptions, MaxSignatureBytes);
            HttpDownloadResult sigDl = HttpStreamClient.DownloadBytes(sigUrl, sigOpts, out byte[] rawSigBytes);
            if (!sigDl.Success || rawSigBytes == null || rawSigBytes.Length == 0)
            {
                result.Error = "Failed to download OTA update.manifest.sig: " + sigDl.Error;
                return false;
            }

            byte[] signature = DecodeEd25519Signature(rawSigBytes);
            if (signature == null)
            {
                result.Error = "Malformed OTA Ed25519 signature (expected 64 raw bytes or 128 hex chars).";
                return false;
            }

            Func<byte[], byte[], bool> verifier = signatureVerifier ?? ReleaseSignatureVerifier.VerifyProductionManifest;
            if (!verifier(manifestBytes, signature))
            {
                result.Error = "OTA update rejected: invalid or untrusted Ed25519 release signature.";
                return false;
            }

            string manifestText = Encoding.UTF8.GetString(manifestBytes);
            if (!UpdateManifest.TryParse(manifestText, out UpdateManifest parsed, out string parseErr) || parsed == null)
            {
                result.Error = "Invalid signed OTA manifest: " + parseErr;
                return false;
            }

            if (parsed.UpdateFormat != 2 || !string.Equals(parsed.SignatureAlgorithm, "ED25519", StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "OTA manifest must be format 2 with ED25519 signature.";
                return false;
            }

            if (!string.Equals(parsed.Architecture, KernelBuildInfo.Architecture, StringComparison.OrdinalIgnoreCase))
            {
                result.Error = "OTA package architecture '" + parsed.Architecture +
                               "' does not match running kernel '" + KernelBuildInfo.Architecture + "'.";
                return false;
            }

            bool isNewer = UpdateManager.IsNewerVersion(parsed.Version, parsed.BuildNumber, currentVersion, currentBuild);
            result.Success = true;
            result.UpdateAvailable = isNewer || forceRefresh;
            result.Manifest = parsed;
            result.RawManifestBytes = manifestBytes;
            result.RawSignatureBytes = signature;
            return true;
        }

        public static bool DownloadVerifiedPackageToDirectory(OtaCheckResult check, string stagingDir,
            HttpDownloadOptions baseOptions, Action<int, string> progress,
            out string manifestPathOnDisk, out string error)
        {
            manifestPathOnDisk = string.Empty;
            error = string.Empty;

            if (check == null || !check.Success || check.Manifest == null ||
                check.RawManifestBytes == null || check.RawSignatureBytes == null)
            {
                error = "No verified OTA release metadata available.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(stagingDir))
            {
                error = "Staging directory path is empty.";
                return false;
            }

            UpdateManifest manifest = check.Manifest;
            if (!UpdateManifest.IsSafeRelativePath(manifest.KernelImage) ||
                !UpdateManifest.IsSafeRelativePath(manifest.PayloadDirectory))
            {
                error = "Unsafe kernel or payload path in OTA manifest.";
                return false;
            }

            try
            {
                progress?.Invoke(5, "Preparing OTA staging directory...");
                ResetDirectory(stagingDir);

                string kernelRel = manifest.KernelImage;
                string kernelUrl = OtaUrlParser.CombineBaseAndRelative(check.BaseUrl, kernelRel);
                string kernelDest = Path.Combine(stagingDir, kernelRel);

                progress?.Invoke(10, "Downloading verified kernel (" + kernelRel + ")...");
                HttpDownloadOptions kernelOpts = CloneOptions(baseOptions, MaxKernelBytes);
                int lastReportedPct = 10;
                HttpDownloadResult kernelRes = HttpStreamClient.DownloadToFile(
                    kernelUrl, kernelDest, manifest.KernelSha256, kernelOpts,
                    (received, total) =>
                    {
                        if (total > 0)
                        {
                            int pct = 10 + (int)((received * 65L) / total);
                            if (pct > lastReportedPct && pct <= 75)
                            {
                                lastReportedPct = pct;
                                progress?.Invoke(pct, "Downloading kernel... " + (received / 1024) + " / " + (total / 1024) + " KiB");
                            }
                        }
                    });

                if (!kernelRes.Success)
                {
                    ResetDirectory(stagingDir);
                    error = "Kernel download failed: " + kernelRes.Error;
                    return false;
                }

                if (!BootSlotManager.HasValidElfHeaderFile(kernelDest))
                {
                    ResetDirectory(stagingDir);
                    error = "Downloaded kernel binary is not a valid ELF64 image for this architecture.";
                    return false;
                }

                string payloadRoot = Path.Combine(stagingDir, manifest.PayloadDirectory);
                if (!Directory.Exists(payloadRoot))
                    Directory.CreateDirectory(payloadRoot);

                int fileCount = manifest.Files.Count;
                for (int i = 0; i < fileCount; i++)
                {
                    UpdateFileEntry entry = manifest.Files[i];
                    if (entry == null || !UpdateManifest.IsSafeRelativePath(entry.RelativePath))
                    {
                        ResetDirectory(stagingDir);
                        error = "Manifest contains an unsafe payload file entry.";
                        return false;
                    }

                    int pct = 75 + (fileCount > 0 ? ((i * 20) / fileCount) : 20);
                    progress?.Invoke(pct, "Downloading payload (" + (i + 1) + "/" + fileCount + "): " + entry.RelativePath);

                    string destFile = Path.Combine(payloadRoot, entry.RelativePath);
                    string nestedUrl = OtaUrlParser.CombineBaseAndRelative(
                        check.BaseUrl, manifest.PayloadDirectory + "/" + entry.RelativePath);

                    HttpDownloadOptions fileOpts = CloneOptions(baseOptions,
                        entry.SizeBytes > 0 ? Math.Min(entry.SizeBytes, MaxPayloadFileBytes) : MaxPayloadFileBytes);

                    HttpDownloadResult fileRes = HttpStreamClient.DownloadToFile(
                        nestedUrl, destFile, entry.ExpectedSha256, fileOpts);

                    // GitHub Releases stores assets flat (no subdirectories). Support flat fallback on 404.
                    if (!fileRes.Success && fileRes.StatusCode == 404)
                    {
                        string flatAsset = "payload__" + entry.RelativePath.Replace('/', '_');
                        string flatUrl = OtaUrlParser.CombineBaseAndRelative(check.BaseUrl, flatAsset);
                        fileRes = HttpStreamClient.DownloadToFile(flatUrl, destFile, entry.ExpectedSha256, fileOpts);
                    }

                    if (!fileRes.Success)
                    {
                        ResetDirectory(stagingDir);
                        error = "Payload download failed (" + entry.RelativePath + "): " + fileRes.Error;
                        return false;
                    }

                    if (entry.SizeBytes > 0 && fileRes.BytesReceived != entry.SizeBytes)
                    {
                        ResetDirectory(stagingDir);
                        error = "Payload size mismatch for " + entry.RelativePath +
                                " (expected " + entry.SizeBytes + ", got " + fileRes.BytesReceived + ").";
                        return false;
                    }
                }

                progress?.Invoke(96, "Writing verified OTA manifest and signature...");
                string sigPath = Path.Combine(stagingDir, "update.manifest.sig");
                string manPath = Path.Combine(stagingDir, "update.manifest");
                File.WriteAllBytes(sigPath, check.RawSignatureBytes);
                File.WriteAllBytes(manPath, check.RawManifestBytes);

                manifestPathOnDisk = manPath;
                progress?.Invoke(100, "OTA update package downloaded and verified.");
                return true;
            }
            catch (Exception ex)
            {
                try { ResetDirectory(stagingDir); } catch { }
                error = "OTA staging failed: " + ex.Message;
                return false;
            }
        }

        private static HttpDownloadOptions CloneOptions(HttpDownloadOptions src, long maxBytes)
        {
            if (src == null)
            {
                return new HttpDownloadOptions { MaxBytes = maxBytes };
            }
            return new HttpDownloadOptions
            {
                TimeoutMs = src.TimeoutMs,
                MaxRedirects = src.MaxRedirects,
                MaxBytes = maxBytes,
                AllowInsecureHttp = src.AllowInsecureHttp,
                DnsResolver = src.DnsResolver,
                ExtraTrustedRoot = src.ExtraTrustedRoot,
                UserAgent = src.UserAgent
            };
        }

        private static byte[] DecodeEd25519Signature(byte[] raw)
        {
            if (raw == null)
                return null;
            if (raw.Length == 64)
                return raw;

            string text = Encoding.ASCII.GetString(raw).Trim();
            if (text.Length == 128)
            {
                try
                {
                    byte[] decoded = Convert.FromHexString(text);
                    return decoded.Length == 64 ? decoded : null;
                }
                catch
                {
                    return null;
                }
            }
            return null;
        }

        private static void ResetDirectory(string dir)
        {
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
                for (int i = 0; i < files.Length; i++)
                {
                    try { File.Delete(files[i]); } catch { }
                }
                string[] subdirs = Directory.GetDirectories(dir);
                for (int i = 0; i < subdirs.Length; i++)
                {
                    try { Directory.Delete(subdirs[i], true); } catch { }
                }
            }
            else
            {
                Directory.CreateDirectory(dir);
            }
        }
    }
}
