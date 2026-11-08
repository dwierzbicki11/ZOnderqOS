using ZonderqOS.Hardware;

namespace ZonderqOS.Platform.X64
{
    /// <summary>
    /// Direct PC Speaker driver using PIT Channel 2 and Port 0x61 on x86_64 architecture.
    /// Excluded from ARM64 builds.
    /// </summary>
    public sealed class X64SpeakerDriver : ISpeakerDriver
    {
        private const ushort PitCommandPort = 0x43;
        private const ushort PitChannel2DataPort = 0x42;
        private const ushort SpeakerControlPort = 0x61;
        private const uint PitBaseFrequency = 1193180;

        public void Tone(int frequencyHz)
        {
            if (frequencyHz < 20 || frequencyHz > 20000)
            {
                Stop();
                return;
            }

            uint divisor = PitBaseFrequency / (uint)frequencyHz;
            if (divisor > 0xFFFF)
                divisor = 0xFFFF;

            // PIT Channel 2: lobyte/hibyte, Mode 3 (Square Wave), binary
            X64PortIo.WriteByte(PitCommandPort, 0xB6);
            X64PortIo.WriteByte(PitChannel2DataPort, (byte)(divisor & 0xFF));
            X64PortIo.WriteByte(PitChannel2DataPort, (byte)((divisor >> 8) & 0xFF));

            // Enable speaker bits (bit 0 = timer 2 gate, bit 1 = speaker data)
            byte current = X64PortIo.ReadByte(SpeakerControlPort);
            if ((current & 0x03) != 0x03)
            {
                X64PortIo.WriteByte(SpeakerControlPort, (byte)(current | 0x03));
            }
        }

        public void Stop()
        {
            byte current = X64PortIo.ReadByte(SpeakerControlPort);
            X64PortIo.WriteByte(SpeakerControlPort, (byte)(current & 0xFC));
        }
    }
}
