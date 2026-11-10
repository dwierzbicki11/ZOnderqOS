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
