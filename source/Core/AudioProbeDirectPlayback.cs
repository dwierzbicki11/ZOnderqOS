#if ZONDERQ_AUDIO_PROBE
using System;
using Cosmos.Kernel.HAL.Devices.Audio;
using Cosmos.Kernel.System.Audio;

namespace ZonderqOS.SystemCore
{
    /// <summary>
    /// Experimental 1ms-wait A/B diagnostic for an isolated single-caller
    /// console only. AudioDevice does not publicly expose AudioPlayer's claim
    /// protocol: this must NEVER be used by production GUI or other threads.
    /// Uses stock 3.0.95 public output API, not custom DMA or IRQ handling.
    /// </summary>
    internal static class AudioProbeDirectPlayback
    {
        // The stock AudioDevice.Player snapshot is not an atomic claim.
        // Prevent two opt-in direct diagnostics from feeding the same ring.
        // This does not replace the stock AudioPlayback ownership protocol.
        private static int _directProbeBusy;

        // A published output may be withdrawn or have its format changed
        // between the initial probe and the next DMA-ring write. Never feed
        // stereo16 into a stream that no longer advertises stereo16/48 kHz.
        // This is a snapshot check, not a replacement for an atomic claim.
        private static bool IsCompatibleUnclaimedHda(AudioDevice output, uint sampleRate)
        {
            if (output == null || output.IsWithdrawn ||
                output.Name != "hda" || output.DriverName != "HdAudioDriver" ||
                output.Player != null || output.SampleRate != sampleRate)
                return false;

            // Stock Cosmos 3.0.95 AudioFormat is a value type, not nullable.
            // A default/uninitialized format has zero channels/width and is
            // rejected by the explicit stereo16 check. Snapshot once to
            // avoid mixing fields from different published formats.
            // This still is NOT an atomic output ownership claim.
            var format = output.Format;
            // Format is a public getter, not an atomic ownership snapshot.
            // A stock player may claim/withdraw/reconfigure the output while
            // that getter executes. Recheck after reading it so this probe
            // does not queue even one stale-format block in that case.
            return format.Channels == 2 && format.ChannelSize == 2 &&
                !output.IsWithdrawn && output.Player == null &&
                output.SampleRate == sampleRate;
        }

        internal static bool TryPlay(AudioDevice output, byte[] pcm, uint sampleRate)
        {
            if (sampleRate != 48000 || pcm == null || pcm.Length == 0 ||
                pcm.Length > AudioProbeRingFeeder.MaxPcmBytes || pcm.Length % 4 != 0 ||
                !IsCompatibleUnclaimedHda(output, sampleRate) || output.IsRunning)
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: requires idle stock HDA and bounded stereo16.");
                return false;
            }

            if (System.Threading.Interlocked.CompareExchange(
                ref _directProbeBusy, 1, 0) != 0)
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: another direct diagnostic is active.");
                return false;
            }

            bool started = false;
            try
            {
                // Recheck after acquiring our local diagnostic guard. A
                // withdrawn, running or newly claimed device is not ours.
                if (!IsCompatibleUnclaimedHda(output, sampleRate) || output.IsRunning)
                    return false;
                // Start can fail after partially enabling the stream engine.
                // Mark cleanup as required before invoking the driver.
                started = true;
                output.Start();
                // Start() can reconfigure a codec or lose the output. Check
                // again before the FIRST write, then before every subsequent
                // write and after the final settle wait. A format drift must
                // abort without queuing any further incompatible PCM.
                if (!IsCompatibleUnclaimedHda(output, sampleRate) || !output.IsRunning)
                {
                    Console.WriteLine("[AUDIO] 1MS REFUSED: HDA changed during Start.");
                    return false;
                }
                bool fed = AudioProbeRingFeeder.FeedAndSettle(pcm,
                    (data, offset, count) => output.Write(data.AsSpan(offset, count)),
                    () => IsCompatibleUnclaimedHda(output, sampleRate) && output.IsRunning,
                    milliseconds => Cosmos.Kernel.System.Timers.TimerManager.Wait(milliseconds),
                    out int waits);
                Console.WriteLine("[AUDIO] 1MS diagnostic ring writer " +
                    (fed ? "ACCEPTED_AND_DRAINED" : "FAILED") +
                    " waits=" + waits + " settle=86ms (not proof of audible PCM)");
                return fed;
            }
            finally
            {
                try
                {
                    if (started)
                    {
                        // AudioDevice.Player is owned by stock AudioPlayback.
                        // If another player claimed this unowned diagnostic
                        // output during Start/Write/settle, stopping it here
                        // would tear down someone else's stream. A snapshot
                        // cannot make this path atomic: remain opt-in only.
                        // A withdrawn AudioDevice is no longer a safe target
                        // for driver Stop(): its backing HDA stream may have
                        // been removed. Never call into a withdrawn device or
                        // tear down a stream claimed by another player.
                        if (output.IsWithdrawn)
                            Console.WriteLine("[AUDIO] 1MS cleanup: output withdrawn; Stop skipped.");
                        else if (output.Player != null)
                            Console.WriteLine("[AUDIO] 1MS cleanup: player claimed output; Stop skipped.");
                        else
                            output.Stop();
                    }
                }
                finally
                {
                    // Release even when Start/Write/Stop throws.
                    System.Threading.Volatile.Write(ref _directProbeBusy, 0);
                }
            }
        }
    }
}
#endif
