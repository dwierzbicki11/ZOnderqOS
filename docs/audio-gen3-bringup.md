# ZonderqOS Gen3 — AUDIO (stock Cosmos 3.0.95)

## What is implemented

This branch uses the **existing upstream** `Cosmos.Kernel.Drivers.Pci.Audio.HdAudio.HdAudioDriver` and `Cosmos.Kernel.System.Audio.AudioManager`. No AC97 driver or custom DMA/IRQ implementation is added. The driver matches PCI `04:03:00`, not a hardcoded vendor ID. The kernel's `audiodiag` command separately reports PCI `04:01` (legacy audio) and `04:03` (HD Audio), with binding explicitly **unverified**.

The normal disk-backed GUI image has `CosmosEnableAudio=false`. To avoid changing the normal boot path, actual HDA initialization and playback are available **only** in an explicitly selected diskless diagnostic image. This image never mounts ZONDERQDATA, runs account setup, or writes to host partitions.

## Build the isolated audio image

From a clean checkout of `feat/audio-gen3` with .NET SDK 10 and the pinned Cosmos build prerequisites installed:

```bash
./run.sh --check
./run.sh --build-audio
```

This reuses the isolated exact-tag v3.0.95 Cosmos packages and builds `output-3095/ZonderqOS-3095-audio.iso`. The underlying builder selects `ZonderqAudioProbe=true`, `ZonderqReadOnlyHardwareProbe=true`, `ZonderqInputDiagnosticBoot=false`, `ZonderqRamRoot=false`. The project rejects `ZonderqAudioProbe=true` without the diskless profile. Normal `./run.sh --build` and `./run.sh --build-probe` keep audio disabled.

## QEMU with real emulated HD Audio and WAV capture

```bash
./run.sh --qemu-audio
```

This boots **one BSP CPU**, no virtual NIC and no host data disk. QEMU adds `intel-hda` plus `hda-duplex` and routes the output into a WAV backend. No guest FAT32 volume is attached. In the QEMU console enter:

```text
audiodiag
audioinfo
tone
wavtone
volume 25
tone
volume 100
```

`audiodiag` reports PCI configuration-space discovery only. `audioinfo` should list the output `hda` from `HdAudioDriver` if it bound successfully. `tone` submits a 250 ms, 440 Hz, 48 kHz, signed 16-bit stereo square wave through `AudioManager.Play` and reports `ACCEPTED_AND_DRAINED` only if the stock audio player returned true. This is **not** proof that any human heard sound.

The additional `wavtone` command constructs a canonical RIFF/WAVE container in
**guest RAM**, validates and decodes it with `AudioWavePcm.DecodeStereo16`,
then submits the decoded 48 kHz PCM16 stereo frames to the same stock HDA
path as `tone`. This tests the actual WAV parser-to-playback path without
opening any disk. It is **not** a file-browser WAV player yet.

The parser currently accepts only uncompressed PCM, 48 kHz, 16-bit stereo,
with a maximum of two seconds (384,000 PCM bytes). It checks RIFF and chunk
bounds, fmt consistency, data alignment, duplicate chunks and odd-sized
metadata padding. Unsupported rates and formats fail explicitly; no hidden
resampling, DMA, filesystem access or production GUI activation occurs.

The CI audio-QEMU workflow boots the ISO **twice**: once for `tone` and once
for `wavtone`, each with a fresh QEMU process and a separate WAV capture.
Both must pass the non-silent pitch validator. The WAV fixture is generated
in memory; this does not prove arbitrary external WAV files work.

The diagnostic console also accepts `volume N` for integer `N` from
0 to 100 (default 100). This applies **software gain** to a fresh PCM copy
before submitting frames to `AudioManager`. It does not modify physical
HDA codec volume registers or change a production system setting. `volume`
without arguments prints the current percentage. The C# host smoke verifies
25% scaling, mute, 100% bit-exactness, invalid inputs and source immutability.

The QEMU workflow performs a **third independent boot** with `volume 25`
and `tone`, then compares the actual backend WAV peak against the normal
100% capture. A PASS requires a ratio near 0.25, not just a serial message.
This verifies software PCM gain in the emulator, not perceived loudness.

After stopping QEMU and allowing it to close the WAV header, check the actual samples:

```bash
python3 tools/verify-audio-wav-capture.py output-3095/audio-capture.wav
```

The verifier rejects silent output, a short click, unsupported PCM formats and a tone with the wrong pitch. A PASS is evidence of actual PCM captured by QEMU, **not** physical laptop sound, IRQ delivery or general WAV playback. To choose a different host capture location set `ZONDERQ_AUDIO_WAV=/path/to/capture.wav` before `./run.sh --qemu-audio`.

The QEMU WAV backend and `hda-duplex,audiodev=audio0` device syntax are documented in the QEMU invocation manual. If the local QEMU build lacks the WAV backend, the launch must fail rather than silently claim success.

## Test tiers and next milestones

1. **Static:** `python3 tests/test_audio_gen3_contract.py` checks the diskless feature gates, PCI class matching, command registration and no disk writes in the probe.
2. **Host C#:** `dotnet run --project tests/AudioPciSmoke/AudioPciSmoke.csproj -c Release` compiles the *real* inventory class with a fake PCI provider and exercises classification and failure handling. This does not touch hardware.
3. **Synthetic WAV:** `python3 tests/test_audio_wav_validator.py` tests the WAV validator with generated 440 Hz, silence, wrong pitch, short and unsupported-format fixtures.
4. **Real ISO/QEMU:** compile and boot the audio ISO, inspect serial and screen output, run `tone`, and verify QEMU's actual captured WAV. Do not claim this tier passed from host tests alone.
5. **Next:** verify all three new QEMU captures (direct PCM, decoded in-RAM WAV, 25% gain), then consider guarded read-only external WAV access. Physical codec/output and GUI sounds remain later milestones; production audio stays off.

Do not enable AC97, SMP/SMT or network in this audio branch. Never use a real host disk as a test target.
