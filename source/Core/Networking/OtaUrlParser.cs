using System;

namespace ZonderqOS.SystemCore.Networking
{
    public sealed class ParsedOtaUrl
    {
        public string Scheme { get; }
        public string Host { get; }
        public int Port { get; }
        public string PathAndQuery { get; }
        public bool IsHttps => string.Equals(Scheme, "https", StringComparison.Ordinal);
        public string HostHeader => (IsHttps && Port == 443) || (!IsHttps && Port == 80)
            ? Host
            : Host + ":" + Port;

        public ParsedOtaUrl(string scheme, string host, int port, string pathAndQuery)
        {
            Scheme = scheme;
            Host = host;
            Port = port;
            PathAndQuery = pathAndQuery;
        }

        public override string ToString()
        {
            return Scheme + "://" + HostHeader + PathAndQuery;
        }
    }

    /// <summary>
    /// Strict URL parser and redirect resolver for ZonderqOS OTA downloads.
    /// Avoids System.Uri reflection/parser overhead and blocks CRLF injection,
    /// userinfo smuggling, and HTTPS-to-HTTP protocol downgrades.
    /// </summary>
    public static class OtaUrlParser
    {
        public const int MaxUrlLength = 2048;

        public static bool TryParse(string rawUrl, bool allowInsecureHttp, out ParsedOtaUrl parsed, out string error)
        {
            parsed = null;
            error = string.Empty;

            if (string.IsNullOrEmpty(rawUrl))
            {
                error = "URL cannot be empty.";
                return false;
            }

            if (rawUrl.Length > MaxUrlLength)
            {
                error = "URL exceeds maximum length (" + MaxUrlLength + ").";
                return false;
            }

            for (int i = 0; i < rawUrl.Length; i++)
            {
                char ch = rawUrl[i];
                if (ch <= 32 || ch >= 127)
                {
                    error = "URL contains illegal whitespace or control/non-ASCII character.";
                    return false;
                }
            }

            // Strip fragment if present.
            int hashIdx = rawUrl.IndexOf('#');
            string clean = hashIdx >= 0 ? rawUrl.Substring(0, hashIdx) : rawUrl;

            string scheme;
            int authorityStart;
            int defaultPort;
            if (clean.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                scheme = "https";
                authorityStart = 8;
                defaultPort = 443;
            }
            else if (clean.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowInsecureHttp)
                {
                    error = "Plaintext HTTP is not allowed; HTTPS is required.";
                    return false;
                }
                scheme = "http";
                authorityStart = 7;
                defaultPort = 80;
            }
            else
            {
                error = "Unsupported URL scheme (expected https://).";
                return false;
            }

            int pathStart = clean.IndexOf('/', authorityStart);
            int queryStart = clean.IndexOf('?', authorityStart);
            int authorityEnd = clean.Length;
            if (pathStart >= 0 && pathStart < authorityEnd)
                authorityEnd = pathStart;
            if (queryStart >= 0 && queryStart < authorityEnd)
                authorityEnd = queryStart;

            string authority = clean.Substring(authorityStart, authorityEnd - authorityStart);
            if (string.IsNullOrEmpty(authority))
            {
                error = "URL host is missing.";
                return false;
            }

            if (authority.IndexOf('@') >= 0)
            {
                error = "URL userinfo ('@') is not permitted.";
                return false;
            }

            if (authority.IndexOf('[') >= 0 || authority.IndexOf(']') >= 0)
            {
                error = "IPv6 literal hosts are not supported.";
                return false;
            }

            string host;
            int port = defaultPort;
            int colonIdx = authority.IndexOf(':');
            if (colonIdx >= 0)
            {
                if (authority.IndexOf(':', colonIdx + 1) >= 0)
                {
                    error = "Malformed host:port authority.";
                    return false;
                }
                host = authority.Substring(0, colonIdx);
                string portText = authority.Substring(colonIdx + 1);
                if (string.IsNullOrEmpty(portText) || !int.TryParse(portText, out port) || port < 1 || port > 65535)
                {
                    error = "Invalid TCP port in URL.";
                    return false;
                }
            }
            else
            {
                host = authority;
            }

