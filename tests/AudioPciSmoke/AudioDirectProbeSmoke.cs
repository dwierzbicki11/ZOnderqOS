using System;
using Device = Cosmos.Kernel.System.Audio.AudioDevice;
using ZonderqOS.SystemCore;

// Host-only substitutes for the public stock Cosmos AudioDevice surface.
// Never compiled into the kernel. The production direct probe is linked
// unchanged with ZONDERQ_AUDIO_PROBE in AudioPciSmoke.csproj.
namespace Cosmos.Kernel.HAL.Devices.Audio { }

namespace Cosmos.Kernel.System.Audio
{
    // Match stock Cosmos 3.0.95: AudioFormat is a struct, not a class.
    // default(HostAudioFormat) represents an invalid/uninitialized format.
    public struct HostAudioFormat
    {
        public int Channels;
        public int ChannelSize;

        public HostAudioFormat(int channels, int channelSize)
        {
            Channels = channels;
            ChannelSize = channelSize;
        }
    }

    public sealed class AudioDevice
    {
        public bool IsWithdrawn;
        public string Name = "hda";
        public string DriverName = "HdAudioDriver";
        public object Player;
        public bool IsRunning;
        public uint SampleRate = 48000;
        private HostAudioFormat _format = new HostAudioFormat(2, 2);
        public Action OnFormatRead;
        public HostAudioFormat Format
        {
            get
            {
                HostAudioFormat snapshot = _format;
                OnFormatRead?.Invoke();
                return snapshot;
            }
            set => _format = value;
        }
        public int StartCalls;
        public int StopCalls;
        public int WrittenBytes;
        public bool ThrowOnStart;
        public bool ThrowOnWrite;
        public bool ThrowOnStop;
        public bool StallWrites;
        public Action OnStart;
        public Action OnWrite;
        public Action OnStalledWrite;

        public void Start()
        {
            StartCalls++;
            if (ThrowOnStart) throw new InvalidOperationException("simulated start failure");
            OnStart?.Invoke();
            IsRunning = true;
        }

        public int Write(ReadOnlySpan<byte> bytes)
        {
            if (ThrowOnWrite) throw new InvalidOperationException("simulated write failure");
            if (StallWrites)
            {
                OnStalledWrite?.Invoke();
                return 0;
            }
            WrittenBytes += bytes.Length;
            OnWrite?.Invoke();
            return bytes.Length;
        }

