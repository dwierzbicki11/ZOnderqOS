using System;
using System.Diagnostics;

namespace ZonderqOS
{
    public enum NotificationKind
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3,
        Security = 4,
        Network = 5,
        Storage = 6
    }

    public struct NotificationEntry
    {
        public ulong Id;
        public NotificationKind Kind;
        public string Source;
        public string Title;
        public string Message;
        public string TimeText;
        public bool IsRead;
        public bool IsDismissed;
    }

    /// <summary>
    /// Session-local notification store. A fixed ring buffer keeps memory bounded and
    /// avoids background allocations. Entries are created only when an actual event is
    /// posted; the GUI reads them without cloning collections every frame.
    /// </summary>
    public static class NotificationService
    {
        public const int Capacity = 64;
        private const int ToastSeconds = 6;

        private static readonly NotificationEntry[] entries = new NotificationEntry[Capacity];
        private static int writeIndex;
        private static int count;
        private static int unreadCount;
        private static ulong nextId = 1;
        private static int version;
        private static bool doNotDisturb;
        private static ulong latestId;
        private static long latestTimestamp;

        public static int Count { get { return count; } }
        public static int UnreadCount { get { return unreadCount; } }
        public static int Version { get { return version; } }
        public static bool DoNotDisturb
        {
            get { return doNotDisturb; }
            set
            {
                if (doNotDisturb == value)
                    return;
                doNotDisturb = value;
                version++;
            }
        }

        public static void ResetForSession()
        {
            for (int i = 0; i < entries.Length; i++)
                entries[i] = default(NotificationEntry);

            writeIndex = 0;
            count = 0;
            unreadCount = 0;
            latestId = 0;
            latestTimestamp = 0;
            doNotDisturb = false;
            version++;
        }

        public static ulong Post(NotificationKind kind, string source, string title, string message)
        {
            if (string.IsNullOrEmpty(title))
                title = "Powiadomienie";
            if (source == null)
                source = "SYSTEM";
            if (message == null)
                message = string.Empty;

            if (count == Capacity)
            {
                NotificationEntry overwritten = entries[writeIndex];
                if (!overwritten.IsRead && !overwritten.IsDismissed && unreadCount > 0)
                    unreadCount--;
            }
            else
            {
                count++;
            }

            DateTime localTime = DateTime.UtcNow.AddHours(SystemSettings.TimeZoneOffsetHours);
            ulong id = nextId++;
            if (nextId == 0)
                nextId = 1;

            entries[writeIndex] = new NotificationEntry
            {
                Id = id,
                Kind = kind,
                Source = source,
                Title = title,
                Message = message,
                TimeText = localTime.ToString("HH:mm"),
                IsRead = false,
                IsDismissed = false
            };

            writeIndex++;
            if (writeIndex >= Capacity)
                writeIndex = 0;

            unreadCount++;
            latestId = id;
            latestTimestamp = Stopwatch.GetTimestamp();
            version++;
            return id;
        }

        public static bool TryGetNewest(int newestOffset, out NotificationEntry entry)
        {
            entry = default(NotificationEntry);
            if (newestOffset < 0 || newestOffset >= count)
                return false;

            int index = writeIndex - 1 - newestOffset;
            while (index < 0)
                index += Capacity;

            entry = entries[index];
            return entry.Id != 0;
        }

        public static bool TryGetById(ulong id, out NotificationEntry entry)
        {
            entry = default(NotificationEntry);
            if (id == 0)
                return false;

            for (int i = 0; i < count; i++)
            {
                int index = writeIndex - 1 - i;
                while (index < 0)
                    index += Capacity;
                if (entries[index].Id != id)
                    continue;
                entry = entries[index];
                return true;
            }
            return false;
        }

        public static void MarkRead(ulong id)
        {
            if (id == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                int index = writeIndex - 1 - i;
                while (index < 0)
                    index += Capacity;

                NotificationEntry entry = entries[index];
                if (entry.Id != id)
                    continue;
                if (!entry.IsRead && !entry.IsDismissed)
                {
                    entry.IsRead = true;
                    entries[index] = entry;
                    if (unreadCount > 0)
                        unreadCount--;
                    version++;
                }
                return;
            }
        }

        public static void MarkAllRead()
        {
            bool changed = false;
            for (int i = 0; i < count; i++)
            {
                int index = writeIndex - 1 - i;
                while (index < 0)
                    index += Capacity;

                NotificationEntry entry = entries[index];
                if (entry.Id == 0 || entry.IsRead || entry.IsDismissed)
                    continue;
                entry.IsRead = true;
                entries[index] = entry;
                changed = true;
            }

            if (!changed)
                return;
            unreadCount = 0;
            version++;
        }

        public static void Dismiss(ulong id)
        {
            if (id == 0)
                return;

            for (int i = 0; i < count; i++)
            {
                int index = writeIndex - 1 - i;
                while (index < 0)
                    index += Capacity;

                NotificationEntry entry = entries[index];
                if (entry.Id != id)
                    continue;
                if (!entry.IsDismissed)
                {
                    if (!entry.IsRead && unreadCount > 0)
                        unreadCount--;
                    entry.IsRead = true;
                    entry.IsDismissed = true;
                    entries[index] = entry;
                    version++;
                }
                return;
            }
        }

        public static void ClearAll()
        {
            for (int i = 0; i < entries.Length; i++)
                entries[i] = default(NotificationEntry);
            writeIndex = 0;
            count = 0;
            unreadCount = 0;
            latestId = 0;
            latestTimestamp = 0;
            version++;
        }

        public static bool TryGetActiveToast(out NotificationEntry entry)
        {
            entry = default(NotificationEntry);
            if (doNotDisturb || latestId == 0 || latestTimestamp <= 0 || Stopwatch.Frequency <= 0)
                return false;

            long elapsed = Stopwatch.GetTimestamp() - latestTimestamp;
            if (elapsed < 0 || elapsed > Stopwatch.Frequency * ToastSeconds)
                return false;

            if (!TryGetById(latestId, out entry))
                return false;
            return !entry.IsDismissed;
        }
    }
}
