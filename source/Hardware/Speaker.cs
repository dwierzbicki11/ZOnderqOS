namespace ZonderqOS.Hardware
{
    public interface ISpeakerDriver
    {
        void Tone(int frequencyHz);
        void Stop();
    }

    public static class Speaker
    {
        public static bool Enabled { get; set; } = true;
        public static ISpeakerDriver Driver { get; set; }

        public static void Tone(int frequencyHz)
        {
            if (!Enabled || Driver == null)
                return;

            try
            {
                Driver.Tone(frequencyHz);
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
                Driver?.Stop();
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

            Tone(frequencyHz);
            // Brief tone will be stopped by next tick or Stop()
        }
    }
}
