using System;
using System.Diagnostics;

namespace ZonderqOS
{
    /// <summary>
    /// Shared in-memory authentication throttling used by graphical login, unlock,
    /// console recovery and account switching. Fixed arrays prevent unbounded allocation.
    /// A global limiter complements per-user limits so cycling fake usernames cannot evict
    /// one account from the small tracking table and bypass throttling.
    /// </summary>
    public static class AuthenticationGuard
    {
        private const int MaxTrackedUsers = 16;
        private const int MaxDelaySeconds = 30;

        private static readonly string[] users = new string[MaxTrackedUsers];
        private static readonly byte[] failures = new byte[MaxTrackedUsers];
        private static readonly long[] blockedUntil = new long[MaxTrackedUsers];
        private static int nextReplacement;
        private static byte globalFailures;
        private static long globalBlockedUntil;

        public static bool CanAttempt(string username, out int retryAfterSeconds)
        {
            retryAfterSeconds = RemainingSeconds(globalBlockedUntil);
            if (retryAfterSeconds > 0)
                return false;

            int index = Find(username);
            if (index < 0)
                return true;

            retryAfterSeconds = RemainingSeconds(blockedUntil[index]);
            if (retryAfterSeconds > 0)
                return false;

            blockedUntil[index] = 0;
            return true;
        }

        public static int GetRetryAfterSeconds(string username)
        {
            int seconds;
            CanAttempt(username, out seconds);
            return seconds;
        }

        public static void RecordFailure(string username)
        {
            int index = FindOrCreate(username);
            if (index >= 0)
            {
                if (failures[index] < byte.MaxValue)
                    failures[index]++;
                ApplyDelay(failures[index], index);
            }

            if (globalFailures < byte.MaxValue)
                globalFailures++;

            // Global throttling starts later than per-account throttling so a typo on one
            // account does not inconvenience every user, but sustained spraying is slowed.
            if (globalFailures >= 8 && Stopwatch.Frequency > 0)
            {
                int extra = globalFailures - 8;
                if (extra > 4)
                    extra = 4;
                int seconds = 2 << extra;
                if (seconds > MaxDelaySeconds)
                    seconds = MaxDelaySeconds;
                globalBlockedUntil = Stopwatch.GetTimestamp() + Stopwatch.Frequency * seconds;
            }
        }

        public static void RecordSuccess(string username)
        {
            int index = Find(username);
            if (index >= 0)
            {
                failures[index] = 0;
                blockedUntil[index] = 0;
            }

            // A valid local authentication proves the console is not in a blind spray loop.
            globalFailures = 0;
            globalBlockedUntil = 0;
        }

        public static void Reset(string username)
        {
            int index = Find(username);
            if (index >= 0)
            {
                failures[index] = 0;
                blockedUntil[index] = 0;
            }
        }

        private static void ApplyDelay(int failureCount, int index)
        {
            if (failureCount < 3 || Stopwatch.Frequency <= 0 || index < 0)
                return;

            int shift = failureCount - 3;
            if (shift > 4)
                shift = 4;

            int delaySeconds = 2 << shift;
            if (delaySeconds > MaxDelaySeconds)
                delaySeconds = MaxDelaySeconds;

            blockedUntil[index] = Stopwatch.GetTimestamp() + Stopwatch.Frequency * delaySeconds;
        }

        private static int RemainingSeconds(long until)
        {
            if (until <= 0 || Stopwatch.Frequency <= 0)
                return 0;

            long now = Stopwatch.GetTimestamp();
            if (now >= until)
                return 0;

            long remaining = until - now;
            int seconds = (int)((remaining + Stopwatch.Frequency - 1) / Stopwatch.Frequency);
            return seconds < 1 ? 1 : seconds;
        }

        private static int Find(string username)
        {
            if (string.IsNullOrEmpty(username))
                return -1;

            for (int i = 0; i < MaxTrackedUsers; i++)
            {
                if (users[i] == username)
                    return i;
            }

            return -1;
        }

        private static int FindOrCreate(string username)
        {
            if (string.IsNullOrEmpty(username))
                username = "?";

            int existing = Find(username);
            if (existing >= 0)
                return existing;

            for (int i = 0; i < MaxTrackedUsers; i++)
            {
                if (users[i] == null)
                {
                    users[i] = username;
                    failures[i] = 0;
                    blockedUntil[i] = 0;
                    return i;
                }
            }

            int replacement = nextReplacement++;
            if (nextReplacement >= MaxTrackedUsers)
                nextReplacement = 0;

            users[replacement] = username;
            failures[replacement] = 0;
            blockedUntil[replacement] = 0;
            return replacement;
        }
    }
}
