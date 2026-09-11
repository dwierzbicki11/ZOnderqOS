using System;

namespace ZonderqOS
{
    /// <summary>
    /// Shared password rules for local ZOnderqOS accounts. Keeping the policy in one
    /// place prevents the graphical account tools, first-boot setup and shell commands
    /// from accepting different credentials.
    /// </summary>
    public static class PasswordPolicy
    {
        public const int MinLength = 8;
        public const int MaxLength = 128;
        public const string Summary = "MIN 8 ZNAKOW, CO NAJMNIEJ 1 LITERA I 1 CYFRA";

        public static bool Validate(string password, out string reason)
        {
            if (password == null)
            {
                reason = "HASLO JEST WYMAGANE";
                return false;
            }

            if (password.Length < MinLength)
            {
                reason = "HASLO MUSI MIEC MINIMUM 8 ZNAKOW";
                return false;
            }

            if (password.Length > MaxLength)
            {
                reason = "HASLO JEST ZA DLUGIE";
                return false;
            }

            bool hasLetter = false;
            bool hasDigit = false;

            for (int i = 0; i < password.Length; i++)
            {
                char ch = password[i];
                if (ch < 32 || ch > 126)
                {
                    reason = "HASLO MOZE UZYWAC TYLKO DRUKOWALNYCH ZNAKOW ASCII";
                    return false;
                }

                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                    hasLetter = true;
                else if (ch >= '0' && ch <= '9')
                    hasDigit = true;
            }

            if (!hasLetter || !hasDigit)
            {
                reason = "HASLO MUSI ZAWIERAC LITERE I CYFRE";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
