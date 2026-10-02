namespace ZonderqOS
{
    public static class ShellHistory
    {
        private const int Capacity = 128;
        private static readonly string[] Entries = new string[Capacity];
        private static readonly object Sync = new object();
        private static int start;
        private static int count;

        public static void Add(string command)
        {
            if (string.IsNullOrWhiteSpace(command) ||
                SensitiveCommandPolicy.ContainsPasswordBearingCommand(command))
                return;

            lock (Sync)
            {
                if (count > 0 && Entries[(start + count - 1) % Capacity] == command)
                    return;

                if (count < Capacity)
                {
                    Entries[(start + count) % Capacity] = command;
                    count++;
                    return;
                }

                Entries[start] = command;
                start = (start + 1) % Capacity;
            }
        }

        public static int CopyTo(string[] destination)
        {
            if (destination == null)
                return 0;

            lock (Sync)
            {
                int copyCount = count < destination.Length ? count : destination.Length;
                for (int i = 0; i < copyCount; i++)
                    destination[i] = Entries[(start + i) % Capacity];
                return copyCount;
            }
        }

        public static void Clear()
        {
            lock (Sync)
            {
                for (int i = 0; i < count; i++)
                    Entries[(start + i) % Capacity] = null;
                start = 0;
                count = 0;
            }
        }
    }
}
