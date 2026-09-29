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
    /// Session-local, bounded and thread-safe notification store.
    /// </summary>
    public static class NotificationService
    {
        public const int Capacity = 64;
        private const int ToastSeconds = 6;
        private const int MaxSourceLength = 24;
        private const int MaxTitleLength = 96;
        private const int MaxMessageLength = 240;

        private static readonly object Sync = new object();
        private static readonly NotificationEntry[] entries = new NotificationEntry[Capacity];

        private static int writeIndex;
        private static int count;
        private static int unreadCount;
        private static ulong nextId = 1;
        private static int version;
        private static bool doNotDisturb;
        private static ulong latestId;
        private static long latestTimestamp;

        public static int Count
        {
            get { lock (Sync) return count; }
        }

        public static int UnreadCount
        {
            get { lock (Sync) return unreadCount; }
        }

        public static int Version
        {
            get { lock (Sync) return version; }
        }

        public static bool DoNotDisturb
        {
            get { lock (Sync) return doNotDisturb; }
            set
            {
                lock (Sync)
                {
                    if (doNotDisturb == value)
                        return;
                    doNotDisturb = value;
                    version++;
                }
            }
        }

        public static void ResetForSession()
        {
            lock (Sync)
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
        }

        public static ulong Post(NotificationKind kind, string source, string title, string message)
        {
            lock (Sync)
            {
                source = Normalize(source, "SYSTEM", MaxSourceLength);
                title = Normalize(title, "Powiadomienie", MaxTitleLength);
                message = Normalize(message, string.Empty, MaxMessageLength);

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
        }

        public static bool TryGetNewest(int newestOffset, out NotificationEntry entry)
        {
            lock (Sync)
            {
                entry = default(NotificationEntry);
                if (newestOffset < 0 || newestOffset >= count)
                    return false;

                int index = NormalizeIndex(writeIndex - 1 - newestOffset);
                entry = entries[index];
                return entry.Id != 0;
            }
        }

        public static bool TryGetById(ulong id, out NotificationEntry entry)
        {
            lock (Sync)
                return TryGetByIdLocked(id, out entry);
        }

        public static void MarkRead(ulong id)
        {
            if (id == 0)
                return;

            lock (Sync)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = NormalizeIndex(writeIndex - 1 - i);
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
        }

        public static void MarkAllRead()
        {
            lock (Sync)
            {
                bool changed = false;
                for (int i = 0; i < count; i++)
                {
                    int index = NormalizeIndex(writeIndex - 1 - i);
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
        }

        public static void Dismiss(ulong id)
        {
            if (id == 0)
                return;

            lock (Sync)
            {
                for (int i = 0; i < count; i++)
                {
                    int index = NormalizeIndex(writeIndex - 1 - i);
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
        }

        public static void ClearAll()
        {
            lock (Sync)
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
        }

        public static bool TryGetActiveToast(out NotificationEntry entry)
        {
            lock (Sync)
            {
                entry = default(NotificationEntry);

                if (doNotDisturb || latestId == 0 || latestTimestamp <= 0 || Stopwatch.Frequency <= 0)
                    return false;

                long elapsed = Stopwatch.GetTimestamp() - latestTimestamp;
                if (elapsed < 0 || elapsed > Stopwatch.Frequency * ToastSeconds)
                    return false;

                if (!TryGetByIdLocked(latestId, out entry))
                    return false;

                return !entry.IsDismissed;
            }
        }

        private static bool TryGetByIdLocked(ulong id, out NotificationEntry entry)
        {
            entry = default(NotificationEntry);
            if (id == 0)
                return false;

            for (int i = 0; i < count; i++)
            {
                int index = NormalizeIndex(writeIndex - 1 - i);
                if (entries[index].Id != id)
                    continue;

                entry = entries[index];
                return true;
            }

            return false;
        }

        private static int NormalizeIndex(int index)
        {
            while (index < 0)
                index += Capacity;
            while (index >= Capacity)
                index -= Capacity;
            return index;
        }

        private static string Normalize(string value, string fallback, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                value = fallback;

            value = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length > maxLength)
                value = value.Substring(0, maxLength);
            return value;
        }
    }
}
