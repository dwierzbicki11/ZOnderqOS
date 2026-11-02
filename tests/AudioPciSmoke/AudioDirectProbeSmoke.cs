using System;
using Device = Cosmos.Kernel.System.Audio.AudioDevice;
using ZonderqOS.SystemCore;

// Host-only substitutes for the public stock Cosmos AudioDevice surface.
// Never compiled into the kernel. The production direct probe is linked
// unchanged with ZONDERQ_AUDIO_PROBE in AudioPciSmoke.csproj.
namespace Cosmos.Kernel.HAL.Devices.Audio { }

namespace Cosmos.Kernel.System.Audio
{
    public sealed class HostAudioFormat
    {
        public int Channels = 2;
        public int ChannelSize = 2;
    }

    public sealed class AudioDevice
    {
        public bool IsWithdrawn;
        public string Name = "hda";
        public string DriverName = "HdAudioDriver";
        public object Player;
        public bool IsRunning;
        public uint SampleRate = 48000;
        public HostAudioFormat Format = new HostAudioFormat();
        public int StartCalls;
        public int StopCalls;
        public int WrittenBytes;
        public bool ThrowOnStart;
        public bool ThrowOnWrite;
        public bool StallWrites;

        public void Start()
        {
            StartCalls++;
            if (ThrowOnStart) throw new InvalidOperationException("simulated start failure");
            IsRunning = true;
        }

        public int Write(ReadOnlySpan<byte> bytes)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("simulated write failure");
            if (StallWrites) return 0;
            WrittenBytes += bytes.Length;
            return bytes.Length;
        }

        public void Stop()
        {
            StopCalls++;
            IsRunning = false;
        }
    }
}

namespace Cosmos.Kernel.System.Timers
{
    public static class TimerManager
    {
        public static Action<uint> OnWait;
        public static void Wait(uint milliseconds) => OnWait?.Invoke(milliseconds);
    }
}

internal static class AudioDirectProbeSmoke
{
    private static readonly byte[] Pcm = new byte[16];

    private static void ExpectRefused(Device device, Action<bool, string> check, string label)
    {
        bool result = AudioProbeDirectPlayback.TryPlay(device, Pcm, 48000);
        check(!result && device.StartCalls == 0 && device.StopCalls == 0 &&
            device.WrittenBytes == 0, label);
    }

    public static void Run(Action<bool, string> check)
    {
        // A mismatch must not reconfigure the codec, start DMA or stop a
        // stream we never owned.
        ExpectRefused(new Device { SampleRate = 44100 }, check,
            "direct probe rejects mismatched sample rate without touching device");
        ExpectRefused(new Device { Format = new Cosmos.Kernel.System.Audio.HostAudioFormat
            { Channels = 1 } }, check, "direct probe rejects mono output");
        ExpectRefused(new Device { Format = new Cosmos.Kernel.System.Audio.HostAudioFormat
            { ChannelSize = 1 } }, check, "direct probe rejects 8-bit output");
        ExpectRefused(new Device { Name = "ac97" }, check,
            "direct probe rejects non-HDA controller");
        ExpectRefused(new Device { DriverName = "OtherDriver" }, check,
            "direct probe rejects non-stock driver");
        ExpectRefused(new Device { Player = new object() }, check,
            "direct probe refuses a claimed player");
        ExpectRefused(new Device { IsRunning = true }, check,
            "direct probe refuses a running stream");
        ExpectRefused(new Device { IsWithdrawn = true }, check,
            "direct probe refuses a withdrawn output");

        int waits = 0;
        Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
        {
            check(ms == 86, "successful direct probe waits one HDA ring period");
            waits++;
        };
        try
        {
            var good = new Device();
            check(AudioProbeDirectPlayback.TryPlay(good, Pcm, 48000),
                "direct probe feeds frame-aligned PCM through the real feeder");
            check(good.StartCalls == 1 && good.StopCalls == 1 &&
                !good.IsRunning && good.WrittenBytes == Pcm.Length + 16384 &&
                waits == 1, "direct probe stops after one silent ring and settle");

            var brokenStart = new Device { ThrowOnStart = true };
            bool threw = false;
            try { AudioProbeDirectPlayback.TryPlay(brokenStart, Pcm, 48000); }
            catch (InvalidOperationException) { threw = true; }
            check(threw && brokenStart.StartCalls == 1 &&
                brokenStart.StopCalls == 1 && !brokenStart.IsRunning,
                "failed HDA Start still invokes Stop in finally");

            var brokenWrite = new Device { ThrowOnWrite = true };
            threw = false;
            try { AudioProbeDirectPlayback.TryPlay(brokenWrite, Pcm, 48000); }
            catch (InvalidOperationException) { threw = true; }
            check(threw && brokenWrite.StartCalls == 1 &&
                brokenWrite.StopCalls == 1 && !brokenWrite.IsRunning,
                "failed HDA Write still invokes Stop in finally");

            waits = 0;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 1, "stalled ring waits only one millisecond");
                waits++;
            };
            var stalled = new Device { StallWrites = true };
            check(!AudioProbeDirectPlayback.TryPlay(stalled, Pcm, 48000) &&
                stalled.StartCalls == 1 && stalled.StopCalls == 1 &&
                !stalled.IsRunning && waits == 1000,
                "permanently full HDA ring fails closed and stops device");

            waits = 0;
            var withdrawn = new Device { StallWrites = true };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 1, "withdrawal wait remains bounded");
                waits++;
                withdrawn.IsWithdrawn = true;
            };
            check(!AudioProbeDirectPlayback.TryPlay(withdrawn, Pcm, 48000) &&
                withdrawn.StopCalls == 1 && waits == 1,
                "device withdrawal during ring-full wait fails closed");
        }
        finally
        {
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = null;
        }
    }
}
