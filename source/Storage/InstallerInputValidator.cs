using System;

namespace ZonderqOS.Storage
{
    /// <summary>
    /// Validates user account, hostname, and explicit disk confirmation inputs
    /// for ZonderqOS Installer and Disk Manager without logging sensitive values.
    /// </summary>
    public static class InstallerInputValidator
    {
        public static bool ValidateUsername(string username, out string error)
        {
            if (string.IsNullOrEmpty(username))
            {
                error = "Username cannot be empty.";
                return false;
            }

            if (username.Length > 32)
            {
                error = "Username cannot exceed 32 characters.";
                return false;
            }

            if (string.Equals(username, "root", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(username, "system", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(username, "daemon", StringComparison.OrdinalIgnoreCase))
            {
                error = "Reserved system account name cannot be used as the primary user.";
                return false;
            }

            char first = username[0];
            if (!IsAsciiLetter(first))
            {
                error = "Username must start with an ASCII letter (a-z or A-Z).";
                return false;
            }

            for (int i = 0; i < username.Length; i++)
            {
                char c = username[i];
                if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '_' && c != '-')
                {
                    error = "Username may only contain ASCII letters, digits, '_' or '-'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static bool ValidateHostname(string hostname, out string error)
        {
            if (string.IsNullOrEmpty(hostname))
            {
                error = "Computer name cannot be empty.";
                return false;
            }

            if (hostname.Length > 32)
            {
                error = "Computer name cannot exceed 32 characters.";
                return false;
            }

            if (hostname[0] == '-' || hostname[hostname.Length - 1] == '-')
            {
                error = "Computer name cannot start or end with '-'.";
                return false;
            }

            for (int i = 0; i < hostname.Length; i++)
            {
                char c = hostname[i];
                if (!IsAsciiLetter(c) && !IsAsciiDigit(c) && c != '-')
                {
                    error = "Computer name may only contain ASCII letters, digits, or '-'.";
                    return false;
                }
            }

            error = null;
            return true;
        }

        public static bool ValidateDiskConfirmation(string expectedDiskId, string typedConfirmation)
        {
            if (string.IsNullOrWhiteSpace(expectedDiskId) || string.IsNullOrWhiteSpace(typedConfirmation))
                return false;

            return string.Equals(
                expectedDiskId.Trim(),
                typedConfirmation.Trim(),
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsAsciiLetter(char c)
        {
            return (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z');
        }

        private static bool IsAsciiDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
