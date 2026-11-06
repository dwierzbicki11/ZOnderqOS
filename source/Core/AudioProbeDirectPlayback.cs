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
            try
            {
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
            catch (InvalidOperationException)
            {
                // A reset or withdrawn HDA codec may reject a format read.
                // Refuse the snapshot before starting or feeding PCM.
                return false;
            }
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
            bool startCompleted = false;
            try
            {
                // Recheck after acquiring our local diagnostic guard. A
                // withdrawn, running or newly claimed device is not ours.
                if (!IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) || output.IsRunning)
                    return false;
                // Start can fail after partially enabling the stream engine.
                // Mark cleanup as required before invoking the driver.
                started = true;
                try
                {
                    output.Start();
                }
                catch (InvalidOperationException)
                {
                    // A codec reset can reject Start after partially touching
                    // the stream. Keep started=true for guarded cleanup, but
                    // never attempt to feed PCM after a failed start.
                    Console.WriteLine("[AUDIO] 1MS FAILED: HDA Start rejected.");
                    return false;
                }
                startCompleted = true;
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
                // Snapshot the stock driver's reported free ring capacity.
                // A changing value is useful for QEMU stall correlation, but
                // is NOT an independent DMA position or IRQ counter.
                int ringSamples = 0;
                int ringWriteSamples = 0;
                int ringWaitSamples = 0;
                int ringChanges = 0;
                int ringWaitIncreases = 0;
                int ringWaitDecreases = 0;
                int ringIncreases = 0;
                int ringDecreases = 0;
                long ringMin = long.MaxValue;
                long ringMax = long.MinValue;
                long previousWritable = 0;
                bool CanSampleDriverWritable()
                {
                    // Never sample an unbound, claimed, withdrawn or replaced
                    // output. Avoid Format: this telemetry must not touch the
                    // codec configuration or change the feeder's readiness.
                    return !output.IsWithdrawn && output.Player == null && output.IsRunning &&
                        output.Name == "hda" && output.DriverName == "HdAudioDriver" &&
                        output.SampleRate == sampleRate &&
                        (stillSelected == null || stillSelected());
                }
                void SampleDriverWritable(bool afterWrite)
                {
                    if (!CanSampleDriverWritable())
                        return;
                    long writable;
                    try
                    {
                        writable = (long)output.WritableBytes;
                    }
                    catch (InvalidOperationException)
                    {
                        // A withdrawn or reset stock HDA ring may reject a
                        // telemetry read. This optional observation must not
                        // crash the diagnostic or mask the feeder's safety gate.
                        return;
                    }
                    // WritableBytes is a public getter, not an atomic device
                    // snapshot. It can withdraw/rebind the controller while
                    // reading: discard the sample if its identity drifted.
                    if (!CanSampleDriverWritable() || writable < 0)
                        return;
                    if (ringSamples > 0 && writable != previousWritable)
                    {
                        ringChanges++;
                        if (writable > previousWritable)
                        {
                            ringIncreases++;
                            if (!afterWrite) ringWaitIncreases++;
                        }
                        else
                        {
                            ringDecreases++;
                            if (!afterWrite) ringWaitDecreases++;
                        }
                    }
                    if (afterWrite) ringWriteSamples++;
                    else if (ringSamples > 0) ringWaitSamples++;
                    previousWritable = writable;
                    ringSamples++;
                    if (writable < ringMin) ringMin = writable;
                    if (writable > ringMax) ringMax = writable;
                }
                SampleDriverWritable(false);
                bool fed;
                // Keep the wait count in scope after the guarded feeder call.
                int waits = 0;
                try
                {
                    fed = AudioProbeRingFeeder.FeedAndSettle(pcm,
                    (data, offset, count) =>
                    {
                        // The feeder's ready() check and this callback are
                        // separate calls. Recheck immediately before Write,
                        // including the published format, to narrow the
                        // reconfiguration window without touching DMA/IRQ.
                        if (!IsCompatibleUnclaimedHda(output, sampleRate, stillSelected) ||
                            !output.IsRunning)
                            return 0;
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
                            // Observe capacity after successful writes, not
                            // only after backpressure/settle. This separates
                            // writer-driven ring changes from wait samples.
                            // It is still NOT an independent DMA/IRQ counter.
                            SampleDriverWritable(true);
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
                        SampleDriverWritable(false);
                    },
                    out waits);
                }
                catch (InvalidOperationException)
                {
                    // A malformed Write() result is a device contract failure,
                    // not a reason to crash the opt-in diagnostic console.
                    // The finally block still performs guarded cleanup.
                    Console.WriteLine("[AUDIO] 1MS FAILED: invalid PCM write count.");
                    return false;
                }
                // Keep each line short enough for the diagnostic text console
                // and QEMU after.ppm capture. Console output is not a serial
                // telemetry channel and these are software ring counters only.
                Console.WriteLine("[AUDIO] 1MS diagnostic ring writer " +
                    (fed ? "ACCEPTED_TO_RING" : "FAILED"));
                Console.WriteLine("[AUDIO] 1MS pcmQueued=" + pcmQueued +
                    " silenceQueued=" + silenceQueued);
                Console.WriteLine("[AUDIO] 1MS writeCalls=" + writeCalls +
                    " zeroWrites=" + zeroWrites + " partialWrites=" + partialWrites);
                Console.WriteLine("[AUDIO] 1MS longestZeroStreak=" + longestZeroStreak +
                    " minPositiveWrite=" + (smallestPositiveWrite == int.MaxValue ? 0 : smallestPositiveWrite));
                Console.WriteLine("[AUDIO] 1MS waits=" + waits +
                    " settleRequests=" + settleRequests + " settle=86ms (requested only)");
                Console.WriteLine("[AUDIO] 1MS ringSamples=" + ringSamples +
                    " ringChanges=" + ringChanges);
                Console.WriteLine("[AUDIO] 1MS ringWriteSamples=" + ringWriteSamples +
                    " ringWaitSamples=" + ringWaitSamples);
                Console.WriteLine("[AUDIO] 1MS ringMin=" +
                    (ringSamples == 0 ? 0 : ringMin) + " ringMax=" +
                    (ringSamples == 0 ? 0 : ringMax));
                Console.WriteLine("[AUDIO] 1MS ringIncreases=" + ringIncreases +
                    " ringDecreases=" + ringDecreases);
                Console.WriteLine("[AUDIO] 1MS ringWaitIncreases=" + ringWaitIncreases +
                    " ringWaitDecreases=" + ringWaitDecreases);
                // Wait-phase free-space recovery is suggestive of ring
                // consumption, but an 86ms observation can alias a full
                // wrap. These counts do not prove DMA/IRQ or audible PCM.
                Console.WriteLine("[AUDIO] 1MS writable=writer+wait snapshots; DMA/IRQ not proven");
                Console.WriteLine("[AUDIO] 1MS DMA/IRQ and audible PCM not proven");
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
                        else if (!startCompleted && !output.IsRunning)
                            Console.WriteLine("[AUDIO] 1MS cleanup: Start failed without running stream; Stop skipped.");
                        else if (startCompleted && !output.IsRunning)
                            Console.WriteLine("[AUDIO] 1MS cleanup: output already stopped; Stop skipped.");
                        else if (output.Player != null)
                            Console.WriteLine("[AUDIO] 1MS cleanup: player claimed output; Stop skipped.");
                        else if (stillSelected != null && !stillSelected())
                            Console.WriteLine("[AUDIO] 1MS cleanup: output is no longer primary; Stop skipped.");
                        else if (output.Name != "hda" || output.DriverName != "HdAudioDriver" ||
                            output.SampleRate != sampleRate)
                            // A replaced controller must not have its Format getter
                            // called merely for cleanup: it may belong to another
                            // driver or trigger side effects during inspection.
                            Console.WriteLine("[AUDIO] 1MS cleanup: HDA identity changed; Stop skipped.");
                        else
                        {
                            // Read Format exactly once: two independent getter
                            // calls could observe different codec configurations.
                            // Revalidate ownership after the getter, which can
                            // itself trigger a withdrawal or reconfiguration.
                            // A codec reset can make Format unavailable even
                            // after a successful final settle. Do not let an
                            // optional diagnostic cleanup getter crash the shell
                            // or attempt Stop against an uncertain stream.
                            bool cleanupFormatValid;
                            try
                            {
                                var cleanupFormat = output.Format;
                                cleanupFormatValid = cleanupFormat.Channels == 2 &&
                                    cleanupFormat.ChannelSize == 2;
                            }
                            catch (InvalidOperationException)
                            {
                                Console.WriteLine("[AUDIO] 1MS cleanup: format unavailable; Stop skipped.");
                                cleanupFormatValid = false;
                            }
                            if (!cleanupFormatValid ||
                                output.IsWithdrawn || (startCompleted && !output.IsRunning) || output.Player != null ||
                                output.Name != "hda" || output.DriverName != "HdAudioDriver" ||
                                output.SampleRate != sampleRate ||
                                (stillSelected != null && !stillSelected()))
                                Console.WriteLine("[AUDIO] 1MS cleanup: HDA changed; Stop skipped.");
                            else
                            {
                                try
                                {
                                    output.Stop();
                                }
                                catch (InvalidOperationException)
                                {
                                    // A codec can withdraw between the final
                                    // ownership snapshot and Stop. Do not turn
                                    // diagnostic cleanup into a shell crash.
                                    Console.WriteLine("[AUDIO] 1MS cleanup: Stop rejected by HDA.");
                                }
                            }
                        }
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
