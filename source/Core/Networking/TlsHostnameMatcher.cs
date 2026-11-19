using System;
using System.Collections.Generic;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.X509;

namespace ZonderqOS.SystemCore.Networking
{
    /// <summary>
    /// RFC 6125 hostname and IPv4 SAN/CN matcher for BouncyCastle X.509 certificates.
    /// </summary>
    public static class TlsHostnameMatcher
    {
        public static bool MatchesHostname(X509Certificate cert, string expectedHost)
        {
            if (cert == null || string.IsNullOrWhiteSpace(expectedHost))
                return false;

            string host = expectedHost.Trim().ToLowerInvariant();
            if (host.EndsWith("."))
                host = host.Substring(0, host.Length - 1);
            if (host.Length == 0)
                return false;

            bool isIp = OtaUrlParser.IsIPv4Literal(host);
            bool sawAnyDnsOrIpSan = false;

            try
            {
                var sans = cert.GetSubjectAlternativeNames();
                if (sans != null)
                {
                    foreach (var entry in sans)
                    {
                        if (entry == null || entry.Count < 2)
                            continue;
                        if (!(entry[0] is int tag))
                            continue;
                        string value = entry[1] as string;
                        if (string.IsNullOrWhiteSpace(value))
                            continue;

                        if (tag == 2) // dNSName
                        {
                            sawAnyDnsOrIpSan = true;
                            if (!isIp && MatchDnsPattern(value, host))
                                return true;
                        }
                        else if (tag == 7) // iPAddress
                        {
                            sawAnyDnsOrIpSan = true;
                            if (isIp && string.Equals(value.Trim(), host, StringComparison.OrdinalIgnoreCase))
                                return true;
                        }
                    }
                }
            }
            catch
            {
                // Treat malformed SAN extension as untrusted.
                return false;
            }

            // RFC 6125: if SAN contains any DNS/IP entries, never fall back to CN.
            if (sawAnyDnsOrIpSan || isIp)
                return false;

            try
            {
                IList<string> cns = cert.SubjectDN.GetValueList(X509Name.CN);
                if (cns != null)
                {
                    for (int i = 0; i < cns.Count; i++)
                    {
                        if (MatchDnsPattern(cns[i], host))
                            return true;
                    }
                }
            }
            catch
            {
                return false;
            }

            return false;
        }

        public static bool MatchDnsPattern(string pattern, string host)
        {
            if (string.IsNullOrWhiteSpace(pattern) || string.IsNullOrWhiteSpace(host))
                return false;

            string p = pattern.Trim().ToLowerInvariant();
            string h = host.Trim().ToLowerInvariant();
            if (p.EndsWith("."))
                p = p.Substring(0, p.Length - 1);
            if (h.EndsWith("."))
                h = h.Substring(0, h.Length - 1);
            if (p.Length == 0 || h.Length == 0)
                return false;

            int star = p.IndexOf('*');
            if (star < 0)
                return string.Equals(p, h, StringComparison.Ordinal);

            // Allow ONLY a single leading wildcard label "*.domain.tld".
            if (star != 0 || !p.StartsWith("*.", StringComparison.Ordinal) || p.IndexOf('*', 1) >= 0)
                return false;

            string suffix = p.Substring(1); // ".domain.tld"
            // Require at least two labels after "*." so "*.com" is rejected.
            if (suffix.IndexOf('.', 1) < 0)
                return false;

            if (h.Length <= suffix.Length || !h.EndsWith(suffix, StringComparison.Ordinal))
                return false;

            int matchedPrefixLen = h.Length - suffix.Length;
            if (matchedPrefixLen <= 0)
                return false;

            // Wildcard matches exactly ONE DNS label (no '.' in matched prefix).
            for (int i = 0; i < matchedPrefixLen; i++)
            {
                if (h[i] == '.')
                    return false;
            }
            return true;
        }
    }
}
