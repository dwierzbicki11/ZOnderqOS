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

        // A published output may be withdrawn, replaced as the primary, or have its format changed
        // between the initial probe and the next DMA-ring write. Never feed
        // stereo16 into a stream that no longer advertises stereo16/48 kHz.
        // The optional callback rechecks the caller's selected AudioManager.Primary
        // reference before/after Format and before every write/settle. It is
        // still a snapshot check, not an atomic claim.
        private static bool IsCompatibleUnclaimedHda(AudioDevice output, uint sampleRate,
            Func<bool> stillSelected)
        {
            if (output == null || output.IsWithdrawn ||
                output.Name != "hda" || output.DriverName != "HdAudioDriver" ||
                output.Player != null || output.SampleRate != sampleRate ||
                (stillSelected != null && !stillSelected()))
                return false;

            // Stock Cosmos 3.0.95 AudioFormat is a value type, not nullable.
            // A default/uninitialized format has zero channels/width and is
            // rejected by the explicit stereo16 check. Snapshot once to
            // avoid mixing fields from different published formats.
            // This still is NOT an atomic output ownership claim.
            var format = output.Format;
            // Format is a public getter, not an atomic ownership snapshot.
            // A stock player may claim/withdraw/reconfigure the output while
            // that getter executes. Recheck identity and ownership after reading
            // it so this probe does not queue a block to a replaced output.
            // This is still a best-effort snapshot, not an atomic claim.
            return format.Channels == 2 && format.ChannelSize == 2 &&
                !output.IsWithdrawn && output.Name == "hda" &&
                output.DriverName == "HdAudioDriver" &&
                output.Player == null && output.SampleRate == sampleRate &&
                (stillSelected == null || stillSelected());
        }

        internal static bool TryPlay(AudioDevice output, byte[] pcm, uint sampleRate,
            Func<bool> stillSelected = null)
        {
            if (sampleRate != 48000 || pcm == null || pcm.Length == 0 ||
                pcm.Length > AudioProbeRingFeeder.MaxPcmBytes || pcm.Length % 4 != 0 ||
                !IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) || output.IsRunning)
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
                if (!IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) || output.IsRunning)
                    return false;
                // Start can fail after partially enabling the stream engine.
                // Mark cleanup as required before invoking the driver.
                started = true;
                output.Start();
                // Start() can reconfigure a codec or lose the output. Check
                // again before the FIRST write, then before every subsequent
                // write and after the final settle wait. A format drift must
                // abort without queuing any further incompatible PCM.
                if (!IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) || !output.IsRunning)
                {
                    Console.WriteLine("[AUDIO] 1MS REFUSED: HDA changed during Start.");
                    return false;
                }
                // Count the actual Write() results, not requested bytes.
                // These counters distinguish PCM acceptance, silent tail
                // acceptance and backpressure. They do NOT measure DMA
                // position, IRQ delivery or physical sound.
                int writeCalls = 0;
                int zeroWrites = 0;
                int partialWrites = 0;
                int zeroStreak = 0;
                int longestZeroStreak = 0;
                int smallestPositiveWrite = int.MaxValue;
                int settleRequests = 0;
                int pcmQueued = 0;
                int silenceQueued = 0;
                bool fed = AudioProbeRingFeeder.FeedAndSettle(pcm,
                    (data, offset, count) =>
                    {
                        int accepted = output.Write(data.AsSpan(offset, count));
                        writeCalls++;
                        if (accepted == 0)
                        {
                            zeroWrites++;
                            zeroStreak++;
                            if (zeroStreak > longestZeroStreak)
                                longestZeroStreak = zeroStreak;
                        }
                        else if (accepted > 0 && accepted <= count && accepted % 4 == 0)
                        {
                            zeroStreak = 0;
                            if (accepted < count)
                                partialWrites++;
                            if (accepted < smallestPositiveWrite)
                                smallestPositiveWrite = accepted;
                            if (object.ReferenceEquals(data, pcm))
                                pcmQueued += accepted;
                            else
                                silenceQueued += accepted;
                        }
                        // The feeder independently rejects invalid counts.
                        return accepted;
                    },
                    () => IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) && output.IsRunning,
                    milliseconds =>
                    {
                        if (milliseconds == 86)
                            settleRequests++;
                        Cosmos.Kernel.System.Timers.TimerManager.Wait(milliseconds);
                    },
                    out int waits);
                Console.WriteLine("[AUDIO] 1MS diagnostic ring writer " +
                    (fed ? "ACCEPTED_TO_RING" : "FAILED") +
                    " pcmQueued=" + pcmQueued +
                    " silenceQueued=" + silenceQueued +
                    " writeCalls=" + writeCalls +
                    " zeroWrites=" + zeroWrites +
                    " partialWrites=" + partialWrites +
                    " longestZeroStreak=" + longestZeroStreak +
                    " minPositiveWrite=" + (smallestPositiveWrite == int.MaxValue ? 0 : smallestPositiveWrite) +
                    " waits=" + waits +
                    " settleRequests=" + settleRequests +
                    " settle=86ms (requested only; DMA/IRQ and audible PCM not proven)");
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
                        // been removed. Likewise, a replaced controller
                        // identity must not receive our Stop. Never call into
                        // a withdrawn/replaced device or tear down a stream
                        // claimed by another player.
                        if (output.IsWithdrawn)
                            Console.WriteLine("[AUDIO] 1MS cleanup: output withdrawn; Stop skipped.");
                        else if (output.Player != null)
                            Console.WriteLine("[AUDIO] 1MS cleanup: player claimed output; Stop skipped.");
                        else if (stillSelected != null && !stillSelected())
                            Console.WriteLine("[AUDIO] 1MS cleanup: output is no longer primary; Stop skipped.");
                        else if (output.Name != "hda" || output.DriverName != "HdAudioDriver")
                            Console.WriteLine("[AUDIO] 1MS cleanup: HDA identity changed; Stop skipped.");
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
