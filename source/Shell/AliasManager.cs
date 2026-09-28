using System;
using System.Collections.Generic;

namespace ZonderqOS
{
    public static class AliasManager
    {
        private const int MaxAliases = 64;
        private const int MaxNameLength = 32;
        private const int MaxValueLength = 256;
        private static readonly Dictionary<string,string> Aliases =
            new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        private static readonly object Sync = new object();

        public static bool TrySet(string name, string value)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength ||
                string.IsNullOrWhiteSpace(value) || value.Length > MaxValueLength)
                return false;

            for (int i = 0; i < name.Length; i++)
                if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_' || name[i] == '-'))
                    return false;

            lock (Sync)
            {
                if (!Aliases.ContainsKey(name) && Aliases.Count >= MaxAliases)
                    return false;
                Aliases[name] = value.Trim();
                return true;
            }
        }

        public static bool Remove(string name)
        {
            lock (Sync)
                return Aliases.Remove(name);
        }

        public static string ExpandLeadingAlias(string commandLine)
        {
            if (string.IsNullOrWhiteSpace(commandLine))
                return commandLine;

            string current = commandLine;
            for (int depth = 0; depth < 8; depth++)
            {
                int split = 0;
                while (split < current.Length && !char.IsWhiteSpace(current[split]))
                    split++;
                string head = current.Substring(0, split);

                string value;
                lock (Sync)
                {
                    if (!Aliases.TryGetValue(head, out value))
                        return current;
                }

                string tail = split < current.Length ? current.Substring(split) : string.Empty;
                string next = value + tail;
                if (string.Equals(next, current, StringComparison.Ordinal))
                    return current;
                current = next;
            }

            return current;
        }

        public static void CopyTo(List<KeyValuePair<string,string>> destination)
        {
            lock (Sync)
            {
                destination.Clear();
                foreach (var pair in Aliases)
                    destination.Add(pair);
            }
        }
    }
}
