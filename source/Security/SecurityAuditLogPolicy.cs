using System;
using System.Text;

namespace ZonderqOS
{
    /// <summary>
    /// Produces single-line, bounded security audit records. Untrusted
    /// messages, user names and severity tokens must not create additional
    /// fake entries in the persistent /var/log/auth.log stream.
    /// </summary>
    internal static class SecurityAuditLogPolicy
    {
        internal static string FormatLine(string timestamp, string severity,
            string username, string message)
        {
            return "[" + SafeField(timestamp, 32, "TIME_ERROR") +
                   "] [" + SafeField(severity, 16, "INFO") +
                   "] [UID:" + SafeField(username, 32, "unknown") + "] " +
                   SystemLogStoragePolicy.BoundMessage(message) + "\n";
        }

        internal static string SafeField(string value, int limit, string fallback)
        {
            if (string.IsNullOrEmpty(value) || limit <= 0)
                return fallback;

            var result = new StringBuilder(Math.Min(value.Length, limit));
            for (int i = 0; i < value.Length && result.Length < limit; i++)
            {
                char c = value[i];
                // Only safe ASCII token characters, plus timestamp punctuation.
                if ((c >= 'a' && c <= 'z') ||
                    (c >= 'A' && c <= 'Z') ||
                    (c >= '0' && c <= '9') ||
                    c == '_' || c == '-' || c == '.' || c == ':' || c == ' ')
                    result.Append(c);
                else
                    result.Append('_');
            }
            return result.Length == 0 ? fallback : result.ToString();
        }
    }
}
