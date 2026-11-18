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
        public const string Summary = "MIN 8 CHARACTERS, AT LEAST 1 LETTER AND 1 DIGIT";

        public static bool Validate(string password, out string reason)
        {
            if (password == null)
            {
                reason = "PASSWORD IS REQUIRED";
                return false;
            }

            if (password.Length < MinLength)
            {
                reason = "PASSWORD MUST BE AT LEAST 8 CHARACTERS LONG";
                return false;
            }

            if (password.Length > MaxLength)
            {
                reason = "PASSWORD IS TOO LONG";
                return false;
            }

            bool hasLetter = false;
            bool hasDigit = false;

            for (int i = 0; i < password.Length; i++)
            {
                char ch = password[i];
                if (ch < 32 || ch > 126)
                {
                    reason = "PASSWORD MAY CONTAIN ONLY PRINTABLE ASCII CHARACTERS";
                    return false;
                }

                if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
                    hasLetter = true;
                else if (ch >= '0' && ch <= '9')
                    hasDigit = true;
            }

            if (!hasLetter || !hasDigit)
            {
                reason = "PASSWORD MUST CONTAIN A LETTER AND A DIGIT";
                return false;
            }

            reason = string.Empty;
            return true;
        }
    }
}
