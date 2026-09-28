using System.Collections.Generic;

namespace ZonderqOS
{
    public static class ShellHistory
    {
        private const int Capacity = 128;
        private static readonly List<string> Entries = new List<string>(Capacity);
        private static readonly object Sync = new object();

        public static void Add(string command)
        {
            if (string.IsNullOrWhiteSpace(command) ||
                SensitiveCommandPolicy.ContainsPasswordBearingCommand(command))
                return;

            lock (Sync)
            {
                if (Entries.Count > 0 && Entries[Entries.Count - 1] == command)
                    return;
                if (Entries.Count >= Capacity)
                    Entries.RemoveAt(0);
                Entries.Add(command);
            }
        }

        public static void CopyTo(List<string> destination)
        {
            lock (Sync)
            {
                destination.Clear();
                destination.AddRange(Entries);
            }
        }

        public static void Clear()
        {
            lock (Sync)
                Entries.Clear();
        }
    }
}
