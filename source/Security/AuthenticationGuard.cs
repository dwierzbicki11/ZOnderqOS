using System;
using System.Diagnostics;

namespace ZonderqOS
{
    /// <summary>
    /// Shared in-memory authentication throttling used by graphical login, unlock and
    /// account switching. Fixed arrays keep the hot path deterministic and avoid an
    /// ever-growing dictionary. State intentionally resets on reboot so a local user can
    /// always recover access without a persistent lockout file.
    /// </summary>
    public static class AuthenticationGuard
    {
        private const int MaxTrackedUsers = 16;
        private const int MaxDelaySeconds = 30;

        private static readonly string[] users = new string[MaxTrackedUsers];
        private static readonly byte[] failures = new byte[MaxTrackedUsers];
        private static readonly long[] blockedUntil = new long[MaxTrackedUsers];
        private static int nextReplacement;

        public static bool CanAttempt(string username, out int retryAfterSeconds)
        {
            retryAfterSeconds = 0;
            int index = Find(username);
            if (index < 0)
                return true;

            long until = blockedUntil[index];
            if (until <= 0 || Stopwatch.Frequency <= 0)
                return true;

            long now = Stopwatch.GetTimestamp();
            if (now >= until)
            {
                blockedUntil[index] = 0;
                return true;
            }

            long remaining = until - now;
            retryAfterSeconds = (int)((remaining + Stopwatch.Frequency - 1) / Stopwatch.Frequency);
            if (retryAfterSeconds < 1)
                retryAfterSeconds = 1;
            return false;
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
            if (index < 0)
                return;

            if (failures[index] < byte.MaxValue)
                failures[index]++;

            int failureCount = failures[index];
            if (failureCount < 3 || Stopwatch.Frequency <= 0)
                return;

            int shift = failureCount - 3;
            if (shift > 4)
                shift = 4;

            int delaySeconds = 2 << shift; // 2, 4, 8, 16, 32 -> capped below.
            if (delaySeconds > MaxDelaySeconds)
                delaySeconds = MaxDelaySeconds;

            long now = Stopwatch.GetTimestamp();
            blockedUntil[index] = now + Stopwatch.Frequency * delaySeconds;
        }

        public static void RecordSuccess(string username)
        {
            int index = Find(username);
            if (index < 0)
                return;

            failures[index] = 0;
            blockedUntil[index] = 0;
        }

        public static void Reset(string username)
        {
            RecordSuccess(username);
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
