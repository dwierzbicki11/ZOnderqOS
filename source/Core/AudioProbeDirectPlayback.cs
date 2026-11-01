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
        internal static bool TryPlay(AudioDevice output, byte[] pcm, uint sampleRate)
        {
            if (output == null || output.IsWithdrawn || output.Name != "hda" ||
                output.DriverName != "HdAudioDriver" ||
                output.Player != null || output.IsRunning ||
                sampleRate != 48000 || pcm == null || pcm.Length == 0 ||
                pcm.Length > 48000 * 2 * 4 || pcm.Length % 4 != 0)
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: requires idle stock HDA and bounded stereo16.");
                return false;
            }

            if (!output.TrySetFormat(AudioFormat.Stereo16, sampleRate))
            {
                Console.WriteLine("[AUDIO] 1MS REFUSED: HDA format rejected.");
                return false;
            }

            bool started = false;
            try
            {
                // Start can fail after partially enabling the stream engine.
                // Mark cleanup as required before invoking the driver.
                started = true;
                output.Start();
                bool fed = AudioProbeRingFeeder.Feed(pcm,
                    (data, offset, count) => output.Write(data.AsSpan(offset, count)),
                    () => !output.IsWithdrawn && output.IsRunning,
                    milliseconds => Cosmos.Kernel.System.Timers.TimerManager.Wait(milliseconds),
                    out int waits);
                Console.WriteLine("[AUDIO] 1MS diagnostic ring writer " +
                    (fed ? "ACCEPTED_AND_DRAINED" : "FAILED") +
                    " waits=" + waits + " (not proof of audible PCM)");
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
