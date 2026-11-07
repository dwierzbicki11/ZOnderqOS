using System;
using System.Diagnostics;
using ZonderqOS.Hardware;

namespace ZonderqOS.Audio
{
    public sealed class MelodyPlayer
    {
        private Track currentTrack;
        private int currentTrackIndex;
        private int currentNoteIndex;
        private long noteStartTicks;
        private int noteElapsedFrames;
        private bool isPlaying;
        private bool isPaused;

        // Visualizer spectrum bars (8 frequency bands, normalized 0..10)
        public readonly int[] SpectrumBars = new int[8];
        public int CurrentFrequency { get; private set; }
        public bool IsPlaying => isPlaying && !isPaused;
        public int CurrentTrackIndex => currentTrackIndex;
        public Track CurrentTrack => currentTrack;
        public int CurrentNoteIndex => currentNoteIndex;

        public MelodyPlayer()
        {
            if (Melodies.Playlist.Length > 0)
                SetTrack(0);
        }

        public void SetTrack(int index)
        {
            if (index < 0 || index >= Melodies.Playlist.Length)
                return;

            Stop();
            currentTrackIndex = index;
            currentTrack = Melodies.Playlist[index];
            currentNoteIndex = 0;
        }

        public void Play()
        {
            if (currentTrack == null)
                return;

            isPlaying = true;
            isPaused = false;
            noteStartTicks = Stopwatch.GetTimestamp();
            noteElapsedFrames = 0;
            ApplyCurrentNote();
        }

        public void Play(int trackIndex)
        {
            SetTrack(trackIndex);
            Play();
        }

        public void Pause()
        {
            if (isPlaying && !isPaused)
            {
                isPaused = true;
                Speaker.Stop();
            }
        }

        public void Resume()
        {
            if (isPlaying && isPaused)
            {
                isPaused = false;
                noteStartTicks = Stopwatch.GetTimestamp();
                noteElapsedFrames = 0;
                ApplyCurrentNote();
            }
        }

        public void Stop()
        {
            isPlaying = false;
            isPaused = false;
            currentNoteIndex = 0;
            CurrentFrequency = 0;
            Speaker.Stop();

            for (int i = 0; i < SpectrumBars.Length; i++)
                SpectrumBars[i] = 0;
        }

        public void Next()
        {
            int nextIdx = (currentTrackIndex + 1) % Melodies.Playlist.Length;
            SetTrack(nextIdx);
            Play();
        }

        public void Previous()
        {
            int prevIdx = (currentTrackIndex - 1 + Melodies.Playlist.Length) % Melodies.Playlist.Length;
            SetTrack(prevIdx);
            Play();
        }

        private void ApplyCurrentNote()
        {
            if (currentTrack == null || currentTrack.Notes == null || currentTrack.Notes.Length == 0)
                return;

            if (currentNoteIndex >= currentTrack.Notes.Length)
            {
                Stop();
                return;
            }

            Note note = currentTrack.Notes[currentNoteIndex];
            CurrentFrequency = note.Frequency;

            if (note.Frequency > 0)
            {
                Speaker.Tone(note.Frequency);
                // Trigger visualizer bars
                int band = Math.Min(7, Math.Max(0, (note.Frequency - 200) / 180));
                SpectrumBars[band] = 10;
                if (band > 0) SpectrumBars[band - 1] = Math.Max(SpectrumBars[band - 1], 6);
                if (band < 7) SpectrumBars[band + 1] = Math.Max(SpectrumBars[band + 1], 6);
            }
            else
            {
                Speaker.Stop();
            }
        }

        public void Update()
        {
            // Decay spectrum bars gradually
            for (int i = 0; i < SpectrumBars.Length; i++)
            {
                if (SpectrumBars[i] > 0)
                    SpectrumBars[i]--;
            }

            if (!isPlaying || isPaused || currentTrack == null)
                return;

            noteElapsedFrames++;
            long now = Stopwatch.GetTimestamp();
            Note note = currentTrack.Notes[currentNoteIndex];

            long intervalTicks = 0;
            if (Stopwatch.Frequency > 0)
                intervalTicks = (Stopwatch.Frequency * (long)note.DurationMs) / 1000;

            int targetFrames = Math.Max(2, note.DurationMs / 16);

            bool timeDone = (Stopwatch.Frequency > 0 && noteStartTicks > 0 && now - noteStartTicks >= intervalTicks);
            bool frameDone = (noteElapsedFrames >= targetFrames);

            if (timeDone || frameDone)
            {
                currentNoteIndex++;
                noteStartTicks = now;
                noteElapsedFrames = 0;

                if (currentNoteIndex >= currentTrack.Notes.Length)
                {
                    // Finished track: loop or stop
                    Stop();
                }
                else
                {
                    ApplyCurrentNote();
                }
            }
        }
    }
}
