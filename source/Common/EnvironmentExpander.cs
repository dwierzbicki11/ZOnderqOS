using System.Text;

namespace ZonderqOS
{
    public static class EnvironmentExpander
    {
        public static string Expand(string input, string currentPath)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            StringBuilder result = new StringBuilder(input.Length + 32);

            for (int i = 0; i < input.Length; i++)
            {
                char c = input[i];

                if (c == '~' && (i == 0 || char.IsWhiteSpace(input[i - 1]) || input[i - 1] == '='))
                {
                    result.Append(EnvironmentManager.Get("HOME"));
                    continue;
                }

                if (c != '$')
                {
                    result.Append(c);
                    continue;
                }

                int start = i + 1;
                if (start >= input.Length)
                {
                    result.Append('$');
                    continue;
                }

                int end = start;
                if (!(char.IsLetter(input[end]) || input[end] == '_'))
                {
                    result.Append('$');
                    continue;
                }

                end++;
                while (end < input.Length &&
                       (char.IsLetterOrDigit(input[end]) || input[end] == '_'))
                    end++;

                string key = input.Substring(start, end - start);
                result.Append(EnvironmentManager.Get(key));
                i = end - 1;
            }

            return result.ToString();
        }
    }
}
