using System.Threading;

namespace ZonderqOS.Hardware
{
    public interface ISpeakerDriver
    {
        void Tone(int frequencyHz);
        void Stop();
    }

    public static class Speaker
    {
        private const int MinFrequencyHz = 20;
        private const int MaxFrequencyHz = 20000;
        private const int FrameDurationMs = 16;
        private const int MaxDurationFrames = 300;
        private const int MaxDurationMs = 5000;
        private static readonly object Sync = new object();

        public static bool Enabled { get; set; } = true;
        public static ISpeakerDriver Driver { get; set; }

        public static void Tone(int frequencyHz)
        {
            if (!Enabled)
                return;

            if (frequencyHz < MinFrequencyHz || frequencyHz > MaxFrequencyHz)
            {
                Stop();
                return;
            }

            try
            {
                lock (Sync)
                {
                    if (!Enabled || Driver == null)
                        return;
                    Driver.Tone(frequencyHz);
                }
            }
            catch
            {
                // Never crash on audio I/O
            }
        }

        public static void Stop()
        {
            try
            {
                lock (Sync)
                {
                    Driver?.Stop();
                }
            }
            catch
            {
                // Never crash on audio I/O
            }
        }

        public static void Beep(int frequencyHz, int durationFrames)
        {
            if (!Enabled || Driver == null)
                return;

            if (durationFrames <= 0 || frequencyHz < MinFrequencyHz || frequencyHz > MaxFrequencyHz)
            {
                Stop();
                return;
            }

            int clampedFrames = durationFrames > MaxDurationFrames ? MaxDurationFrames : durationFrames;
            long durationMsLong = (long)clampedFrames * FrameDurationMs;
            if (durationMsLong > MaxDurationMs)
                durationMsLong = MaxDurationMs;
            int durationMs = (int)durationMsLong;

            try
            {
                Tone(frequencyHz);
                Thread.Sleep(durationMs);
            }
            finally
            {
                Stop();
            }
        }
    }
}