        public void Stop()
        {
            StopCalls++;
            IsRunning = false;
            if (ThrowOnStop) throw new InvalidOperationException("simulated stop failure");
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
        ExpectRefused(new Device { Format = default }, check,
            "direct probe refuses default/uninitialized PCM format without starting");
        var formatWithdrawnOnRead = new Device();
        formatWithdrawnOnRead.OnFormatRead = () => formatWithdrawnOnRead.Format = default;
        check(!AudioProbeDirectPlayback.TryPlay(formatWithdrawnOnRead, Pcm, 48000) &&
            formatWithdrawnOnRead.StartCalls == 0 &&
            formatWithdrawnOnRead.StopCalls == 0 &&
            formatWithdrawnOnRead.WrittenBytes == 0,
            "direct probe snapshots format once and catches drift on pre-Start recheck");

        var withdrawnOnFormatRead = new Device();
        withdrawnOnFormatRead.OnFormatRead = () => withdrawnOnFormatRead.IsWithdrawn = true;
        check(!AudioProbeDirectPlayback.TryPlay(withdrawnOnFormatRead, Pcm, 48000) &&
            withdrawnOnFormatRead.StartCalls == 0 &&
            withdrawnOnFormatRead.StopCalls == 0 &&
            withdrawnOnFormatRead.WrittenBytes == 0,
            "withdrawal during format getter aborts before Start");

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

            // Start may change the published sample rate or PCM format.
            // No bytes may reach a codec whose format changed during Start.
            var startRateDrift = new Device();
            startRateDrift.OnStart = () => startRateDrift.SampleRate = 44100;
            check(!AudioProbeDirectPlayback.TryPlay(startRateDrift, Pcm, 48000) &&
                startRateDrift.StartCalls == 1 && startRateDrift.StopCalls == 1 &&
                startRateDrift.WrittenBytes == 0 && !startRateDrift.IsRunning,
                "direct probe refuses sample-rate drift during Start before PCM write");

            var startFormatDrift = new Device();
            startFormatDrift.OnStart = () => startFormatDrift.Format =
                new Cosmos.Kernel.System.Audio.HostAudioFormat(2, 1);
            check(!AudioProbeDirectPlayback.TryPlay(startFormatDrift, Pcm, 48000) &&
                startFormatDrift.StartCalls == 1 && startFormatDrift.StopCalls == 1 &&
                startFormatDrift.WrittenBytes == 0 && !startFormatDrift.IsRunning,
                "direct probe refuses sample-width drift during Start");

            var startNullFormat = new Device();
            startNullFormat.OnStart = () => startNullFormat.Format = default;
            check(!AudioProbeDirectPlayback.TryPlay(startNullFormat, Pcm, 48000) &&
                startNullFormat.StartCalls == 1 && startNullFormat.StopCalls == 1 &&
                startNullFormat.WrittenBytes == 0 && !startNullFormat.IsRunning,
                "direct probe refuses missing PCM format during Start");

            // A format getter can itself trigger a claim or rate change.
            // This deterministic hook exercises the final readiness check
            // immediately before the first PCM write (fourth format read).
            var claimedDuringReady = new Device();
            int claimFormatReads = 0;
            claimedDuringReady.OnFormatRead = () =>
            {
                if (++claimFormatReads == 4)
                    claimedDuringReady.Player = new object();
            };
            check(!AudioProbeDirectPlayback.TryPlay(claimedDuringReady, Pcm, 48000) &&
                claimFormatReads == 4 && claimedDuringReady.StartCalls == 1 &&
                claimedDuringReady.StopCalls == 0 &&
                claimedDuringReady.WrittenBytes == 0,
                "claim during final format getter aborts before first PCM write");

            var rateChangedDuringReady = new Device();
            int rateFormatReads = 0;
            rateChangedDuringReady.OnFormatRead = () =>
            {
                if (++rateFormatReads == 4)
                    rateChangedDuringReady.SampleRate = 44100;
            };
            check(!AudioProbeDirectPlayback.TryPlay(rateChangedDuringReady, Pcm, 48000) &&
                rateFormatReads == 4 && rateChangedDuringReady.StartCalls == 1 &&
                rateChangedDuringReady.StopCalls == 1 &&
                rateChangedDuringReady.WrittenBytes == 0,
                "rate drift during final format getter aborts before first PCM write");

            var startWithdrawn = new Device();
            startWithdrawn.OnStart = () => startWithdrawn.IsWithdrawn = true;
            check(!AudioProbeDirectPlayback.TryPlay(startWithdrawn, Pcm, 48000) &&
                startWithdrawn.StartCalls == 1 && startWithdrawn.StopCalls == 0 &&
                startWithdrawn.WrittenBytes == 0 && startWithdrawn.IsRunning,
                "withdrawal during Start skips unsafe Stop and all PCM writes");
            var afterWithdrawal = new Device();
            check(AudioProbeDirectPlayback.TryPlay(afterWithdrawal, Pcm, 48000) &&
                afterWithdrawal.StartCalls == 1 && afterWithdrawal.StopCalls == 1,
                "direct diagnostic guard released after withdrawal during Start");

            var startClaimed = new Device();
            startClaimed.OnStart = () => startClaimed.Player = new object();
            check(!AudioProbeDirectPlayback.TryPlay(startClaimed, Pcm, 48000) &&
                startClaimed.StartCalls == 1 && startClaimed.StopCalls == 0 &&
                startClaimed.WrittenBytes == 0 && startClaimed.IsRunning,
                "direct probe refuses output claimed during Start without stopping new owner");

            // If the codec changes format after accepting the first PCM
            // fragment, do not write the subsequent silent ring at the wrong
            // sample rate. The device must still be stopped in finally.
            var writeRateDrift = new Device();
            writeRateDrift.OnWrite = () => writeRateDrift.SampleRate = 44100;
            check(!AudioProbeDirectPlayback.TryPlay(writeRateDrift, Pcm, 48000) &&
                writeRateDrift.StartCalls == 1 && writeRateDrift.StopCalls == 1 &&
                writeRateDrift.WrittenBytes == Pcm.Length && !writeRateDrift.IsRunning,
                "direct probe aborts on format drift after first accepted write");

            var writeNullFormat = new Device();
            writeNullFormat.OnWrite = () => writeNullFormat.Format = default;
            check(!AudioProbeDirectPlayback.TryPlay(writeNullFormat, Pcm, 48000) &&
                writeNullFormat.StartCalls == 1 && writeNullFormat.StopCalls == 1 &&
                writeNullFormat.WrittenBytes == Pcm.Length,
                "direct probe stops feeding when PCM format disappears after write");

            var writeClaimed = new Device();
            writeClaimed.OnWrite = () => writeClaimed.Player = new object();
            check(!AudioProbeDirectPlayback.TryPlay(writeClaimed, Pcm, 48000) &&
                writeClaimed.StartCalls == 1 && writeClaimed.StopCalls == 0 &&
                writeClaimed.WrittenBytes == Pcm.Length && writeClaimed.IsRunning,
                "direct probe stops feeding without stopping a newly claimed output");

            var writeWithdrawn = new Device();
            writeWithdrawn.OnWrite = () => writeWithdrawn.IsWithdrawn = true;
            check(!AudioProbeDirectPlayback.TryPlay(writeWithdrawn, Pcm, 48000) &&
                writeWithdrawn.StartCalls == 1 && writeWithdrawn.StopCalls == 0 &&
                writeWithdrawn.WrittenBytes == Pcm.Length && writeWithdrawn.IsRunning,
                "withdrawal after accepted PCM write skips further writes and Stop");

            // Format drift during the 86ms settle is also a failure, even
            // though all bytes were already accepted by the fake ring.
            var settleRateDrift = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "drift during final settle uses bounded wait");
                settleRateDrift.SampleRate = 44100;
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleRateDrift, Pcm, 48000) &&
                settleRateDrift.StartCalls == 1 && settleRateDrift.StopCalls == 1 &&
                settleRateDrift.WrittenBytes == Pcm.Length + 16384,
                "direct probe rejects changed format after final settle");
            var settleNullFormat = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "missing format during settle uses bounded wait");
                settleNullFormat.Format = default;
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleNullFormat, Pcm, 48000) &&
                settleNullFormat.StartCalls == 1 && settleNullFormat.StopCalls == 1 &&
                settleNullFormat.WrittenBytes == Pcm.Length + 16384,
                "direct probe refuses disappearing PCM format during settle");
            // Ownership can also change after the final ring write but
            // before the 86ms settle completes. Never Stop the new owner.
            var settleClaimed = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "claimed output during settle uses bounded wait");
                settleClaimed.Player = new object();
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleClaimed, Pcm, 48000) &&
                settleClaimed.StartCalls == 1 && settleClaimed.StopCalls == 0 &&
                settleClaimed.WrittenBytes == Pcm.Length + 16384 &&
                settleClaimed.IsRunning,
                "direct probe does not stop a player claiming output during settle");
            var settleWithdrawn = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "withdrawn output during settle uses bounded wait");
                settleWithdrawn.IsWithdrawn = true;
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleWithdrawn, Pcm, 48000) &&
                settleWithdrawn.StartCalls == 1 && settleWithdrawn.StopCalls == 0 &&
                settleWithdrawn.WrittenBytes == Pcm.Length + 16384 &&
                settleWithdrawn.IsRunning,
                "withdrawal during settle skips unsafe Stop after silent drain");
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "normal settle remains one HDA ring period");
                waits++;
            };

            // A nested diagnostic can arrive before Start marks the output
            // running. Player == null is not an atomic ownership claim; the
            // direct-probe guard must reject it without touching its device.
            var nested = new Device();
            bool nestedAccepted = true;
            var outer = new Device
            {
                OnStart = () => nestedAccepted =
                    AudioProbeDirectPlayback.TryPlay(nested, Pcm, 48000)
            };
            check(AudioProbeDirectPlayback.TryPlay(outer, Pcm, 48000) &&
                !nestedAccepted && nested.StartCalls == 0 &&
                nested.StopCalls == 0 && outer.StopCalls == 1,
                "nested direct HDA probe refused before touching second output");
            check(AudioProbeDirectPlayback.TryPlay(nested, Pcm, 48000) &&
                nested.StartCalls == 1 && nested.StopCalls == 1,
                "direct probe guard released after successful outer playback");

            var brokenStop = new Device { ThrowOnStop = true };
            bool stopThrew = false;
            try { AudioProbeDirectPlayback.TryPlay(brokenStop, Pcm, 48000); }
            catch (InvalidOperationException) { stopThrew = true; }
            check(stopThrew && brokenStop.StopCalls == 1,
                "simulated Stop exception propagates");
            var afterStopFailure = new Device();
            check(AudioProbeDirectPlayback.TryPlay(afterStopFailure, Pcm, 48000) &&
                afterStopFailure.StartCalls == 1 &&
                afterStopFailure.StopCalls == 1,
                "direct probe guard released even when Stop throws");

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

            // The actual direct playback path must observe withdrawal
            // during a zero-byte driver write before sleeping or retrying.
            waits = 0;
            var withdrawnOnZeroWrite = new Device { StallWrites = true };
            withdrawnOnZeroWrite.OnStalledWrite =
                () => withdrawnOnZeroWrite.IsWithdrawn = true;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = _ => waits++;
            check(!AudioProbeDirectPlayback.TryPlay(withdrawnOnZeroWrite, Pcm, 48000) &&
                withdrawnOnZeroWrite.StartCalls == 1 &&
                withdrawnOnZeroWrite.StopCalls == 0 &&
                withdrawnOnZeroWrite.IsRunning && waits == 0,
                "direct HDA probe stops without waiting after zero-write withdrawal");

            waits = 0;
            var withdrawn = new Device { StallWrites = true };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 1, "withdrawal wait remains bounded");
                waits++;
                withdrawn.IsWithdrawn = true;
            };
            check(!AudioProbeDirectPlayback.TryPlay(withdrawn, Pcm, 48000) &&
                withdrawn.StopCalls == 0 && withdrawn.IsRunning && waits == 1,
                "device withdrawal during ring-full wait fails closed");
        }
        finally
        {
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = null;
        }
    }
}
