using System;
using System.Collections.Generic;
using System.IO;

namespace ZonderqOS
{
    public sealed class ShellCompletionResult
    {
        public string Text { get; }
        public int CursorPosition { get; }
        public string[] Matches { get; }
        public bool Changed { get; }

        public ShellCompletionResult(string text, int cursorPosition, string[] matches, bool changed)
        {
            Text = text ?? string.Empty;
            CursorPosition = cursorPosition;
            Matches = matches ?? new string[0];
            Changed = changed;
        }
    }

    /// <summary>
    /// Shared completion engine for both the recovery console and graphical terminal.
    /// It completes command names at the start of each shell segment and filesystem
    /// entries everywhere else. The implementation avoids LINQ and reflection so a
    /// completion request stays predictable on the Cosmos runtime.
    /// </summary>
    public static class ShellCompletion
    {
        public static ShellCompletionResult Complete(string input, int cursorPosition, string currentPath)
        {
            input ??= string.Empty;
            currentPath = string.IsNullOrEmpty(currentPath) ? "/" : currentPath;

            if (cursorPosition < 0)
                cursorPosition = 0;
            else if (cursorPosition > input.Length)
                cursorPosition = input.Length;

            CompletionContext context = Analyze(input, cursorPosition);
            string rawToken = input.Substring(context.TokenStart, context.TokenEnd - context.TokenStart);
            bool commandPosition = IsCommandPosition(input, context.SegmentStart, context.TokenStart);

            List<Candidate> candidates = commandPosition
                ? FindCommandCandidates(rawToken)
                : FindPathCandidates(rawToken, currentPath);

            SortCandidates(candidates);
            string[] displays = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
                displays[i] = candidates[i].Display;

            if (candidates.Count == 0)
                return new ShellCompletionResult(input, cursorPosition, displays, false);

            string replacement;
            if (candidates.Count == 1)
            {
                Candidate candidate = candidates[0];
                replacement = candidate.Replacement;

                if (commandPosition)
                {
                    replacement += " ";
                }
                else if (candidate.IsDirectory)
                {
                    replacement = KeepDirectoryOpen(replacement, rawToken);
                }
                else
                {
                    replacement = CloseQuotedFile(replacement, rawToken) + " ";
                }
            }
            else
            {
                replacement = LongestCommonPrefix(candidates);
                if (replacement.Length < rawToken.Length)
                    replacement = rawToken;

                if (!StartsWithQuote(rawToken) && replacement.IndexOf(' ') >= 0 && !StartsWithQuote(replacement))
                    replacement = "\"" + replacement;
            }

            string updated = input.Substring(0, context.TokenStart) + replacement + input.Substring(context.TokenEnd);
            int newCursor = context.TokenStart + replacement.Length;
            bool changed = !string.Equals(updated, input, StringComparison.Ordinal) || newCursor != cursorPosition;
            return new ShellCompletionResult(updated, newCursor, displays, changed);
        }

        private static List<Candidate> FindCommandCandidates(string rawToken)
        {
            var matches = new List<Candidate>();
            string prefix = rawToken ?? string.Empty;
            string[] names = Command.GetCommandNames();

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    matches.Add(new Candidate(name, name, false));
            }

            return matches;
        }

