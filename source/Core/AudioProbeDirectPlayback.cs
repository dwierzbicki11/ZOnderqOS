#if ZONDERQ_AUDIO_PROBE
using System;
using Cosmos.Kernel.HAL.Devices.Audio;

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
        internal static bool TryPlay(AudioDevice output, byte[] pcm, uint sampleRate)
        {
            if (output == null || output.IsWithdrawn || output.Name != "hda" ||
                output.DriverName != "HdAudioDriver" ||
                output.Player != null || output.IsRunning ||
                sampleRate != 48000 || pcm == null || pcm.Length == 0 ||
                pcm.Length > AudioProbeRingFeeder.MaxPcmBytes || pcm.Length % 4 != 0)
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: requires idle stock HDA and bounded stereo16.");
                return false;
            }

            // The caller has already checked the published HDA format.
            // Do not reprogram the codec merely to run a diagnostic: changing
            // format here can reset a working cyclic stream or race a claim.
            // The direct path is still single-caller/opt-in, never production.
            if (output.SampleRate != sampleRate ||
                output.Format.Channels != 2 || output.Format.ChannelSize != 2)
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: existing HDA format is not stereo16/48kHz.");
                return false;
            }

            bool started = false;
            try
            {
                // Start can fail after partially enabling the stream engine.
                // Mark cleanup as required before invoking the driver.
                started = true;
                output.Start();
                bool fed = AudioProbeRingFeeder.FeedAndSettle(pcm,
                    (data, offset, count) => output.Write(data.AsSpan(offset, count)),
                    () => !output.IsWithdrawn && output.IsRunning,
                    milliseconds => Cosmos.Kernel.System.Timers.TimerManager.Wait(milliseconds),
                    out int waits);
                Console.WriteLine("[AUDIO] 1MS diagnostic ring writer " +
                    (fed ? "ACCEPTED_AND_DRAINED" : "FAILED") +
                    " waits=" + waits + " settle=86ms (not proof of audible PCM)");
                return fed;
            }
            finally
            {
                if (started)
                    output.Stop();
            }
        }
    }
}
#endif
