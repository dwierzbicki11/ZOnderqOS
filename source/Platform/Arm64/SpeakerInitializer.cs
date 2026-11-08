namespace ZonderqOS.Platform
{
    public static class SpeakerInitializer
    {
        public static void Initialize()
        {
            // ARM64 virt boards do not have an x86 PIT/PC Speaker
            ZonderqOS.Hardware.Speaker.Driver = null;
        }
    }
}