        private static List<Candidate> FindPathCandidates(string rawToken, string currentPath)
        {
            var matches = new List<Candidate>();
            if (rawToken == null)
                rawToken = string.Empty;

            char quote = GetOpeningQuote(rawToken);
            string token = quote == '\0' ? rawToken : rawToken.Substring(1);
            if (quote != '\0' && token.Length > 0 && token[token.Length - 1] == quote)
                token = token.Substring(0, token.Length - 1);

            token = token.Replace('\\', '/');
            int slash = token.LastIndexOf('/');
            string directoryPrefix = slash >= 0 ? token.Substring(0, slash + 1) : string.Empty;
            string namePrefix = slash >= 0 ? token.Substring(slash + 1) : token;
            string lookupDirectory = string.IsNullOrEmpty(directoryPrefix)
                ? currentPath
                : PathResolver.GetAbsolutePath(currentPath, directoryPrefix);

            try
            {
                if (!Directory.Exists(lookupDirectory))
                    return matches;

                string[] directories = Directory.GetDirectories(lookupDirectory);
                for (int i = 0; i < directories.Length; i++)
                {
                    string leaf = LeafName(directories[i]);
                    if (!leaf.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string body = directoryPrefix + leaf + "/";
                    string replacement = quote == '\0' ? body : quote + body;
                    matches.Add(new Candidate(replacement, body, true));
                }

                string[] files = Directory.GetFiles(lookupDirectory);
                for (int i = 0; i < files.Length; i++)
                {
                    string leaf = LeafName(files[i]);
                    if (!leaf.StartsWith(namePrefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    string body = directoryPrefix + leaf;
                    string replacement = quote == '\0' ? body : quote + body;
                    matches.Add(new Candidate(replacement, body, false));
                }
            }
            catch
            {
                // Completion is an optional shell convenience. An inaccessible or
                // temporarily unavailable mount must never break command input.
            }

            return matches;
        }

        private static string KeepDirectoryOpen(string replacement, string rawToken)
        {
            if (StartsWithQuote(rawToken) || StartsWithQuote(replacement))
                return replacement;

            if (replacement.IndexOf(' ') >= 0)
                return "\"" + replacement;

            return replacement;
        }

        private static string CloseQuotedFile(string replacement, string rawToken)
        {
            if (StartsWithQuote(rawToken) || StartsWithQuote(replacement))
            {
                char quote = StartsWithQuote(rawToken) ? rawToken[0] : replacement[0];
                if (replacement.Length == 0 || replacement[replacement.Length - 1] != quote)
                    return replacement + quote;
                return replacement;
            }

            if (replacement.IndexOf(' ') >= 0)
                return "\"" + replacement + "\"";

            return replacement;
        }

        private static CompletionContext Analyze(string input, int cursorPosition)
        {
            int tokenStart = 0;
            int segmentStart = 0;
            bool singleQuote = false;
            bool doubleQuote = false;
            bool escaped = false;

            for (int i = 0; i < cursorPosition; i++)
            {
                char c = input[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '\'' && !doubleQuote)
                {
                    singleQuote = !singleQuote;
                    continue;
                }

                if (c == '"' && !singleQuote)
                {
                    doubleQuote = !doubleQuote;
                    continue;
                }

                if (singleQuote || doubleQuote)
                    continue;

                if (c == ';' || c == '|' || c == '&')
                {
                    segmentStart = i + 1;
                    tokenStart = i + 1;
                }
                else if (char.IsWhiteSpace(c) || c == '>')
                {
                    tokenStart = i + 1;
                }
            }

            int tokenEnd = FindTokenEnd(input, tokenStart, cursorPosition);
            return new CompletionContext(segmentStart, tokenStart, tokenEnd);
        }

        private static int FindTokenEnd(string input, int tokenStart, int cursorPosition)
        {
            bool singleQuote = false;
            bool doubleQuote = false;
            bool escaped = false;

            for (int i = tokenStart; i < input.Length; i++)
            {
                char c = input[i];
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (c == '\'' && !doubleQuote)
                {
                    singleQuote = !singleQuote;
                    continue;
                }

                if (c == '"' && !singleQuote)
                {
                    doubleQuote = !doubleQuote;
                    continue;
                }

                if (i < cursorPosition || singleQuote || doubleQuote)
                    continue;

                if (char.IsWhiteSpace(c) || c == ';' || c == '|' || c == '&' || c == '>')
                    return i;
            }

            return input.Length;
        }

        private static bool IsCommandPosition(string input, int segmentStart, int tokenStart)
        {
            for (int i = segmentStart; i < tokenStart; i++)
            {
                if (!char.IsWhiteSpace(input[i]))
                    return false;
            }
            return true;
        }

        private static string LongestCommonPrefix(List<Candidate> candidates)
        {
            if (candidates.Count == 0)
                return string.Empty;

            string prefix = candidates[0].Replacement;
            for (int i = 1; i < candidates.Count && prefix.Length > 0; i++)
            {
                string value = candidates[i].Replacement;
                int length = Math.Min(prefix.Length, value.Length);
                int j = 0;
                while (j < length && char.ToUpperInvariant(prefix[j]) == char.ToUpperInvariant(value[j]))
                    j++;
                prefix = prefix.Substring(0, j);
            }
            return prefix;
        }

        private static void SortCandidates(List<Candidate> candidates)
        {
            for (int i = 1; i < candidates.Count; i++)
            {
                Candidate value = candidates[i];
                int j = i - 1;
                while (j >= 0 && string.Compare(candidates[j].Display, value.Display, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    candidates[j + 1] = candidates[j];
                    j--;
                }
                candidates[j + 1] = value;
            }
        }

        private static string LeafName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            string normalized = path.Replace('\\', '/').TrimEnd('/');
            int slash = normalized.LastIndexOf('/');
            return slash >= 0 ? normalized.Substring(slash + 1) : normalized;
        }

        private static char GetOpeningQuote(string value)
        {
            if (string.IsNullOrEmpty(value))
                return '\0';
            return value[0] == '"' || value[0] == '\'' ? value[0] : '\0';
        }

        private static bool StartsWithQuote(string value)
        {
            return !string.IsNullOrEmpty(value) && (value[0] == '"' || value[0] == '\'');
        }

        private sealed class Candidate
        {
            public string Replacement { get; }
            public string Display { get; }
            public bool IsDirectory { get; }

            public Candidate(string replacement, string display, bool isDirectory)
            {
                Replacement = replacement;
                Display = display;
                IsDirectory = isDirectory;
            }
        }

        private readonly struct CompletionContext
        {
            public int SegmentStart { get; }
            public int TokenStart { get; }
            public int TokenEnd { get; }

            public CompletionContext(int segmentStart, int tokenStart, int tokenEnd)
            {
                SegmentStart = segmentStart;
                TokenStart = tokenStart;
                TokenEnd = tokenEnd;
            }
        }
    }
}
