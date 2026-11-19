using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.X509;

namespace ZonderqOS.SystemCore.Networking
{
    public sealed class HttpDownloadOptions
    {
        public int TimeoutMs { get; set; } = 15000;
        public int MaxRedirects { get; set; } = 5;
        public long MaxBytes { get; set; } = 64L * 1024L * 1024L;
        public bool AllowInsecureHttp { get; set; } = false;
        public Func<string, string> DnsResolver { get; set; } = null;
        public X509Certificate ExtraTrustedRoot { get; set; } = null;
        public string UserAgent { get; set; } = "ZonderqOS-OTA/1.0";
    }

    public sealed class HttpDownloadResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public long BytesReceived { get; set; }
        public string Sha256Hex { get; set; } = string.Empty;
        public string FinalUrl { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    /// <summary>
    /// Bounded, streaming HTTP/1.1 + HTTPS (BouncyCastle TLS 1.2/1.3) client
    /// designed for low-RAM Cosmos Gen3 kernels and host-side regression tests.
    /// Uses a reusable 16 KiB transfer buffer and incremental SHA-256 hashing.
    /// </summary>
    public static class HttpStreamClient
    {
        private const int MaxHeaderBytes = 32 * 1024;
        private const int TransferBufferSize = 16 * 1024;

        public static HttpDownloadResult DownloadBytes(string url, HttpDownloadOptions options,
            out byte[] data, Action<long, long> onProgress = null)
        {
            data = null;
            HttpDownloadOptions opts = options ?? new HttpDownloadOptions();
            long maxAllowed = opts.MaxBytes > 0 ? Math.Min(opts.MaxBytes, 4L * 1024L * 1024L) : 1024L * 1024L;

            using (var ms = new MemoryStream())
            {
                HttpDownloadResult res = ExecuteGet(url, opts, maxAllowed, ms, onProgress);
                if (res.Success)
                    data = ms.ToArray();
                return res;
            }
        }

        public static HttpDownloadResult DownloadToFile(string url, string destinationPath,
            string expectedSha256Hex, HttpDownloadOptions options, Action<long, long> onProgress = null)
        {
            HttpDownloadOptions opts = options ?? new HttpDownloadOptions();
            if (string.IsNullOrWhiteSpace(destinationPath))
            {
                return new HttpDownloadResult { Success = false, Error = "Destination path is empty." };
            }

            string dir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            string partPath = destinationPath + ".part";
            TryDeleteFile(partPath);

            HttpDownloadResult result;
            try
            {
                using (var fs = new FileStream(partPath, FileMode.Create, FileAccess.Write))
                {
                    result = ExecuteGet(url, opts, opts.MaxBytes, fs, onProgress);
                    fs.Flush();
                }

                if (!result.Success)
                {
                    TryDeleteFile(partPath);
                    return result;
                }

                if (!string.IsNullOrEmpty(expectedSha256Hex) &&
                    !string.Equals(result.Sha256Hex, expectedSha256Hex.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    TryDeleteFile(partPath);
                    result.Success = false;
                    result.Error = "SHA-256 mismatch: expected " + expectedSha256Hex + ", got " + result.Sha256Hex + ".";
                    return result;
                }

                if (File.Exists(destinationPath))
                    File.Delete(destinationPath);
                File.Move(partPath, destinationPath);
                return result;
            }
            catch (Exception ex)
            {
                TryDeleteFile(partPath);
                return new HttpDownloadResult
                {
                    Success = false,
                    Error = "File transfer failed: " + ex.Message
                };
            }
        }

        private static HttpDownloadResult ExecuteGet(string initialUrl, HttpDownloadOptions opts,
            long maxBytes, Stream sink, Action<long, long> onProgress)
        {
            if (!OtaUrlParser.TryParse(initialUrl, opts.AllowInsecureHttp, out ParsedOtaUrl current, out string parseError))
            {
                return new HttpDownloadResult { Success = false, Error = parseError };
            }

            int maxHops = Math.Max(0, Math.Min(opts.MaxRedirects, 10));

            for (int hop = 0; hop <= maxHops; hop++)
            {
                // Reset sink between redirect hops in case anything wrote (redirects never write body).
                HttpDownloadResult hopResult = ExecuteSingleHop(current, opts, maxBytes, sink, onProgress, out string redirectLocation);
                hopResult.FinalUrl = current.ToString();

                if (redirectLocation != null)
                {
                    if (hop == maxHops)
                    {
                        hopResult.Success = false;
                        hopResult.Error = "Too many HTTP redirects (limit " + maxHops + ").";
                        return hopResult;
                    }

                    if (!OtaUrlParser.TryResolveRedirect(current, redirectLocation, opts.AllowInsecureHttp,
                            out ParsedOtaUrl next, out string redirErr))
                    {
                        hopResult.Success = false;
                        hopResult.Error = redirErr;
                        return hopResult;
                    }

                    current = next;
                    continue;
                }

                return hopResult;
            }

            return new HttpDownloadResult { Success = false, Error = "Redirect loop terminated." };
        }

        private static HttpDownloadResult ExecuteSingleHop(ParsedOtaUrl target, HttpDownloadOptions opts,
            long maxBytes, Stream sink, Action<long, long> onProgress, out string redirectLocation)
        {
            redirectLocation = null;

            string ipText = ResolveHost(target.Host, opts.DnsResolver, out string dnsError);
            if (string.IsNullOrEmpty(ipText))
            {
                return new HttpDownloadResult { Success = false, Error = dnsError };
            }

            if (!IPAddress.TryParse(ipText, out IPAddress remoteIp))
            {
                return new HttpDownloadResult { Success = false, Error = "Invalid resolved IP address: " + ipText };
            }

            int timeoutMs = Math.Max(1000, Math.Min(opts.TimeoutMs, 60000));

            try
            {
                using (var tcp = new TcpClient())
                {
                    tcp.Connect(remoteIp, target.Port);
                    if (tcp.Client != null)
                    {
                        tcp.Client.ReceiveTimeout = timeoutMs;
                        tcp.Client.SendTimeout = timeoutMs;
                    }

                    NetworkStream netStream = tcp.GetStream();
                    ManagedTlsConnection tls = null;
                    try
                    {
                        Stream ioStream = netStream;
                        if (target.IsHttps)
                        {
                            tls = ManagedTlsConnection.Connect(netStream, target.Host, opts.ExtraTrustedRoot);
                            ioStream = tls.Stream;
                        }

                        string requestText =
                            "GET " + target.PathAndQuery + " HTTP/1.1\r\n" +
                            "Host: " + target.HostHeader + "\r\n" +
                            "User-Agent: " + (string.IsNullOrEmpty(opts.UserAgent) ? "ZonderqOS-OTA/1.0" : opts.UserAgent) + "\r\n" +
                            "Accept: */*\r\n" +
                            "Accept-Encoding: identity\r\n" +
                            "Connection: close\r\n\r\n";

                        byte[] requestBytes = Encoding.ASCII.GetBytes(requestText);
                        ioStream.Write(requestBytes, 0, requestBytes.Length);
                        ioStream.Flush();

                        var reader = new BufferedHttpReader(ioStream);
                        if (!TryReadResponseHeaders(reader, out int statusCode, out Dictionary<string, string> headers, out string hdrErr))
                        {
                            return new HttpDownloadResult { Success = false, Error = hdrErr };
                        }

                        if (statusCode == 301 || statusCode == 302 || statusCode == 303 ||
                            statusCode == 307 || statusCode == 308)
                        {
                            if (headers.TryGetValue("location", out string loc) && !string.IsNullOrWhiteSpace(loc))
                            {
                                redirectLocation = loc;
                                return new HttpDownloadResult { Success = false, StatusCode = statusCode };
                            }
                            return new HttpDownloadResult
                            {
                                Success = false,
                                StatusCode = statusCode,
                                Error = "HTTP " + statusCode + " redirect missing Location header."
                            };
                        }

                        if (statusCode != 200)
                        {
                            return new HttpDownloadResult
                            {
                                Success = false,
                                StatusCode = statusCode,
                                Error = "HTTP server returned status " + statusCode + "."
                            };
                        }

                        bool isChunked = headers.TryGetValue("transfer-encoding", out string te) &&
                                         te.IndexOf("chunked", StringComparison.OrdinalIgnoreCase) >= 0;

                        long contentLength = -1;
                        if (!isChunked && headers.TryGetValue("content-length", out string clStr))
                        {
                            if (!long.TryParse(clStr.Trim(), out contentLength) || contentLength < 0)
                            {
                                return new HttpDownloadResult
                                {
                                    Success = false,
                                    StatusCode = statusCode,
                                    Error = "Invalid Content-Length header."
                                };
                            }
                            if (maxBytes > 0 && contentLength > maxBytes)
                            {
                                return new HttpDownloadResult
                                {
                                    Success = false,
                                    StatusCode = statusCode,
                                    Error = "Remote file (" + contentLength + " bytes) exceeds maximum allowed size (" + maxBytes + " bytes)."
                                };
                            }
                        }

                        var sha = new Sha256Digest();
                        byte[] buf = new byte[TransferBufferSize];
                        long totalReceived = 0;

                        if (isChunked)
                        {
                            if (!ReadChunkedBody(reader, sink, sha, buf, maxBytes, onProgress, out totalReceived, out string chunkErr))
                            {
                                return new HttpDownloadResult
                                {
                                    Success = false,
                                    StatusCode = statusCode,
                                    BytesReceived = totalReceived,
                                    Error = chunkErr
                                };
                            }
                        }
                        else if (contentLength >= 0)
                        {
                            long remaining = contentLength;
                            while (remaining > 0)
                            {
                                int toRead = remaining < buf.Length ? (int)remaining : buf.Length;
                                int read = reader.Read(buf, 0, toRead);
                                if (read <= 0)
                                {
                                    return new HttpDownloadResult
                                    {
                                        Success = false,
                                        StatusCode = statusCode,
                                        BytesReceived = totalReceived,
                                        Error = "Connection closed prematurely (" + totalReceived + "/" + contentLength + " bytes)."
                                    };
                                }
                                sink.Write(buf, 0, read);
                                sha.BlockUpdate(buf, 0, read);
                                totalReceived += read;
                                remaining -= read;
                                onProgress?.Invoke(totalReceived, contentLength);
                            }
                        }
                        else
                        {
                            while (true)
                            {
                                int read = reader.Read(buf, 0, buf.Length);
                                if (read <= 0)
                                    break;
                                if (maxBytes > 0 && totalReceived + read > maxBytes)
                                {
                                    return new HttpDownloadResult
                                    {
                                        Success = false,
                                        StatusCode = statusCode,
                                        BytesReceived = totalReceived,
                                        Error = "Response body exceeded maximum allowed size (" + maxBytes + " bytes)."
                                    };
                                }
                                sink.Write(buf, 0, read);
                                sha.BlockUpdate(buf, 0, read);
                                totalReceived += read;
                                onProgress?.Invoke(totalReceived, -1);
                            }
                        }

                        byte[] digest = new byte[sha.GetDigestSize()];
                        sha.DoFinal(digest, 0);

                        return new HttpDownloadResult
                        {
                            Success = true,
                            StatusCode = statusCode,
                            BytesReceived = totalReceived,
                            Sha256Hex = ToLowerHex(digest)
                        };
                    }
                    finally
                    {
                        tls?.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                return new HttpDownloadResult
                {
                    Success = false,
                    Error = "HTTP/TLS error: " + ex.Message
                };
            }
        }

        private static string ResolveHost(string host, Func<string, string> customResolver, out string error)
        {
            error = string.Empty;
            if (OtaUrlParser.IsIPv4Literal(host))
                return host;

            if (customResolver != null)
            {
                string resolved = customResolver(host);
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
                error = "DNS resolution failed for '" + host + "'.";
                return null;
            }

            try
            {
                IPAddress[] addrs = Dns.GetHostAddresses(host);
                if (addrs != null)
                {
                    for (int i = 0; i < addrs.Length; i++)
                    {
                        if (addrs[i] != null && addrs[i].AddressFamily == AddressFamily.InterNetwork)
                            return addrs[i].ToString();
                    }
                }
            }
            catch { }

            error = "DNS resolution failed for '" + host + "'.";
            return null;
        }

        private static bool TryReadResponseHeaders(BufferedHttpReader reader, out int statusCode,
            out Dictionary<string, string> headers, out string error)
        {
            statusCode = 0;
            headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            error = string.Empty;

            int bytesRead = 0;
            string statusLine = reader.ReadLineAscii(MaxHeaderBytes, ref bytesRead);
            if (string.IsNullOrEmpty(statusLine))
            {
                error = "Empty HTTP status line.";
                return false;
            }

            // Expected: HTTP/1.1 200 OK
            int sp1 = statusLine.IndexOf(' ');
            if (sp1 <= 0 || !statusLine.StartsWith("HTTP/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Malformed HTTP status line.";
                return false;
            }

            int codeStart = sp1 + 1;
            while (codeStart < statusLine.Length && statusLine[codeStart] == ' ')
                codeStart++;
            int sp2 = statusLine.IndexOf(' ', codeStart);
            string codeStr = sp2 > codeStart ? statusLine.Substring(codeStart, sp2 - codeStart) : statusLine.Substring(codeStart);
            if (!int.TryParse(codeStr, out statusCode) || statusCode < 100 || statusCode > 599)
            {
                error = "Invalid HTTP status code.";
                return false;
            }

            while (true)
            {
                string line = reader.ReadLineAscii(MaxHeaderBytes, ref bytesRead);
                if (line == null)
                {
                    error = "Unexpected EOF while reading HTTP headers.";
                    return false;
                }
                if (bytesRead > MaxHeaderBytes)
                {
                    error = "HTTP response headers exceed 32 KiB limit.";
                    return false;
                }
                if (line.Length == 0)
                    return true;

                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                string name = line.Substring(0, colon).Trim().ToLowerInvariant();
                string val = line.Substring(colon + 1).Trim();
                if (!headers.ContainsKey(name))
                    headers[name] = val;
                else
                    headers[name] = headers[name] + ", " + val;
            }
        }

        private static bool ReadChunkedBody(BufferedHttpReader reader, Stream sink, Sha256Digest sha,
            byte[] buf, long maxBytes, Action<long, long> onProgress, out long totalReceived, out string error)
        {
            totalReceived = 0;
            error = string.Empty;
            int lineBudget = 0;

            while (true)
            {
                string sizeLine = reader.ReadLineAscii(4096, ref lineBudget);
                if (sizeLine == null)
                {
                    error = "Unexpected EOF reading chunk size.";
                    return false;
                }
                int semi = sizeLine.IndexOf(';');
                if (semi >= 0)
                    sizeLine = sizeLine.Substring(0, semi);
                sizeLine = sizeLine.Trim();
                if (sizeLine.Length == 0 || !TryParseHexLong(sizeLine, out long chunkSize) || chunkSize < 0)
                {
                    error = "Malformed HTTP chunked length.";
                    return false;
                }

                if (chunkSize == 0)
                {
                    // Drain optional trailer lines until empty line.
                    int trailerBudget = 0;
                    for (int t = 0; t < 64; t++)
                    {
                        string trailer = reader.ReadLineAscii(4096, ref trailerBudget);
                        if (string.IsNullOrEmpty(trailer))
                            break;
                    }
                    return true;
                }

                if (maxBytes > 0 && totalReceived + chunkSize > maxBytes)
                {
                    error = "Chunked body exceeded maximum allowed size (" + maxBytes + " bytes).";
                    return false;
                }

                long remaining = chunkSize;
                while (remaining > 0)
                {
                    int toRead = remaining < buf.Length ? (int)remaining : buf.Length;
                    int read = reader.Read(buf, 0, toRead);
                    if (read <= 0)
                    {
                        error = "Unexpected EOF inside HTTP chunk.";
                        return false;
                    }
                    sink.Write(buf, 0, read);
                    sha.BlockUpdate(buf, 0, read);
                    totalReceived += read;
                    remaining -= read;
                    onProgress?.Invoke(totalReceived, -1);
                }

                // Consume trailing CRLF after chunk payload.
                int crlfBudget = 0;
                string sep = reader.ReadLineAscii(16, ref crlfBudget);
                if (sep == null || sep.Length != 0)
                {
                    error = "Malformed HTTP chunk terminator.";
                    return false;
                }
                lineBudget = 0;
            }
        }

        private static bool TryParseHexLong(string text, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(text) || text.Length > 15)
                return false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                int digit;
                if (c >= '0' && c <= '9') digit = c - '0';
                else if (c >= 'a' && c <= 'f') digit = 10 + (c - 'a');
                else if (c >= 'A' && c <= 'F') digit = 10 + (c - 'A');
                else return false;
                value = (value << 4) | (uint)digit;
            }
            return true;
        }

        private static string ToLowerHex(byte[] bytes)
        {
            const string hex = "0123456789abcdef";
            char[] chars = new char[bytes.Length * 2];
            for (int i = 0; i < bytes.Length; i++)
            {
                chars[i * 2] = hex[(bytes[i] >> 4) & 0xF];
                chars[i * 2 + 1] = hex[bytes[i] & 0xF];
            }
            return new string(chars);
        }

        private static void TryDeleteFile(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }

        private sealed class BufferedHttpReader
        {
            private readonly Stream stream;
            private readonly byte[] buffer = new byte[4096];
            private int pos;
            private int len;

            public BufferedHttpReader(Stream stream)
            {
                this.stream = stream;
            }

            public int Read(byte[] dst, int offset, int count)
            {
                if (pos < len)
                {
                    int avail = len - pos;
                    int copy = count < avail ? count : avail;
                    Buffer.BlockCopy(buffer, pos, dst, offset, copy);
                    pos += copy;
                    return copy;
                }
                return stream.Read(dst, offset, count);
            }

            public string ReadLineAscii(int maxTotalBytes, ref int totalBytes)
            {
                var sb = new StringBuilder(128);
                while (true)
                {
                    int b = ReadByte();
                    if (b < 0)
                        return sb.Length > 0 ? sb.ToString() : null;

                    totalBytes++;
                    if (totalBytes > maxTotalBytes)
                        return sb.ToString();

                    if (b == '\n')
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] == '\r')
                            sb.Length--;
                        return sb.ToString();
                    }

                    sb.Append((char)b);
                }
            }

            private int ReadByte()
            {
                if (pos >= len)
                {
                    pos = 0;
                    len = stream.Read(buffer, 0, buffer.Length);
                    if (len <= 0)
                        return -1;
                }
                return buffer[pos++];
            }
        }
    }
}
