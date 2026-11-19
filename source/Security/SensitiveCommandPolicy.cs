using System;

namespace ZonderqOS
{
    /// <summary>
    /// Conservative detector for shell lines that may contain credentials. False positives
    /// are acceptable: omitting a harmless line from history is better than persisting a
    /// password after a compound command such as "echo ok; su user secret".
    /// </summary>
    public static class SensitiveCommandPolicy
    {
        public static bool ContainsPasswordBearingCommand(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;

            // Parse shell tokens while stripping single/double quotes and backslash escapes
            // so obfuscated forms like "su", 'su', \su, s\u, or "user""add" cannot bypass redaction.
            int len = input.Length;
            char[] tokenBuf = new char[16];
            int i = 0;

            while (i < len)
            {
                while (i < len && IsBoundary(input[i]))
                    i++;
                if (i >= len)
                    break;

                int tokenLen = 0;
                bool overflowed = false;
                bool inSingle = false;
                bool inDouble = false;

                while (i < len)
                {
                    char c = input[i];
                    if (!inSingle && !inDouble && IsBoundary(c))
                        break;

                    if (c == '\\' && !inSingle && i + 1 < len)
                    {
                        i++;
                        c = input[i];
                    }
                    else if (c == '\'' && !inDouble)
                    {
                        inSingle = !inSingle;
                        i++;
                        continue;
                    }
                    else if (c == '"' && !inSingle)
                    {
                        inDouble = !inDouble;
                        i++;
                        continue;
                    }

                    if (c >= 'A' && c <= 'Z')
                        c = (char)(c + ('a' - 'A'));

                    if (tokenLen < tokenBuf.Length)
                        tokenBuf[tokenLen++] = c;
                    else
                        overflowed = true;

                    i++;
                }

                if (!overflowed &&
                    (TokenEquals(tokenBuf, tokenLen, "su") ||
                     TokenEquals(tokenBuf, tokenLen, "useradd") ||
                     TokenEquals(tokenBuf, tokenLen, "zinstall")))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool TokenEquals(char[] buf, int length, string expected)
        {
            if (length != expected.Length)
                return false;

            for (int i = 0; i < length; i++)
            {
                if (buf[i] != expected[i])
                    return false;
            }

            return true;
        }

        private static bool IsBoundary(char ch)
        {
            return char.IsWhiteSpace(ch) || ch == ';' || ch == '|' || ch == '&' ||
                   ch == '>' || ch == '<' || ch == '(' || ch == ')';
        }
    }
}
