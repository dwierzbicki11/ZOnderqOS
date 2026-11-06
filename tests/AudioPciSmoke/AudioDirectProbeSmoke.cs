using System;
using System.IO;
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
        public bool ThrowOnFormatRead;
        public HostAudioFormat Format
        {
            get
            {
                HostAudioFormat snapshot = _format;
                OnFormatRead?.Invoke();
                if (ThrowOnFormatRead) throw new InvalidOperationException("simulated codec reset during Format");
                return snapshot;
            }
            set => _format = value;
        }
        public int StartCalls;
        public int StopCalls;
        public int WrittenBytes;
        private int _writableBytes = 16384;
        public int WritableReadCalls;
        public Action OnWritableRead;
        public bool ThrowOnWritableRead;
        public int WritableBytes
        {
            get
            {
                // Detect stale hardware reads after a simulated unplug.
                if (IsWithdrawn)
                    throw new InvalidOperationException("read of withdrawn HDA ring");
                WritableReadCalls++;
                OnWritableRead?.Invoke();
                if (ThrowOnWritableRead)
                    throw new InvalidOperationException("simulated HDA ring reset during telemetry");
                return _writableBytes;
            }
            set => _writableBytes = value;
        }
        public bool ThrowOnStart;
        public bool ThrowOnWrite;
        public bool ThrowOnStop;
        public bool StallWrites;
        public int? ForcedWriteResult;
        // Host-only partial-write simulation; stock device behavior is unchanged.
        public int MaxWriteBytes;
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
            if (ForcedWriteResult.HasValue) return ForcedWriteResult.Value;
            if (StallWrites)
            {
                OnStalledWrite?.Invoke();
                return 0;
            }
            int accepted = MaxWriteBytes > 0 ? Math.Min(MaxWriteBytes, bytes.Length) : bytes.Length;
            accepted -= accepted % 4;
            WrittenBytes += accepted;
            OnWrite?.Invoke();
            return accepted;
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

        // Format can fail when a codec resets during a public getter.
        // This must fail closed without starting or stopping the device.
        var failedFormatRead = new Device { ThrowOnFormatRead = true };
        check(!AudioProbeDirectPlayback.TryPlay(failedFormatRead, Pcm, 48000) &&
            failedFormatRead.StartCalls == 0 && failedFormatRead.StopCalls == 0 &&
            failedFormatRead.WrittenBytes == 0,
            "HDA format getter failure refuses playback without touching controller");

        var withdrawnOnFormatRead = new Device();
        withdrawnOnFormatRead.OnFormatRead = () => withdrawnOnFormatRead.IsWithdrawn = true;
        check(!AudioProbeDirectPlayback.TryPlay(withdrawnOnFormatRead, Pcm, 48000) &&
            withdrawnOnFormatRead.StartCalls == 0 &&
            withdrawnOnFormatRead.StopCalls == 0 &&
            withdrawnOnFormatRead.WrittenBytes == 0,
            "withdrawal during format getter aborts before Start");

        // A rejected Start must not feed PCM or Stop an idle controller.
        // A rejected Stop during cleanup must not crash the diagnostic shell.
        var rejectedStart = new Device { ThrowOnStart = true };
        check(!AudioProbeDirectPlayback.TryPlay(rejectedStart, Pcm, 48000) &&
            rejectedStart.StartCalls == 1 && rejectedStart.StopCalls == 0 &&
            rejectedStart.WrittenBytes == 0 && !rejectedStart.IsRunning,
            "direct probe handles rejected Start without touching idle stream");

        var rejectedStop = new Device { ThrowOnStop = true };
        check(AudioProbeDirectPlayback.TryPlay(rejectedStop, Pcm, 48000) &&
            rejectedStop.StartCalls == 1 && rejectedStop.StopCalls == 1 &&
            rejectedStop.WrittenBytes == Pcm.Length + 16384 &&
            !rejectedStop.IsRunning,
            "direct probe handles rejected Stop after feeding without throwing");

        // An external actor may stop the stream during a successful Write.
        // The diagnostic must not issue a second Stop on a stream it no
        // longer knows to be running.
        var stoppedDuringWrite = new Device();
        stoppedDuringWrite.OnWrite = () => stoppedDuringWrite.IsRunning = false;
        check(!AudioProbeDirectPlayback.TryPlay(stoppedDuringWrite, Pcm, 48000) &&
            stoppedDuringWrite.StartCalls == 1 && stoppedDuringWrite.StopCalls == 0 &&
            stoppedDuringWrite.WrittenBytes == Pcm.Length,
            "direct probe skips Stop when stream stopped during Write");

        // A faulty driver must not crash the diagnostic shell or leak a
        // running output when it reports an invalid frame count.
        foreach (int invalidCount in new[] { -1, 3, 4097 })
        {
            var invalidWriter = new Device { ForcedWriteResult = invalidCount };
            check(!AudioProbeDirectPlayback.TryPlay(invalidWriter, Pcm, 48000) &&
                invalidWriter.StartCalls == 1 && invalidWriter.StopCalls == 1 &&
                !invalidWriter.IsRunning,
                "direct probe safely rejects invalid driver Write count " + invalidCount);
        }

        int waits = 0;
        Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
        {
            check(ms == 86, "successful direct probe waits one HDA ring period");
            waits++;
        };
        try
        {
            var good = new Device();
            TextWriter priorOutput = Console.Out;
            using var normalTrace = new StringWriter();
            bool goodResult;
            try
            {
                Console.SetOut(normalTrace);
                goodResult = AudioProbeDirectPlayback.TryPlay(good, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
            }
            check(goodResult, "direct probe feeds frame-aligned PCM through the real feeder");
            check(good.StartCalls == 1 && good.StopCalls == 1 &&
                !good.IsRunning && good.WrittenBytes == Pcm.Length + 16384 &&
                waits == 1, "direct probe stops after one silent ring and settle");
            string normalProgress = normalTrace.ToString();
            check(normalProgress.Contains("ACCEPTED_TO_RING") &&
                normalProgress.Contains("pcmQueued=16") &&
                normalProgress.Contains("silenceQueued=16384") &&
                normalProgress.Contains("writeCalls=5") &&
                normalProgress.Contains("zeroWrites=0") &&
                normalProgress.Contains("partialWrites=0") &&
                normalProgress.Contains("longestZeroStreak=0") &&
                normalProgress.Contains("minPositiveWrite=16") &&
                normalProgress.Contains("waits=0") &&
                normalProgress.Contains("settleRequests=1") &&
                normalProgress.Contains("ringSamples=7 ringChanges=0") &&
                normalProgress.Contains("ringWriteSamples=5 ringWaitSamples=1") &&
                normalProgress.Contains("ringMin=16384 ringMax=16384") &&
                normalProgress.Contains("ringIncreases=0 ringDecreases=0") &&
                normalProgress.Contains("ringWaitIncreases=0 ringWaitDecreases=0") &&
                normalProgress.Contains("DMA/IRQ and audible PCM not proven"),
                "direct probe reports accepted bytes without claiming hardware DMA drain");

            // Only driver-reported WritableBytes is sampled: this verifies
            // changes across the settle wait, not actual DMA or IRQ delivery.
            var movingCapacity = new Device { WritableBytes = 0 };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "capacity movement occurs during settle");
                movingCapacity.WritableBytes = 4096;
            };
            using var capacityTrace = new StringWriter();
            bool capacityResult;
            try
            {
                Console.SetOut(capacityTrace);
                capacityResult = AudioProbeDirectPlayback.TryPlay(movingCapacity, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
                Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
                {
                    check(ms == 86, "successful direct probe waits one HDA ring period");
                    waits++;
                };
            }
            check(capacityResult && movingCapacity.StopCalls == 1 &&
                capacityTrace.ToString().Contains("ringSamples=7 ringChanges=1") &&
                capacityTrace.ToString().Contains("ringWriteSamples=5 ringWaitSamples=1") &&
                capacityTrace.ToString().Contains("ringMin=0 ringMax=4096") &&
                capacityTrace.ToString().Contains("ringIncreases=1 ringDecreases=0") &&
                capacityTrace.ToString().Contains("ringWaitIncreases=1 ringWaitDecreases=0") &&
                capacityTrace.ToString().Contains("writable=writer+wait snapshots; DMA/IRQ not proven") &&
                movingCapacity.WritableReadCalls == 7,
                "direct probe reports stock driver capacity movement without claiming DMA proof");

            // A full-ring wait can reveal both newly freed capacity and
            // subsequent consumption. Changes remain software snapshots,
            // not evidence that a guest IRQ handler actually ran.
            var changingCapacity = new Device { StallWrites = true, WritableBytes = 0 };
            changingCapacity.OnStalledWrite = () => changingCapacity.StallWrites = false;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                if (ms == 1) changingCapacity.WritableBytes = 4096;
                else if (ms == 86) changingCapacity.WritableBytes = 1024;
                else throw new Exception("Unexpected HDA wait duration");
            };
            using var changingTrace = new StringWriter();
            bool changingResult;
            try
            {
                Console.SetOut(changingTrace);
                changingResult = AudioProbeDirectPlayback.TryPlay(changingCapacity, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
                Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
                {
                    check(ms == 86, "successful direct probe waits one HDA ring period");
                    waits++;
                };
            }
            check(changingResult && changingCapacity.WritableReadCalls == 8 &&
                changingTrace.ToString().Contains("ringSamples=8 ringChanges=2") &&
                changingTrace.ToString().Contains("ringWriteSamples=5 ringWaitSamples=2") &&
                changingTrace.ToString().Contains("ringMin=0 ringMax=4096") &&
                changingTrace.ToString().Contains("ringIncreases=1 ringDecreases=1") &&
                changingTrace.ToString().Contains("ringWaitIncreases=1 ringWaitDecreases=1"),
                "direct probe distinguishes driver-reported free-space increase and decrease");

            // Optional telemetry must tolerate a transient invalidated HDA ring
            // without treating a failed WritableBytes read as DMA progress.
            var invalidatedTelemetry = new Device { ThrowOnWritableRead = true };
            check(AudioProbeDirectPlayback.TryPlay(invalidatedTelemetry, Pcm, 48000) &&
                invalidatedTelemetry.WritableReadCalls > 0 &&
                invalidatedTelemetry.StopCalls == 1 &&
                invalidatedTelemetry.WrittenBytes == Pcm.Length + 16384,
                "invalidated HDA telemetry does not interrupt valid PCM writes");

            // The getter itself may withdraw/rebind a controller. A stale
            // snapshot must be discarded before ring statistics are updated,
            // and the feeder must not write or Stop somebody else's output.
            var withdrawnInWritableGetter = new Device();
            withdrawnInWritableGetter.OnWritableRead =
                () => withdrawnInWritableGetter.IsWithdrawn = true;
            check(!AudioProbeDirectPlayback.TryPlay(withdrawnInWritableGetter, Pcm, 48000) &&
                withdrawnInWritableGetter.WritableReadCalls == 1 &&
                withdrawnInWritableGetter.WrittenBytes == 0 &&
                withdrawnInWritableGetter.StopCalls == 0,
                "withdrawal inside WritableBytes getter aborts without stale ring reads");

            bool selectedDuringWritableGetter = true;
            var replacedInWritableGetter = new Device();
            replacedInWritableGetter.OnWritableRead = () => selectedDuringWritableGetter = false;
            check(!AudioProbeDirectPlayback.TryPlay(replacedInWritableGetter, Pcm, 48000,
                () => selectedDuringWritableGetter) &&
                replacedInWritableGetter.WritableReadCalls == 1 &&
                replacedInWritableGetter.WrittenBytes == 0 &&
                replacedInWritableGetter.StopCalls == 0,
                "primary replacement inside WritableBytes getter skips unsafe Stop");

            // A host-only device returning partial frame-aligned writes must
            // be counted by accepted bytes, not by requested block sizes.
            var partial = new Device { MaxWriteBytes = 1024 };
            using var partialTrace = new StringWriter();
            bool partialResult;
            try
            {
                Console.SetOut(partialTrace);
                partialResult = AudioProbeDirectPlayback.TryPlay(partial, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
            }
            check(partialResult && partial.WrittenBytes == Pcm.Length + 16384 &&
                partialTrace.ToString().Contains("pcmQueued=16") &&
                partialTrace.ToString().Contains("silenceQueued=16384") &&
                partialTrace.ToString().Contains("writeCalls=17") &&
                partialTrace.ToString().Contains("zeroWrites=0") &&
                partialTrace.ToString().Contains("partialWrites=12") &&
                partialTrace.ToString().Contains("longestZeroStreak=0") &&
                partialTrace.ToString().Contains("minPositiveWrite=16") &&
                partialTrace.ToString().Contains("settleRequests=1"),
                "partial HDA writes count accepted bytes and actual calls");

            // A single zero-byte return is backpressure, not an accepted
            // PCM fragment. The 1 ms wait must appear in telemetry.
            var stallOnce = new Device { StallWrites = true };
            stallOnce.OnStalledWrite = () => stallOnce.StallWrites = false;
            int oneMsWaits = 0;
            int settleWaitsAfterStall = 0;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                if (ms == 1) oneMsWaits++;
                else if (ms == 86) settleWaitsAfterStall++;
                else throw new Exception("Unexpected audio wait duration");
            };
            using var stalledTrace = new StringWriter();
            bool stalledResult;
            try
            {
                Console.SetOut(stalledTrace);
                stalledResult = AudioProbeDirectPlayback.TryPlay(stallOnce, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
                Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
                {
                    check(ms == 86, "successful direct probe waits one HDA ring period");
                    waits++;
                };
            }
            check(stalledResult && oneMsWaits == 1 && settleWaitsAfterStall == 1 &&
                stallOnce.WrittenBytes == Pcm.Length + 16384 &&
                stalledTrace.ToString().Contains("pcmQueued=16") &&
                stalledTrace.ToString().Contains("silenceQueued=16384") &&
                stalledTrace.ToString().Contains("writeCalls=6") &&
                stalledTrace.ToString().Contains("zeroWrites=1") &&
                stalledTrace.ToString().Contains("partialWrites=0") &&
                stalledTrace.ToString().Contains("longestZeroStreak=1") &&
                stalledTrace.ToString().Contains("waits=1") &&
                stalledTrace.ToString().Contains("settleRequests=1"),
                "one stalled HDA write reports backpressure without inflating queued PCM");

            // Two consecutive full-ring writes must be reported as one
            // backpressure streak, not two independent DMA completions.
            var twoStalls = new Device { StallWrites = true };
            int stalledCalls = 0;
            twoStalls.OnStalledWrite = () =>
            {
                stalledCalls++;
                if (stalledCalls == 2)
                    twoStalls.StallWrites = false;
            };
            int twoShortWaits = 0;
            int twoSettleWaits = 0;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                if (ms == 1) twoShortWaits++;
                else if (ms == 86) twoSettleWaits++;
                else throw new Exception("Unexpected audio wait duration");
            };
            using var twoStallTrace = new StringWriter();
            bool twoStallResult;
            try
            {
                Console.SetOut(twoStallTrace);
                twoStallResult = AudioProbeDirectPlayback.TryPlay(twoStalls, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorOutput);
                Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
                {
                    check(ms == 86, "successful direct probe waits one HDA ring period");
                    waits++;
                };
            }
            check(twoStallResult && twoShortWaits == 2 && twoSettleWaits == 1 &&
                twoStalls.WrittenBytes == Pcm.Length + 16384 &&
                twoStallTrace.ToString().Contains("writeCalls=7") &&
                twoStallTrace.ToString().Contains("zeroWrites=2") &&
                twoStallTrace.ToString().Contains("partialWrites=0") &&
                twoStallTrace.ToString().Contains("longestZeroStreak=2") &&
                twoStallTrace.ToString().Contains("minPositiveWrite=16") &&
                twoStallTrace.ToString().Contains("waits=2"),
                "consecutive full-ring writes report real backpressure streak");

            // The public HDA name/driver can stay identical even after
            // AudioManager.Primary changes to another device instance.
            // The caller-provided reference check must refuse stale output
            // before Start, after Start, after a write and during settle.
            var staleBeforeStart = new Device();
            check(!AudioProbeDirectPlayback.TryPlay(staleBeforeStart, Pcm, 48000,
                () => false) && staleBeforeStart.StartCalls == 0 &&
                staleBeforeStart.StopCalls == 0 && staleBeforeStart.WrittenBytes == 0,
                "replaced primary refuses direct HDA before Start");

            bool selectedDuringFormat = true;
            var replacedDuringFormatRead = new Device();
            replacedDuringFormatRead.OnFormatRead = () => selectedDuringFormat = false;
            check(!AudioProbeDirectPlayback.TryPlay(replacedDuringFormatRead, Pcm, 48000,
                () => selectedDuringFormat) &&
                replacedDuringFormatRead.StartCalls == 0 &&
                replacedDuringFormatRead.StopCalls == 0 &&
                replacedDuringFormatRead.WrittenBytes == 0,
                "primary replacement during format snapshot aborts before Start");

            bool selectedPrimary = true;
            var replacedDuringStart = new Device();
            replacedDuringStart.OnStart = () => selectedPrimary = false;
            check(!AudioProbeDirectPlayback.TryPlay(replacedDuringStart, Pcm, 48000,
                () => selectedPrimary) && replacedDuringStart.StartCalls == 1 &&
                replacedDuringStart.StopCalls == 0 && replacedDuringStart.WrittenBytes == 0,
                "primary replacement during Start skips stale writes and Stop");

            selectedPrimary = true;
            var replacedAfterWrite = new Device();
            replacedAfterWrite.OnWrite = () => selectedPrimary = false;
            check(!AudioProbeDirectPlayback.TryPlay(replacedAfterWrite, Pcm, 48000,
                () => selectedPrimary) && replacedAfterWrite.StartCalls == 1 &&
                replacedAfterWrite.StopCalls == 0 &&
                replacedAfterWrite.WrittenBytes == Pcm.Length,
                "primary replacement after PCM write skips silent drain and Stop");

            selectedPrimary = true;
            var replacedDuringSettle = new Device();
            int settleWaits = 0;
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "primary replacement occurs during final settle");
                settleWaits++;
                selectedPrimary = false;
            };
            check(!AudioProbeDirectPlayback.TryPlay(replacedDuringSettle, Pcm, 48000,
                () => selectedPrimary) && replacedDuringSettle.StartCalls == 1 &&
                replacedDuringSettle.StopCalls == 0 &&
                replacedDuringSettle.WrittenBytes == Pcm.Length + 16384 &&
                replacedDuringSettle.WritableReadCalls == 6 && settleWaits == 1,
                "primary replacement during settle skips Stop on stale device");
            // Restore the normal successful 86ms wait callback for the
            // remaining ownership and format regression cases.
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "successful direct probe waits one HDA ring period");
                waits++;
            };
            var guardAfterPrimaryReplacement = new Device();
            check(AudioProbeDirectPlayback.TryPlay(guardAfterPrimaryReplacement, Pcm, 48000) &&
                guardAfterPrimaryReplacement.StopCalls == 1,
                "primary replacement releases direct diagnostic guard");

            // Start may change the published sample rate or PCM format.
            // No bytes may reach a codec whose format changed during Start.
            var startRateDrift = new Device();
            startRateDrift.OnStart = () => startRateDrift.SampleRate = 44100;
            check(!AudioProbeDirectPlayback.TryPlay(startRateDrift, Pcm, 48000) &&
                startRateDrift.StartCalls == 1 && startRateDrift.StopCalls == 0 &&
                startRateDrift.WrittenBytes == 0 && startRateDrift.IsRunning,
                "direct probe refuses sample-rate drift during Start before PCM write");

            // A reconfigured output may now belong to a different playback
            // configuration. Cleanup must not Stop its new sample rate.
            check(startRateDrift.SampleRate == 44100,
                "reconfigured sample rate is preserved after probe abort");

            var startFormatDrift = new Device();
            startFormatDrift.OnStart = () => startFormatDrift.Format =
                new Cosmos.Kernel.System.Audio.HostAudioFormat(2, 1);
            check(!AudioProbeDirectPlayback.TryPlay(startFormatDrift, Pcm, 48000) &&
                startFormatDrift.StartCalls == 1 && startFormatDrift.StopCalls == 0 &&
                startFormatDrift.WrittenBytes == 0 && startFormatDrift.IsRunning,
                "direct probe refuses sample-width drift during Start");

            var startNullFormat = new Device();
            startNullFormat.OnStart = () => startNullFormat.Format = default;
            check(!AudioProbeDirectPlayback.TryPlay(startNullFormat, Pcm, 48000) &&
                startNullFormat.StartCalls == 1 && startNullFormat.StopCalls == 0 &&
                startNullFormat.WrittenBytes == 0 && startNullFormat.IsRunning,
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

            // A controller identity can change while the public format
            // getter executes. The last readiness check must not accept
            // the previous HDA identity and write to a replaced device.
            var nameChangedDuringReady = new Device();
            int nameFormatReads = 0;
            nameChangedDuringReady.OnFormatRead = () =>
            {
                if (++nameFormatReads == 4)
                    nameChangedDuringReady.Name = "replacement";
            };
            check(!AudioProbeDirectPlayback.TryPlay(nameChangedDuringReady, Pcm, 48000) &&
                nameFormatReads == 4 && nameChangedDuringReady.StartCalls == 1 &&
                nameChangedDuringReady.StopCalls == 0 &&
                nameChangedDuringReady.WrittenBytes == 0 &&
                nameChangedDuringReady.IsRunning,
                "identity drift in final format getter aborts before PCM write");

            var driverChangedDuringReady = new Device();
            int driverFormatReads = 0;
            driverChangedDuringReady.OnFormatRead = () =>
            {
                if (++driverFormatReads == 4)
                    driverChangedDuringReady.DriverName = "ReplacementDriver";
            };
            check(!AudioProbeDirectPlayback.TryPlay(driverChangedDuringReady, Pcm, 48000) &&
                driverFormatReads == 4 && driverChangedDuringReady.StartCalls == 1 &&
                driverChangedDuringReady.StopCalls == 0 &&
                driverChangedDuringReady.WrittenBytes == 0 &&
                driverChangedDuringReady.IsRunning,
                "driver drift in final format getter aborts before PCM write");

            var rateChangedDuringReady = new Device();
            int rateFormatReads = 0;
            rateChangedDuringReady.OnFormatRead = () =>
            {
                if (++rateFormatReads == 4)
                    rateChangedDuringReady.SampleRate = 44100;
            };
            check(!AudioProbeDirectPlayback.TryPlay(rateChangedDuringReady, Pcm, 48000) &&
                rateFormatReads == 4 && rateChangedDuringReady.StartCalls == 1 &&
                rateChangedDuringReady.StopCalls == 0 &&
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
            // sample rate. Cleanup must NOT stop a reconfigured stream.
            var writeRateDrift = new Device();
            writeRateDrift.OnWrite = () => writeRateDrift.SampleRate = 44100;
            check(!AudioProbeDirectPlayback.TryPlay(writeRateDrift, Pcm, 48000) &&
                writeRateDrift.StartCalls == 1 && writeRateDrift.StopCalls == 0 &&
                writeRateDrift.WrittenBytes == Pcm.Length && writeRateDrift.IsRunning &&
                writeRateDrift.SampleRate == 44100,
                "direct probe aborts on format drift after first accepted write");

            var writeIdentityDrift = new Device();
            writeIdentityDrift.OnWrite = () => writeIdentityDrift.Name = "replacement";
            check(!AudioProbeDirectPlayback.TryPlay(writeIdentityDrift, Pcm, 48000) &&
                writeIdentityDrift.StartCalls == 1 &&
                writeIdentityDrift.StopCalls == 0 &&
                writeIdentityDrift.WrittenBytes == Pcm.Length &&
                writeIdentityDrift.IsRunning,
                "identity drift after PCM write aborts without stopping replacement");

            var writeNullFormat = new Device();
            writeNullFormat.OnWrite = () => writeNullFormat.Format = default;
            check(!AudioProbeDirectPlayback.TryPlay(writeNullFormat, Pcm, 48000) &&
                writeNullFormat.StartCalls == 1 && writeNullFormat.StopCalls == 0 &&
                writeNullFormat.WrittenBytes == Pcm.Length && writeNullFormat.IsRunning,
                "direct probe stops feeding when PCM format disappears after write");

            var writeClaimed = new Device();
            writeClaimed.OnWrite = () => writeClaimed.Player = new object();
            check(!AudioProbeDirectPlayback.TryPlay(writeClaimed, Pcm, 48000) &&
                writeClaimed.StartCalls == 1 && writeClaimed.StopCalls == 0 &&
                writeClaimed.WrittenBytes == Pcm.Length && writeClaimed.IsRunning,
                "direct probe stops feeding without stopping a newly claimed output");

            var writeWithdrawn = new Device();
            writeWithdrawn.OnWrite = () => writeWithdrawn.IsWithdrawn = true;
            using var abortedTrace = new StringWriter();
            bool abortedResult;
            TextWriter priorAbortedOutput = Console.Out;
            try
            {
                Console.SetOut(abortedTrace);
                abortedResult = AudioProbeDirectPlayback.TryPlay(writeWithdrawn, Pcm, 48000);
            }
            finally
            {
                Console.SetOut(priorAbortedOutput);
            }
            check(!abortedResult &&
                writeWithdrawn.StartCalls == 1 && writeWithdrawn.StopCalls == 0 &&
                writeWithdrawn.WrittenBytes == Pcm.Length && writeWithdrawn.IsRunning,
                "withdrawal after accepted PCM write skips further writes and Stop");
            check(abortedTrace.ToString().Contains("FAILED") &&
                abortedTrace.ToString().Contains("pcmQueued=16") &&
                abortedTrace.ToString().Contains("silenceQueued=0") &&
                abortedTrace.ToString().Contains("writeCalls=1") &&
                abortedTrace.ToString().Contains("settleRequests=0"),
                "withdrawal telemetry never reports an unrequested settle or silent tail");

            // Format drift during the 86ms settle is also a failure, even
            // though all bytes were already accepted by the fake ring.
            var settleRateDrift = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "drift during final settle uses bounded wait");
                settleRateDrift.SampleRate = 44100;
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleRateDrift, Pcm, 48000) &&
                settleRateDrift.StartCalls == 1 && settleRateDrift.StopCalls == 0 &&
                settleRateDrift.IsRunning && settleRateDrift.SampleRate == 44100 &&
                settleRateDrift.WrittenBytes == Pcm.Length + 16384 &&
                settleRateDrift.WritableReadCalls == 6,
                "direct probe rejects changed format after final settle");
            var settleDriverDrift = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "driver drift during settle uses bounded wait");
                settleDriverDrift.DriverName = "ReplacementDriver";
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleDriverDrift, Pcm, 48000) &&
                settleDriverDrift.StartCalls == 1 &&
                settleDriverDrift.StopCalls == 0 &&
                settleDriverDrift.WrittenBytes == Pcm.Length + 16384 &&
                settleDriverDrift.IsRunning,
                "driver drift during settle skips Stop on replaced controller");

            var settleNullFormat = new Device();
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "missing format during settle uses bounded wait");
                settleNullFormat.Format = default;
            };
            check(!AudioProbeDirectPlayback.TryPlay(settleNullFormat, Pcm, 48000) &&
                settleNullFormat.StartCalls == 1 && settleNullFormat.StopCalls == 0 &&
                settleNullFormat.IsRunning &&
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
                settleClaimed.WritableReadCalls == 6 && settleClaimed.IsRunning,
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
                settleWithdrawn.WritableReadCalls == 6 && settleWithdrawn.IsRunning,
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

            // Cleanup reads Format only once and must recheck ownership if
            // the getter itself rebinds the device after the final settle.
            var claimedDuringCleanupFormat = new Device();
            bool cleanupPhase = false;
            int cleanupFormatReads = 0;
            claimedDuringCleanupFormat.OnFormatRead = () =>
            {
                if (cleanupPhase)
                {
                    // The feeder performs one final readiness read after
                    // the settle callback. Claim only on the subsequent
                    // cleanup getter so the test exercises guarded Stop().
                    if (++cleanupFormatReads == 2)
                        claimedDuringCleanupFormat.Player = new object();
                }
            };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "cleanup claim test settles once");
                cleanupPhase = true;
            };
            check(AudioProbeDirectPlayback.TryPlay(claimedDuringCleanupFormat, Pcm, 48000) &&
                cleanupFormatReads == 2 && claimedDuringCleanupFormat.StopCalls == 0 &&
                claimedDuringCleanupFormat.IsRunning,
                "cleanup format getter claim skips Stop and reads format once");
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "successful direct probe waits one HDA ring period");
                waits++;
            };

            // The final feeder readiness check reads Format once after settle.
            // A reset on the following cleanup-only getter must not throw
            // or Stop an output whose configuration can no longer be verified.
            var resetDuringCleanupFormat = new Device();
            int resetCleanupReads = 0;
            bool resetCleanupPhase = false;
            resetDuringCleanupFormat.OnFormatRead = () =>
            {
                if (resetCleanupPhase && ++resetCleanupReads == 2)
                    resetDuringCleanupFormat.ThrowOnFormatRead = true;
            };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "cleanup format reset test settles once");
                resetCleanupPhase = true;
            };
            check(AudioProbeDirectPlayback.TryPlay(resetDuringCleanupFormat, Pcm, 48000) &&
                resetCleanupReads == 2 && resetDuringCleanupFormat.StopCalls == 0 &&
                resetDuringCleanupFormat.IsRunning,
                "cleanup Format exception skips unsafe Stop without crashing");
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "normal settle restored after cleanup reset");
                waits++;
            };

            // A Format getter can also reconfigure the sample rate during
            // cleanup. Never Stop the stream after this identity drift.
            var rateChangedDuringCleanup = new Device();
            int cleanupRateReads = 0;
            bool cleanupRatePhase = false;
            rateChangedDuringCleanup.OnFormatRead = () =>
            {
                if (cleanupRatePhase && ++cleanupRateReads == 2)
                    rateChangedDuringCleanup.SampleRate = 44100;
            };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "cleanup rate drift test settles once");
                cleanupRatePhase = true;
            };
            check(AudioProbeDirectPlayback.TryPlay(rateChangedDuringCleanup, Pcm, 48000) &&
                cleanupRateReads == 2 && rateChangedDuringCleanup.StopCalls == 0 &&
                rateChangedDuringCleanup.IsRunning,
                "cleanup Format getter sample-rate drift skips unsafe Stop");
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 86, "normal settle restored after cleanup rate drift");
                waits++;
            };

            var brokenStop = new Device { ThrowOnStop = true };
            bool stopThrew = false;
            try { AudioProbeDirectPlayback.TryPlay(brokenStop, Pcm, 48000); }
            catch (InvalidOperationException) { stopThrew = true; }
            check(stopThrew && brokenStop.StopCalls == 1,
                "simulated Stop exception propagates");
            var afterIdentityDrift = new Device();
            check(AudioProbeDirectPlayback.TryPlay(afterIdentityDrift, Pcm, 48000) &&
                afterIdentityDrift.StartCalls == 1 && afterIdentityDrift.StopCalls == 1,
                "direct guard remains reusable after replaced-output cleanup");

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

            // The diagnostic catches InvalidOperationException from Write as
            // a failed PCM operation, rather than crashing the shell. Cleanup
            // must still Stop the unclaimed, unchanged HDA stream.
            var brokenWrite = new Device { ThrowOnWrite = true };
            bool writeResult = AudioProbeDirectPlayback.TryPlay(brokenWrite, Pcm, 48000);
            check(!writeResult && brokenWrite.StartCalls == 1 &&
                brokenWrite.StopCalls == 1 && !brokenWrite.IsRunning,
                "failed HDA Write returns failure and invokes Stop in finally");

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

            // The feeder checks ready() before calling the writer, but a
            // reconfiguration can occur in the gap. The writer must perform
            // its own format/ownership check before the first PCM byte.
            var claimedAtWriteBoundary = new Device();
            int boundaryFormatReads = 0;
            claimedAtWriteBoundary.OnFormatRead = () =>
            {
                if (++boundaryFormatReads == 5)
                    claimedAtWriteBoundary.Player = new object();
            };
            check(!AudioProbeDirectPlayback.TryPlay(claimedAtWriteBoundary, Pcm, 48000) &&
                claimedAtWriteBoundary.StartCalls == 1 &&
                claimedAtWriteBoundary.WrittenBytes == 0 &&
                claimedAtWriteBoundary.StopCalls == 0 &&
                boundaryFormatReads == 5,
                "direct HDA writer rechecks ownership after feeder readiness");

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
                withdrawnOnZeroWrite.IsRunning && waits == 0 &&
                withdrawnOnZeroWrite.WritableReadCalls == 1,
                "direct HDA probe avoids stale capacity read after zero-write withdrawal");

            waits = 0;
            var withdrawn = new Device { StallWrites = true };
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = ms =>
            {
                check(ms == 1, "withdrawal wait remains bounded");
                waits++;
                withdrawn.IsWithdrawn = true;
            };
            check(!AudioProbeDirectPlayback.TryPlay(withdrawn, Pcm, 48000) &&
                withdrawn.StopCalls == 0 && withdrawn.IsRunning && waits == 1 &&
                withdrawn.WritableReadCalls == 1,
                "device withdrawal during ring-full wait skips stale capacity reads");
        }
        finally
        {
            Cosmos.Kernel.System.Timers.TimerManager.OnWait = null;
        }
    }
}
