using System;
using System.IO;
using System.Text;
using ZonderqOS.Security;
using ZonderqOS.SystemCore.Networking;
using ZonderqOS.SystemCore.Updates;

namespace ZonderqOS.Commands
{
    /// <summary>
    /// Diagnostic HTTPS/HTTP streaming fetch utility:
    ///   httpget <https://url> [--out <path>] [--insecure-http]
    /// </summary>
    public sealed class CmdHttpGet : ICommand
    {
        public string Name => "httpget";
        public string Description => "Fetch a URL over HTTPS (TLS 1.2/1.3): httpget <url> [--out <file>]";

        public void Execute(string[] args, ref string currentPath)
        {
            if (args.Length < 2 || string.IsNullOrWhiteSpace(args[1]))
            {
                CommandIO.WriteLine("Usage: httpget <https://url> [--out <path>]");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            if (!Network.IsReady)
            {
                CommandIO.WriteLine("httpget: network is not configured. Run 'net dhcp' first.");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            string url = args[1].Trim();
            string outPath = null;
            bool allowHttp = false;

            for (int i = 2; i < args.Length; i++)
            {
                if (args[i] == "--out" && i + 1 < args.Length)
                {
                    outPath = args[++i];
                }
                else if (args[i] == "--insecure-http")
                {
                    allowHttp = true;
                }
            }

            HttpDownloadOptions opts = OtaUpdateClient.CreateDefaultOptions(16L * 1024L * 1024L);
            opts.AllowInsecureHttp = allowHttp;

            if (!string.IsNullOrEmpty(outPath))
            {
                if (!SecurityContext.IsAuthenticated)
                {
                    WriteMessage.WriteError("Authentication required to save downloaded files.", "NET");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                string resolved = PathResolver.GetAbsolutePath(currentPath, outPath);
                string user = SecurityContext.CurrentUser ?? string.Empty;
                if (!PermissionManager.CanCreate(resolved, user) && !PermissionManager.CanWrite(resolved, user))
                {
                    WriteMessage.WriteError("Permission denied: cannot write to " + resolved, "NET");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                CommandIO.WriteLine("[HTTP] Downloading " + url + " -> " + resolved + "...");
                HttpDownloadResult res = HttpStreamClient.DownloadToFile(url, resolved, null, opts);
                if (!res.Success)
                {
                    WriteMessage.WriteError("httpget failed: " + res.Error, "NET");
                    CommandIO.LastCommandSuccess = false;
                    return;
                }

                WriteMessage.WriteOK("Downloaded " + res.BytesReceived + " bytes (HTTP " + res.StatusCode + ")", "NET");
                CommandIO.WriteLine("  Final URL: " + res.FinalUrl);
                CommandIO.WriteLine("  SHA-256:   " + res.Sha256Hex);
                CommandIO.LastCommandSuccess = true;
                return;
            }

            opts.MaxBytes = 256 * 1024;
            CommandIO.WriteLine("[HTTP] GET " + url + "...");
            HttpDownloadResult memRes = HttpStreamClient.DownloadBytes(url, opts, out byte[] body);
            if (!memRes.Success)
            {
                WriteMessage.WriteError("httpget failed: " + memRes.Error, "NET");
                CommandIO.LastCommandSuccess = false;
                return;
            }

            WriteMessage.WriteOK("HTTP " + memRes.StatusCode + " (" + memRes.BytesReceived + " bytes)", "NET");
            CommandIO.WriteLine("  Final URL: " + memRes.FinalUrl);
            CommandIO.WriteLine("  SHA-256:   " + memRes.Sha256Hex);
            if (body != null && body.Length > 0)
            {
                int previewLen = Math.Min(body.Length, 1024);
                string preview = Encoding.UTF8.GetString(body, 0, previewLen);
                CommandIO.WriteLine("--- Preview (" + previewLen + "/" + body.Length + " bytes) ---");
                CommandIO.WriteLine(preview);
            }
            CommandIO.LastCommandSuccess = true;
        }
    }
}
