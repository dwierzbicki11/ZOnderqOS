namespace ZonderqOS.Platform
{
    public static class SpeakerInitializer
    {
        public static void Initialize()
        {
            ZonderqOS.Hardware.Speaker.Driver = new X64.X64SpeakerDriver();
        }
    }
}
