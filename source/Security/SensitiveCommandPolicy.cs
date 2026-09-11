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

            return ContainsToken(input, "su") || ContainsToken(input, "useradd");
        }

        private static bool ContainsToken(string input, string command)
        {
            int commandLength = command.Length;
            for (int i = 0; i + commandLength <= input.Length; i++)
            {
                if (!MatchesAtIgnoreCase(input, command, i))
                    continue;

                bool leftBoundary = i == 0 || IsBoundary(input[i - 1]);
                int end = i + commandLength;
                bool rightBoundary = end >= input.Length || IsBoundary(input[end]);
                if (leftBoundary && rightBoundary)
                    return true;
            }

            return false;
        }

        private static bool MatchesAtIgnoreCase(string input, string value, int index)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char a = input[index + i];
                char b = value[i];
                if (a >= 'A' && a <= 'Z')
                    a = (char)(a + ('a' - 'A'));
                if (b >= 'A' && b <= 'Z')
                    b = (char)(b + ('a' - 'A'));
                if (a != b)
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
