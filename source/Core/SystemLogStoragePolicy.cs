using System;
using System.Text;

namespace ZonderqOS
{
    /// <summary>
    /// Byte-accurate bounded log rotation check. Persisted system logs are
    /// UTF-8; .NET string.Length counts UTF-16 code units, not file bytes.
    /// </summary>
    internal static class SystemLogStoragePolicy
    {
        internal const int MaxMessageChars = 4096;
        private const string TruncatedSuffix = " [truncated]";

        internal static string BoundMessage(string message)
        {
            if (string.IsNullOrEmpty(message))
                return string.Empty;

            // Cap a single hostile or accidentally huge log message before
            // it enters the in-memory ring or the FAT32 journal.
            if (message.Length > MaxMessageChars)
                message = message.Substring(0, MaxMessageChars - TruncatedSuffix.Length) +
                          TruncatedSuffix;

            return message.Replace('\r', ' ').Replace('\n', ' ');
        }

        // A source field is enclosed in [brackets] in the persistent log.
        // Reject line breaks, control characters and closing brackets so a
        // malformed app name cannot forge another log entry.
        internal static string NormalizeSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "SYS";

            string token = source.Trim();
            if (token.Length == 0)
                return "SYS";
            if (token.Length > 16)
                token = token.Substring(0, 16);

            char[] safe = null;
            for (int i = 0; i < token.Length; i++)
            {
                char c = token[i];
                if (char.IsControl(c) || c == '[' || c == ']')
                {
                    if (safe == null)
                        safe = token.ToCharArray();
                    safe[i] = '_';
                }
            }
            return (safe == null ? token : new string(safe)).ToUpperInvariant();
        }

        internal static bool ShouldRotate(long currentBytes, string nextLine, long maxBytes)
        {
            if (currentBytes < 0 || maxBytes <= 0)
                return false; // Fail open for logging when metadata is invalid.

            int additional = Encoding.UTF8.GetByteCount(nextLine ?? string.Empty);
            // Subtraction avoids potential long overflow.
            return currentBytes > maxBytes ||
                additional > maxBytes - currentBytes;
        }
    }
}
