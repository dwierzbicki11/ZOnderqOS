using System;
using System.IO;

namespace ZonderqOS
{
    /// <summary>
    /// Append a local account to both databases without overwriting either
    /// committed file in place. Publishing shadow before passwd means a power
    /// cut between commits leaves a non-loginable shadow-only orphan, not a
    /// passwd-only account. FAT32 does not provide a two-file transaction.
    /// Caller must serialize all account mutations.
    /// </summary>
    internal static class AccountFileTransaction
    {
        internal static bool ContainsUser(string[] lines, string username)
        {
            if (lines == null || string.IsNullOrEmpty(username))
                return false;

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (string.IsNullOrEmpty(line))
                    continue;

                int separator = line.IndexOf(':');
                if (separator == username.Length &&
                    string.CompareOrdinal(line, 0, username, 0, separator) == 0)
                    return true;
            }

            return false;
        }

        internal static void AppendAccount(string passwdPath, string shadowPath,
            string username, string passwdLine, string shadowLine,
            Action<string, int> protectPath)
        {
            if (string.IsNullOrEmpty(username) ||
                username.IndexOf(':') >= 0 || username.IndexOf('\n') >= 0 ||
                username.IndexOf('\r') >= 0)
                throw new ArgumentException("Invalid account name.", nameof(username));

            if (string.IsNullOrEmpty(passwdLine) || string.IsNullOrEmpty(shadowLine) ||
                !passwdLine.StartsWith(username + ":", StringComparison.Ordinal) ||
                !shadowLine.StartsWith(username + ":", StringComparison.Ordinal))
                throw new ArgumentException("Inconsistent account records.");

            if (!File.Exists(passwdPath) || !File.Exists(shadowPath))
                throw new FileNotFoundException("Account database is missing.");

            string[] passwdBefore = File.ReadAllLines(passwdPath);
            string[] shadowBefore = File.ReadAllLines(shadowPath);
            if (passwdBefore.Length == 0 || shadowBefore.Length == 0)
                throw new InvalidDataException("Refusing to modify empty account database.");

            // An orphaned shadow entry from a previous interrupted creation
            // must not be silently reassigned to another account.
            if (ContainsUser(passwdBefore, username) ||
                ContainsUser(shadowBefore, username))
                throw new InvalidOperationException("Account name already present.");

            string[] passwdAfter = new string[passwdBefore.Length + 1];
            string[] shadowAfter = new string[shadowBefore.Length + 1];
            Array.Copy(passwdBefore, passwdAfter, passwdBefore.Length);
            Array.Copy(shadowBefore, shadowAfter, shadowBefore.Length);
            passwdAfter[passwdBefore.Length] = passwdLine.TrimEnd('\r', '\n');
            shadowAfter[shadowBefore.Length] = shadowLine.TrimEnd('\r', '\n');

            // First commit the credential: it cannot grant a login without
            // a corresponding entry in passwd.
            ShadowFileTransaction.Replace(shadowPath, shadowAfter,
                protectPath == null ? null : path => protectPath(path, 600));
            try
            {
                ShadowFileTransaction.Replace(passwdPath, passwdAfter,
                    protectPath == null ? null : path => protectPath(path, 644));
            }
            catch
            {
                // Best-effort ordinary error rollback. After power loss the
                // shadow-only orphan is deliberately left fail-closed.
                try
                {
                    ShadowFileTransaction.Replace(shadowPath, shadowBefore,
                        protectPath == null ? null : path => protectPath(path, 600));
                }
                catch { }
                throw;
            }
        }
    }
}