            if (!IsValidHost(host))
            {
                error = "Invalid hostname in URL.";
                return false;
            }

            string pathAndQuery;
            if (authorityEnd >= clean.Length)
            {
                pathAndQuery = "/";
            }
            else if (clean[authorityEnd] == '?')
            {
                pathAndQuery = "/" + clean.Substring(authorityEnd);
            }
            else
            {
                pathAndQuery = clean.Substring(authorityEnd);
            }

            parsed = new ParsedOtaUrl(scheme, host.ToLowerInvariant(), port, pathAndQuery);
            return true;
        }

        public static bool TryResolveRedirect(ParsedOtaUrl current, string location, bool allowInsecureHttp,
            out ParsedOtaUrl next, out string error)
        {
            next = null;
            error = string.Empty;

            if (current == null)
            {
                error = "Current URL is missing.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                error = "Redirect Location header is empty.";
                return false;
            }

            string trimmed = location.Trim();
            string candidate;
            if (trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            {
                candidate = trimmed;
            }
            else if (trimmed.StartsWith("//", StringComparison.Ordinal))
            {
                candidate = current.Scheme + ":" + trimmed;
            }
            else if (trimmed[0] == '/')
            {
                candidate = current.Scheme + "://" + current.HostHeader + trimmed;
            }
            else
            {
                string basePath = current.PathAndQuery;
                int q = basePath.IndexOf('?');
                if (q >= 0)
                    basePath = basePath.Substring(0, q);
                int lastSlash = basePath.LastIndexOf('/');
                string dir = lastSlash >= 0 ? basePath.Substring(0, lastSlash + 1) : "/";
                candidate = current.Scheme + "://" + current.HostHeader + dir + trimmed;
            }

            if (!TryParse(candidate, allowInsecureHttp, out ParsedOtaUrl resolved, out error))
                return false;

            // Never allow silent HTTPS -> HTTP downgrade on redirects.
            if (current.IsHttps && !resolved.IsHttps)
            {
                error = "Refused HTTPS-to-HTTP redirect downgrade.";
                return false;
            }

            next = resolved;
            return true;
        }

        public static string CombineBaseAndRelative(string baseUrl, string relativePath)
        {
            string b = (baseUrl ?? string.Empty).Trim();
            while (b.EndsWith("/"))
                b = b.Substring(0, b.Length - 1);
            string r = (relativePath ?? string.Empty).Trim();
            while (r.StartsWith("/"))
                r = r.Substring(1);
            return b + "/" + r;
        }

        public static bool IsIPv4Literal(string host)
        {
            if (string.IsNullOrEmpty(host))
                return false;
            string[] parts = host.Split('.');
            if (parts.Length != 4)
                return false;
            for (int i = 0; i < 4; i++)
            {
                string p = parts[i];
                if (p.Length == 0 || p.Length > 3)
                    return false;
                for (int j = 0; j < p.Length; j++)
                {
                    if (p[j] < '0' || p[j] > '9')
                        return false;
                }
                if (!int.TryParse(p, out int val) || val < 0 || val > 255)
                    return false;
            }
            return true;
        }

        private static bool IsValidHost(string host)
        {
            if (string.IsNullOrEmpty(host) || host.Length > 253)
                return false;
            if (host[0] == '.' || host[host.Length - 1] == '-' || host[0] == '-')
                return false;
            for (int i = 0; i < host.Length; i++)
            {
                char ch = host[i];
                bool ok = (ch >= 'a' && ch <= 'z') ||
                          (ch >= 'A' && ch <= 'Z') ||
                          (ch >= '0' && ch <= '9') ||
                          ch == '.' || ch == '-';
                if (!ok)
                    return false;
                if (ch == '.' && i > 0 && host[i - 1] == '.')
                    return false;
            }
            return true;
        }
    }
}
