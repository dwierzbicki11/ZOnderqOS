using System;
using System.Collections.Generic;
using ZonderqOS.SystemCore;

internal static class AudioProbeFeederSmoke
{
    private static int checks;

    private static void Check(bool ok, string name)
    {
        checks++;
        if (!ok) throw new Exception("[AUDIO-FEEDER-HOST] FAIL " + name);
    }

    public static void Run()
    {
        byte[] pcm = new byte[4800 * 4];
        for (int i = 0; i < pcm.Length; i++)
            pcm[i] = (byte)((i * 17 + 3) & 255);
        byte[] original = (byte[])pcm.Clone();
        var written = new List<byte>();
        int pending = 0;
        int ticks = 0;
        int Write(byte[] source, int offset, int count)
        {
            int accepted = Math.Min(count, 4096 - pending);
            accepted -= accepted % 4;
            for (int i = 0; i < accepted; i++)
                written.Add(source[offset + i]);
            pending += accepted;
            return accepted;
        }
        void Wait(uint milliseconds)
        {
            Check(milliseconds == 1, "one-millisecond wait");
            ticks++;
            pending = Math.Max(0, pending - 1024);
        }
        bool ok = AudioProbeRingFeeder.Feed(pcm, Write, () => true, Wait, out int waits);
        Check(ok, "partial writes complete");
        Check(waits == ticks && waits > 0, "full ring actually waited");
        Check(written.Count == pcm.Length + 16384, "one silent ring drained");
        for (int i = 0; i < pcm.Length; i++)
        {
            Check(written[i] == pcm[i], "PCM byte order preserved");
            Check(pcm[i] == original[i], "source PCM unchanged");
        }
        for (int i = pcm.Length; i < written.Count; i++)
            Check(written[i] == 0, "drain is silent");

        // A two-second WAV is valid elsewhere in the pipeline. The opt-in
        // direct HDA feeder must accept the same exact PCM bound, including
        // partial-ring-sized tails, without truncation or source mutation.
        Check(AudioProbeRingFeeder.MaxPcmBytes == 48000 * 2 * 4,
            "two-second stereo16 diagnostic bound");
        byte[] twoSeconds = new byte[AudioProbeRingFeeder.MaxPcmBytes];
        twoSeconds[0] = 0x5A;
        twoSeconds[twoSeconds.Length - 1] = 0xA5;
        int pcmAccepted = 0;
        int silenceAccepted = 0;
        ok = AudioProbeRingFeeder.Feed(twoSeconds,
            (source, offset, count) =>
            {
                Check(count > 0 && count <= 4096 && count % 4 == 0,
                    "bounded frame-aligned writer request");
                Check(offset >= 0 && offset + count <= source.Length,
                    "writer request within source bounds");
                if (ReferenceEquals(source, twoSeconds))
                {
                    Check(offset == pcmAccepted, "sequential PCM writes");
                    pcmAccepted += count;
                }
                else
                {
                    Check(source.Length == 4096, "bounded silent ring block");
                    for (int i = offset; i < offset + count; i++)
                        Check(source[i] == 0, "silent ring contains only zeroes");
                    silenceAccepted += count;
                }
                return count;
            },
            () => true, _ => throw new Exception("unexpected wait with unblocked writer"),
            out waits);
        Check(ok && waits == 0 && pcmAccepted == twoSeconds.Length &&
            silenceAccepted == 16384, "full two-second PCM plus silent ring accepted");
        Check(twoSeconds[0] == 0x5A && twoSeconds[twoSeconds.Length - 1] == 0xA5,
            "two-second source remains unmodified");

        // The opt-in direct path must not stop the cyclic DMA stream
        // immediately after queuing its last silent frame. A single bounded
        // 86 ms settle wait covers the nominal 16 KiB/48 kHz stereo16 ring.
        int settleCalls = 0;
        int settledBytes = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { settledBytes += count; return count; },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 86, "one complete HDA ring settle interval");
                settleCalls++;
            }, out waits);
        Check(ok && waits == 0 && settleCalls == 1 && settledBytes == 4 + 16384,
            "direct probe queues silence and waits once before stop");

        bool withdrawnDuringSettle = false;
        settleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => count,
            () => !withdrawnDuringSettle,
            milliseconds => { withdrawnDuringSettle = true; settleCalls++; }, out waits);
        Check(!ok && settleCalls == 1 && waits == 0,
            "withdrawn output during final settle fails closed");

        // Withdrawal can happen after the first post-feed readiness
        // snapshot but before the long 86 ms settle sleep. Never sleep
        // against a stale controller even though all bytes were accepted.
        int settleReadinessChecks = 0;
        int staleSettleSleeps = 0;
        int staleSettleWrites = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { staleSettleWrites++; return count; },
            () => ++settleReadinessChecks <= 12,
            milliseconds => staleSettleSleeps++, out waits);
        Check(!ok && staleSettleSleeps == 0 && waits == 0 &&
            staleSettleWrites == 5 && settleReadinessChecks == 13,
            "withdrawal between settle snapshots skips 86ms delay");

        settleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => count,
            () => false,
            milliseconds => settleCalls++, out waits);
        Check(!ok && settleCalls == 0 && waits == 0,
            "withdrawn output before feeding never enters settle wait");

        bool overBoundRejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[AudioProbeRingFeeder.MaxPcmBytes + 4],
                (a, b, c) => c, () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { overBoundRejected = true; }
        Check(overBoundRejected, "two seconds plus one frame rejected");

        // The output can disappear immediately after accepting the final
        // silent frame. A successful byte count alone is not enough.
        int acceptedBeforeWithdrawal = 0;
        bool withdrawnAfterLastFrame = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) =>
            {
                acceptedBeforeWithdrawal += c;
                if (acceptedBeforeWithdrawal == 4 + 16384)
                    withdrawnAfterLastFrame = true;
                return c;
            },
            () => !withdrawnAfterLastFrame, _ => { }, out waits);
        Check(!ok && withdrawnAfterLastFrame && acceptedBeforeWithdrawal == 4 + 16384,
            "withdrawn output after final silent frame fails closed");

        int acceptedBeforeMidDrainWithdrawal = 0;
        bool withdrawnDuringDrain = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) =>
            {
                acceptedBeforeMidDrainWithdrawal += c;
                if (acceptedBeforeMidDrainWithdrawal >= 4 + 4096)
                    withdrawnDuringDrain = true;
                return c;
            },
            () => !withdrawnDuringDrain, _ => { }, out waits);
        Check(!ok && withdrawnDuringDrain && acceptedBeforeMidDrainWithdrawal == 4 + 4096,
            "withdrawn output during silent drain fails closed");

        // A driver can withdraw the output while returning zero bytes.
        // Do not issue even one 1 ms wait after that failed write.
        bool withdrawnDuringZeroWrite = false;
        int zeroWriteCalls = 0;
        int waitsAfterZeroWithdrawal = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) =>
            {
                zeroWriteCalls++;
                withdrawnDuringZeroWrite = true;
                return 0;
            },
            () => !withdrawnDuringZeroWrite,
            _ => waitsAfterZeroWithdrawal++, out waits);
        Check(!ok && zeroWriteCalls == 1 && waitsAfterZeroWithdrawal == 0 &&
            waits == 0, "withdrawal during zero-byte write skips stale ring wait");

        // Withdrawal can occur after the post-Write check but before
        // the ring-full sleep. Do not wait on that stale device snapshot.
        int readinessChecksBeforeWait = 0;
        int writesBeforeStaleWait = 0;
        int staleWaitCallbacks = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) => { writesBeforeStaleWait++; return 0; },
            () => ++readinessChecksBeforeWait < 3,
            _ => staleWaitCallbacks++, out waits);
        Check(!ok && readinessChecksBeforeWait == 3 &&
            writesBeforeStaleWait == 1 && staleWaitCallbacks == 0 && waits == 0,
            "withdrawal between zero-write check and wait skips stale sleep");

        // A device may disappear while the 1 ms ring-full wait is running.
        // The feeder must recheck ownership before attempting another write.
        bool withdrawnInWait = false;
        int writesBeforeWaitWithdrawal = 0;
        int waitsBeforeWithdrawal = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (a, b, c) => { writesBeforeWaitWithdrawal++; return 0; },
            () => !withdrawnInWait,
            milliseconds =>
            {
                Check(milliseconds == 1, "withdrawal wait remains bounded");
                waitsBeforeWithdrawal++;
                withdrawnInWait = true;
            }, out waits);
        Check(!ok && writesBeforeWaitWithdrawal == 1 &&
            waitsBeforeWithdrawal == 1 && waits == 1,
            "withdrawal during ring-full wait prevents another write");

        // Withdrawal on a successful complete PCM write must abort before
        // allocating/queueing the first silent drain block. The driver's
        // positive count is not proof that the stream remains usable.
        int completedPcmWrites = 0;
        bool withdrawnOnCompletedPcm = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (source, offset, count) =>
            {
                completedPcmWrites++;
                withdrawnOnCompletedPcm = true;
                return count;
            },
            () => !withdrawnOnCompletedPcm,
            _ => throw new Exception("no wait after completed PCM withdrawal"),
            out waits);
        Check(!ok && completedPcmWrites == 1 && waits == 0,
            "complete positive PCM write withdrawal skips silent drain");

        // A positive partial write can also revoke the output. Neither the
        // remaining PCM nor the silent drain may be sent to that device.
        bool withdrawnAfterPartialWrite = false;
        int writesAfterPartialWithdrawal = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[8],
            (a, b, c) =>
            {
                writesAfterPartialWithdrawal++;
                withdrawnAfterPartialWrite = true;
                return 4;
            },
            () => !withdrawnAfterPartialWrite,
            _ => throw new Exception("unexpected wait after partial withdrawal"),
            out waits);
        Check(!ok && writesAfterPartialWithdrawal == 1 && waits == 0,
            "withdrawal after positive partial write prevents subsequent PCM");

        // The PCM payload may finish successfully while the silent DMA drain
        // is blocked. If the output disappears on that zero-byte drain write,
        // no stale 1 ms wait or further silence write is permitted.
        int drainWriteCalls = 0;
        int drainWaitCalls = 0;
        bool withdrawnOnBlockedDrain = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (source, offset, count) =>
            {
                drainWriteCalls++;
                if (drainWriteCalls == 1)
                    return count;
                withdrawnOnBlockedDrain = true;
                return 0;
            },
            () => !withdrawnOnBlockedDrain,
            _ => drainWaitCalls++, out waits);
        Check(!ok && withdrawnOnBlockedDrain && drainWriteCalls == 2 &&
            drainWaitCalls == 0 && waits == 0,
            "blocked silent drain withdrawal skips stale wait and writes");

        // The same transition can happen during the drain's bounded wait.
        // In that case exactly one wait is counted and no retry is issued.
        int drainWaitWrites = 0;
        int drainWaitCallbacks = 0;
        bool withdrawnWhileDrainWaits = false;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (source, offset, count) =>
            {
                drainWaitWrites++;
                return drainWaitWrites == 1 ? count : 0;
            },
            () => !withdrawnWhileDrainWaits,
            milliseconds =>
            {
                Check(milliseconds == 1, "silent drain wait is bounded");
                drainWaitCallbacks++;
                withdrawnWhileDrainWaits = true;
            }, out waits);
        Check(!ok && withdrawnWhileDrainWaits && drainWaitWrites == 2 &&
            drainWaitCallbacks == 1 && waits == 1,
            "withdrawal during blocked silent drain prevents retry");

        // The extended settle path must never sleep for 86 ms if the
        // controller disappears while the final silent ring is being queued.
        int failedSettleCalls = 0;
        int failedSettleWrites = 0;
        bool withdrawnBeforeSettle = false;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) =>
            {
                failedSettleWrites++;
                if (failedSettleWrites == 2)
                    withdrawnBeforeSettle = true;
                return count;
            },
            () => !withdrawnBeforeSettle,
            milliseconds => failedSettleCalls++, out waits);
        Check(!ok && failedSettleWrites == 2 &&
            failedSettleCalls == 0 && waits == 0,
            "withdrawal during silent ring prevents settle sleep");

        // A full ring that never becomes writable must also abort before
        // settling; the 1 ms backpressure budget is independent of settle.
        int blockedSettleWaits = 0;
        int blockedSettleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => 0,
            () => true,
            milliseconds =>
            {
                if (milliseconds == 1) blockedSettleWaits++;
                else blockedSettleCalls++;
            }, out waits);
        Check(!ok && blockedSettleWaits == 1000 &&
            blockedSettleCalls == 0 && waits == 1000,
            "stalled PCM never enters 86ms settle phase");

        // Stress a non-divisor partial-write size across 4096-byte block
        // boundaries. Inject backpressure between frames and verify that
        // neither PCM nor the final silent ring is duplicated or skipped.
        byte[] boundaryPcm = new byte[4096 * 2 + 12];
        for (int i = 0; i < boundaryPcm.Length; i++)
            boundaryPcm[i] = (byte)((i * 29 + 7) & 255);
        var boundaryOutput = new List<byte>();
        int boundaryWrites = 0;
        int boundaryWaits = 0;
        ok = AudioProbeRingFeeder.Feed(boundaryPcm,
            (source, offset, count) =>
            {
                boundaryWrites++;
                if (boundaryWrites % 5 == 0)
                    return 0;
                int accepted = Math.Min(count, 12);
                for (int i = 0; i < accepted; i++)
                    boundaryOutput.Add(source[offset + i]);
                return accepted;
            },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 1, "boundary backpressure remains bounded");
                boundaryWaits++;
            }, out waits);
        Check(ok && boundaryWaits == waits && boundaryWaits > 0,
            "partial writes with intermittent backpressure finish");
        Check(boundaryOutput.Count == boundaryPcm.Length + 16384,
            "partial boundary writes preserve exact PCM and drain length");
        for (int i = 0; i < boundaryPcm.Length; i++)
            Check(boundaryOutput[i] == boundaryPcm[i],
                "partial boundary writes preserve every PCM byte");
        for (int i = boundaryPcm.Length; i < boundaryOutput.Count; i++)
            Check(boundaryOutput[i] == 0,
                "partial boundary writes never replay PCM in silent drain");

        int stalledWaits = 0;
        ok = AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 0,
            () => true, _ => stalledWaits++, out waits);
        Check(!ok && stalledWaits == 1000 && waits == 1000,
            "permanently full ring fails closed after bounded waits");

        // A ring that occasionally accepts a frame can evade the 1000
        // consecutive-wait limit. The global 6000-wait budget must still
        // terminate, and telemetry must equal the actual wait callbacks.
        int waitsSinceProgress = 0;
        int totalWaitCallbacks = 0;
        int intermittentBytes = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[8 * 4],
            (a, b, c) =>
            {
                if (waitsSinceProgress < 900) return 0;
                waitsSinceProgress = 0;
                intermittentBytes += 4;
                return 4;
            },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 1, "intermittent stall waits one millisecond");
                waitsSinceProgress++;
                totalWaitCallbacks++;
            }, out waits);
        Check(!ok && waits == 6000 && totalWaitCallbacks == 6000 &&
            intermittentBytes == 24,
            "intermittent ring progress obeys exact 6000-wait global budget");

        // The global wait budget must also cover the silent tail, not just
        // the PCM payload. A slowly draining output can make one frame of
        // progress every 900 waits and otherwise avoid the consecutive cap.
        int tailWaitStreak = 0;
        int tailWaitCallbacks = 0;
        int tailAccepted = 0;
        int tailPcmWrites = 0;
        ok = AudioProbeRingFeeder.Feed(new byte[4],
            (source, offset, count) =>
            {
                if (tailPcmWrites++ == 0)
                    return count;
                if (tailWaitStreak < 900)
                    return 0;
                tailWaitStreak = 0;
                tailAccepted += 4;
                return 4;
            },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 1, "silent tail backpressure waits 1ms");
                tailWaitStreak++;
                tailWaitCallbacks++;
            }, out waits);
        Check(!ok && waits == 6000 && tailWaitCallbacks == 6000 &&
            tailAccepted == 24,
            "silent tail cannot bypass cumulative 6000-wait budget");

        // The consecutive backpressure limit must reset after actual frame
        // progress. A device that recovers on the 999th wait must be able
        // to finish, while the cumulative budget still spans all blocks.
        int nearLimitWaitStreak = 0;
        int nearLimitWaitCallbacks = 0;
        int nearLimitWrites = 0;
        int nearLimitAccepted = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) =>
            {
                nearLimitWrites++;
                if (nearLimitWrites == 1)
                {
                    nearLimitAccepted += count;
                    return count;
                }
                if (nearLimitWaitStreak < 999)
                    return 0;
                nearLimitWaitStreak = 0;
                nearLimitAccepted += count;
                return count;
            },
            () => true,
            milliseconds =>
            {
                if (milliseconds == 1)
                {
                    nearLimitWaitStreak++;
                    nearLimitWaitCallbacks++;
                }
                else
                    Check(milliseconds == 86, "recovered ring settles for 86ms");
            }, out waits);
        // Four silence blocks each recover after 999 waits: 3996 total,
        // below the 6000 global budget, despite repeated near-timeouts.
        Check(ok && waits == 3996 && nearLimitWaitCallbacks == 3996 &&
            nearLimitAccepted == 4 + 16384,
            "near-limit recovery resets consecutive budget without losing frames");

        int readyCalls = 0;
        ok = AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 4,
            () => ++readyCalls == 1, _ => { }, out waits);
        Check(!ok && readyCalls == 2, "withdrawn output stops immediately");

        bool rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => 3,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "unaligned driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => c + 4,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "overreported driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(pcm, (a, b, c) => -4,
                () => true, _ => { }, out waits);
        }
        catch (InvalidOperationException) { rejected = true; }
        Check(rejected, "negative driver write rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[3], (a, b, c) => c,
                () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "unaligned input rejected");

        rejected = false;
        try
        {
            AudioProbeRingFeeder.Feed(new byte[384004], (a, b, c) => c,
                () => true, _ => { }, out waits);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected, "oversized PCM rejected");
        // Reject an invalid payload before any driver callback is invoked.
        // This prevents malformed PCM from probing hardware or sleeping.
        int invalidCallbackCalls = 0;
        rejected = false;
        try
        {
            AudioProbeRingFeeder.FeedAndSettle(new byte[5],
                (a, b, c) => { invalidCallbackCalls++; return c; },
                () => { invalidCallbackCalls++; return true; },
                _ => invalidCallbackCalls++, out waits);
        }
        catch (ArgumentException) { rejected = true; }
        Check(rejected && invalidCallbackCalls == 0,
            "unaligned PCM rejected before device access or settle");

        // Invalid callback wiring must fail before invoking any remaining
        // device callback, including on the extended settle path.
        int nullWiringCallbacks = 0;
        bool nullWiringRejected = false;
        try
        {
            AudioProbeRingFeeder.FeedAndSettle(new byte[4], null,
                () => { nullWiringCallbacks++; return true; },
                _ => nullWiringCallbacks++, out waits);
        }
        catch (ArgumentNullException) { nullWiringRejected = true; }
        Check(nullWiringRejected && nullWiringCallbacks == 0,
            "null writer rejected without readiness checks or sleeps");

        nullWiringCallbacks = 0;
        nullWiringRejected = false;
        try
        {
            AudioProbeRingFeeder.FeedAndSettle(new byte[4],
                (a, b, c) => { nullWiringCallbacks++; return c; },
                null, _ => nullWiringCallbacks++, out waits);
        }
        catch (ArgumentNullException) { nullWiringRejected = true; }
        Check(nullWiringRejected && nullWiringCallbacks == 0,
            "null readiness callback rejected before any PCM write");

        nullWiringCallbacks = 0;
        nullWiringRejected = false;
        try
        {
            AudioProbeRingFeeder.FeedAndSettle(new byte[4],
                (a, b, c) => { nullWiringCallbacks++; return c; },
                () => { nullWiringCallbacks++; return true; },
                null, out waits);
        }
        catch (ArgumentNullException) { nullWiringRejected = true; }
        Check(nullWiringRejected && nullWiringCallbacks == 0,
            "null wait callback rejected before any device access");

        // A faulty HDA writer may behave correctly for PCM but return an
        // invalid count only when flushing the silent DMA ring. Reject that
        // result immediately: neither a retry nor the 86 ms settle delay is
        // safe after a broken device callback.
        foreach (int badDrainCount in new[] { -4, 3, 4100 })
        {
            int drainWrites = 0;
            int drainSleeps = 0;
            bool badDrainRejected = false;
            try
            {
                AudioProbeRingFeeder.FeedAndSettle(new byte[4],
                    (source, offset, count) =>
                    {
                        drainWrites++;
                        return drainWrites == 1 ? count : badDrainCount;
                    },
                    () => true,
                    _ => drainSleeps++, out waits);
            }
            catch (InvalidOperationException) { badDrainRejected = true; }
            Check(badDrainRejected && drainWrites == 2 && drainSleeps == 0 &&
                waits == 0, "invalid silent DMA drain write aborts before settle");
        }

        // The PCM write can succeed while the silent drain remains
        // permanently full. Enforce the same 1000 ms backpressure bound
        // during the drain, without entering the long settle interval.
        int fullDrainWrites = 0;
        int fullDrainWaits = 0;
        int fullDrainSettleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) =>
            {
                fullDrainWrites++;
                return fullDrainWrites == 1 ? count : 0;
            },
            () => true,
            milliseconds =>
            {
                if (milliseconds == 1) fullDrainWaits++;
                else fullDrainSettleCalls++;
            }, out waits);
        Check(!ok && fullDrainWrites == 1002 && fullDrainWaits == 1000 &&
            fullDrainSettleCalls == 0 && waits == 1000,
            "permanently full silent drain aborts without settle");

        // Accepted PCM and one full silent ring are not sufficient to report
        // playback success if the output disappears during the 86 ms settle.
        // The final readiness check must observe that withdrawal, while the
        // 1 ms backpressure counter remains zero.
        int settleWrites = 0;
        int settleLongWaits = 0;
        bool withdrawnDuringFinalSettle = false;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { settleWrites++; return count; },
            () => !withdrawnDuringFinalSettle,
            milliseconds =>
            {
                Check(milliseconds == 86, "unblocked ring only uses 86ms settle");
                settleLongWaits++;
                withdrawnDuringFinalSettle = true;
            }, out waits);
        Check(!ok && settleWrites == 5 && settleLongWaits == 1 &&
            waits == 0, "withdrawal during settle cannot report success");

        // A healthy controller should accept the PCM and four silence blocks
        // and settle exactly once, without counting that 86 ms as ring waits.
        int healthySettleWrites = 0;
        int healthySettleCalls = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { healthySettleWrites++; return count; },
            () => true,
            milliseconds =>
            {
                Check(milliseconds == 86, "healthy settle duration");
                healthySettleCalls++;
            }, out waits);
        Check(ok && healthySettleWrites == 5 && healthySettleCalls == 1 &&
            waits == 0, "healthy ring settles once without backpressure");

        // A device may disappear immediately after accepting a partial PCM
        // frame. No further bytes (including silent drain) may be submitted.
        int partialWithdrawalWrites = 0;
        int partialWithdrawalSleeps = 0;
        bool partialWithdrawalReady = true;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[16],
            (source, offset, count) =>
            {
                partialWithdrawalWrites++;
                partialWithdrawalReady = false;
                return 4;
            },
            () => partialWithdrawalReady,
            _ => partialWithdrawalSleeps++, out waits);
        Check(!ok && partialWithdrawalWrites == 1 &&
            partialWithdrawalSleeps == 0 && waits == 0,
            "withdrawal after partial positive PCM write prevents subsequent DMA submissions");

        // The 1 ms callback may withdraw the device. The next iteration
        // must recheck readiness before another write or a silent tail.
        int waitWithdrawalWrites = 0;
        int waitWithdrawalCalls = 0;
        bool waitWithdrawalReady = true;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => { waitWithdrawalWrites++; return 0; },
            () => waitWithdrawalReady,
            milliseconds =>
            {
                Check(milliseconds == 1, "withdrawal during backpressure only waits 1ms");
                waitWithdrawalCalls++;
                waitWithdrawalReady = false;
            }, out waits);
        Check(!ok && waitWithdrawalWrites == 1 &&
            waitWithdrawalCalls == 1 && waits == 1,
            "withdrawal during backpressure aborts before retry or settle");

        // Boundary contract: 999 blocked waits followed by a positive frame
        // must recover; a permanently blocked writer must stop at 1000.
        // Neither path may enter the final 86 ms settle unless drained.
        int boundaryWaitCount = 0;
        int boundarySettleCount = 0;
        int boundaryAcceptedBytes = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) =>
            {
                if (boundaryAcceptedBytes == 0 && boundaryWaitCount < 999)
                    return 0;
                boundaryAcceptedBytes += count;
                return count;
            },
            () => true,
            milliseconds =>
            {
                if (milliseconds == 1) boundaryWaitCount++;
                else if (milliseconds == 86) boundarySettleCount++;
                else throw new Exception("unexpected ring wait duration");
            }, out waits);
        Check(ok && waits == 999 && boundaryWaitCount == 999 &&
            boundaryAcceptedBytes == 4 + 16384 && boundarySettleCount == 1,
            "999 blocked waits recover and settle exactly once");

        int limitWaitCount = 0;
        int limitSettleCount = 0;
        ok = AudioProbeRingFeeder.FeedAndSettle(new byte[4],
            (source, offset, count) => 0,
            () => true,
            milliseconds =>
            {
                if (milliseconds == 1) limitWaitCount++;
                else limitSettleCount++;
            }, out waits);
        Check(!ok && waits == 1000 && limitWaitCount == 1000 &&
            limitSettleCount == 0,
            "1000 consecutive waits fail without silent settle");

        // A faulty driver must not be allowed to advance the PCM cursor with
        // a negative, oversized or non-frame-aligned byte count. In each case
        // the feeder must throw before submitting the silent DMA tail.
        foreach (int invalidCount in new[] { -4, 2, 8 })
        {
            int invalidWrites = 0;
            int invalidWaits = 0;
            bool invalidRejected = false;
            try
            {
                AudioProbeRingFeeder.FeedAndSettle(new byte[4],
                    (source, offset, count) =>
                    {
                        invalidWrites++;
                        return invalidCount;
                    },
                    () => true, _ => invalidWaits++, out waits);
            }
            catch (InvalidOperationException)
            {
                invalidRejected = true;
            }
            Check(invalidRejected && invalidWrites == 1 && invalidWaits == 0,
                "invalid device write count rejected before retry, silence or settle: " +
                invalidCount);
        }

        Console.WriteLine("[AUDIO-FEEDER-HOST] PASS " + checks +
            " checks (fake ring; not DMA, IRQ or real playback)");
    }
}
