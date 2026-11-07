namespace ZonderqOS.Audio
{
    public readonly struct Note
    {
        public readonly int Frequency; // Hz (0 = pause/rest)
        public readonly int DurationMs; // duration in milliseconds

        public Note(int frequency, int durationMs)
        {
            Frequency = frequency;
            DurationMs = durationMs;
        }
    }
}
