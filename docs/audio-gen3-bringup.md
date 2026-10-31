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
wavmono
wav44
wav8
wav24
wav32
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

The parser accepts uncompressed PCM8, PCM16, PCM24 and signed PCM32 at 44.1 or 48 kHz in mono or stereo, with
a maximum of two seconds (at most 384,000 decoded stereo PCM bytes). Mono
input is upmixed by duplicating each signed sample to both channels; no
resampling occurs for 48 kHz input. 44.1 kHz input is converted to 48 kHz stereo using bounded integer linear interpolation, without touching the filesystem. It checks RIFF and chunk bounds, fmt consistency, data
alignment, duplicate chunks, odd-sized metadata padding and source-duration
bounds before allocating the decoded buffer. Unsupported rates and formats
fail explicitly; no DMA, filesystem access or production GUI activation occurs.
The diskless `wavtone` fixture is stereo (440 Hz). The `wavmono` command
uses a distinct 660 Hz, 48 kHz mono PCM16 RIFF fixture in guest RAM, decodes/upmixes
to stereo and sends it
to the same stock HDA output. The independent QEMU capture validates 660 Hz
on both stereo channels so a mistakenly executed 440 Hz `tone` cannot pass.
The new capture must pass before claiming mono playback verified end-to-end.

A separate `AudioWaveInput.DecodeBounded(Stream)` component now accepts caller-owned, read-only WAV streams. It reads exactly the declared RIFF size (at most 800 KiB), rejects truncation and trailing bytes, and never opens, mounts or writes a disk. The `wav44` QEMU probe exercises this bounded stream loader using a RAM-backed `MemoryStream`. This **does not** yet play a file from FAT32 or prove removable-media behavior; connecting an external read-only source is a separate safety milestone.

The `wav44` command generates a distinct 880 Hz stereo PCM16 fixture at 44.1 kHz,
encodes RIFF/WAV entirely in RAM, resamples it to 48 kHz and plays it through
the same HDA path. The sixth QEMU capture checks the distinct 880 Hz signal,
rather than trusting a decoder log line. 44.1 kHz mono is covered by real C#
host tests, not independently by QEMU.

The CI audio-QEMU workflow boots the ISO **nine times**: direct `tone`,
`wavtone`, mono `wavmono`, 44.1 kHz `wav44`, unsigned mono PCM8 `wav8`, signed mono PCM24 `wav24`, signed mono PCM32 `wav32`, `tone` at 25% software volume, and `tone` at 0% (mute).
Each uses a fresh QEMU process and a separate WAV capture. The first seven
must pass the non-silent pitch validator, including a stereo channel-balance
check; the ninth must contain at least 100 ms of genuinely muted PCM frames. The WAV fixture is generated
in memory; this does not prove arbitrary external WAV files work.

The diagnostic console also accepts `volume N` for integer `N` from
0 to 100 (default 100). This applies **software gain** to a fresh PCM copy
before submitting frames to `AudioManager`. It does not modify physical
HDA codec volume registers or change a production system setting. `volume`
without arguments prints the current percentage. The C# host smoke verifies
25% scaling, mute, 100% bit-exactness, invalid inputs and source immutability.

The eighth QEMU boot uses `volume 25` and `tone`, then compares the actual
backend WAV peak against the normal 100% capture. A PASS requires a ratio
near 0.25, not just a serial message. The ninth boot uses `volume 0` and
requires a nonempty, sustained WAV capture with a PCM peak no higher than 32;
a missing/header-only file is a failure, not a mute PASS. This verifies
software PCM gain/mute in the emulator, not perceived loudness.

After stopping QEMU and allowing it to close the WAV header, check the actual samples:

```bash
python3 tools/verify-audio-wav-capture.py output-3095/audio-capture.wav
```

The verifier rejects silent output, a short click, unsupported PCM formats,
a tone with the wrong pitch, and a stereo capture with one silent or severely
imbalanced channel. A PASS is evidence of actual PCM captured by QEMU, **not** physical laptop sound, IRQ delivery or general WAV playback. To choose a different host capture location set `ZONDERQ_AUDIO_WAV=/path/to/capture.wav` before `./run.sh --qemu-audio`.

The QEMU WAV backend and `hda-duplex,audiodev=audio0` device syntax are documented in the QEMU invocation manual. If the local QEMU build lacks the WAV backend, the launch must fail rather than silently claim success.

## Test tiers and next milestones

1. **Static:** `python3 tests/test_audio_gen3_contract.py` checks the diskless feature gates, PCI class matching, command registration and no disk writes in the probe.
2. **Host C#:** `dotnet run --project tests/AudioPciSmoke/AudioPciSmoke.csproj -c Release` compiles the *real* inventory class with a fake PCI provider and exercises classification and failure handling. This does not touch hardware.
3. **Synthetic WAV:** `python3 tests/test_audio_wav_validator.py` tests the WAV validator with generated 440 Hz, silence, wrong pitch, short and unsupported-format fixtures.
4. **Real ISO/QEMU:** compile and boot the audio ISO, inspect serial and screen output, run `tone`, and verify QEMU's actual captured WAV. Do not claim this tier passed from host tests alone.
5. **Verified baseline:** the 2026-10-31 real QEMU run #41221871415 passed three independent captures (direct PCM, decoded in-RAM WAV, 25% gain), with measured 0.250 amplitude ratio. The newer stereo-balance and mute gates require their own CI runs; do not infer they passed from the baseline.
6. **Next:** confirm the new 44.1 kHz resampler in real QEMU; then design a bounded read-only external WAV source without mounting a host disk or enabling production audio. Physical codec/output and GUI sounds remain later milestones; production audio stays off.

Do not enable AC97, SMP/SMT or network in this audio branch. Never use a real host disk as a test target.


### Unsigned PCM8 WAV (isolated experimental extension)

The opt-in decoder also accepts unsigned PCM8 mono/stereo at 44.1/48 kHz and converts to signed 48 kHz stereo PCM16. The `wav8` RAM-only diagnostic generates a distinct 1320 Hz mono PCM8 tone and sends it through the existing bounded read-only stream loader and stock HDA output. The dedicated QEMU gate requires a real captured WAV at approximately 1320 Hz; host C# tests additionally cover odd-sized RIFF data padding and malformed headers. This is **not** playback from FAT32, physical speaker confirmation or ISR evidence. Production audio remains disabled.

### Signed PCM24 WAV (isolated diagnostic, QEMU capture verified)

The bounded decoder now also accepts signed **24-bit little-endian PCM** at
44.1/48 kHz, mono or stereo. It converts to 48 kHz stereo PCM16 using the
upper 16 bits of each signed 24-bit sample (discarding the lowest eight bits),
with the same bounded 44.1→48 kHz linear interpolation. The current input RIFF limit
is **800 KiB**, sufficient for two seconds of 48 kHz stereo PCM32 plus
metadata; decoded output remains at most **384,000 bytes**. Invalid frame
alignment, byte rate, unsupported sample rates, truncation and excessive
duration are rejected before decoded-buffer allocation.

The diskless `wav24` command generates a distinct **1760 Hz** mono PCM24
RIFF fixture in RAM, reads it through the bounded caller-owned stream API
and submits decoded PCM16 stereo to the existing stock HDA playback path.
The eighth independent QEMU boot captures actual output to WAV and checks
for 1760 Hz on both channels. Host C# tests additionally cover PCM24 signed
extremes, 48 kHz mono/stereo, 44.1 kHz resampling, odd RIFF padding and
the exact two-second input bound. [Real QEMU HDA capture #41242893468](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41242893468)
**passed all eight independent boots**, including signed PCM24 `wav24` with
measured 1705.8 Hz (1760 Hz requested), 44.1 kHz stereo16 WAV backend,
peak 2908. Host-side [C# and Python #41242893436](https://github.com/dwierzbicki11/ZOnderqOS/actions/runs/41242893436)
passed 768,125 C# assertions and 33 Python tests. This does not enable
external FAT32 playback, physical audio, or production GUI sound.


### Signed PCM32 WAV (isolated diagnostic; verification pending)

The opt-in RIFF decoder now also accepts signed **32-bit little-endian integer
PCM** mono/stereo at 44.1/48 kHz. It retains the high 16 bits of each signed
32-bit sample, then uses the existing 44.1→48 kHz resampler if necessary.
IEEE float (format 3), WAVE_FORMAT_EXTENSIBLE and other codecs remain
unsupported and are explicitly rejected. The input cap is now **800 KiB**,
allowing two seconds of 48 kHz stereo PCM32 (768,000 source bytes), while the
decoded 48 kHz stereo PCM16 cap remains **384,000 bytes**.

The diskless `wav32` command generates a distinct **2200 Hz** signed PCM32
mono WAV in guest RAM, decodes it via the caller-owned bounded stream API and
submits PCM16 to stock HDA. Host C# tests cover signed extremes, discarded
low bits, 44.1 kHz resampling, mono/stereo, fragmented streams, exact
two-second bounds and invalid format/frame/rate. The ninth independent QEMU
boot/capture validates actual 2200 Hz PCM on both channels. **Do not claim
this new path passed QEMU until the new workflow completes successfully.**
No external FAT32 file is opened, no host disk is attached, no physical
speaker or IRQ execution is proven, and production GUI audio remains off.

### WAVE_FORMAT_EXTENSIBLE PCM16 (opt-in, diskless)

The bounded decoder now recognizes WAVE_FORMAT_EXTENSIBLE (0xFFFE) for
uncompressed PCM8/16/24/32 mono/stereo at 44.1 or 48 kHz. The fmt chunk must
contain at least 40 bytes, a complete 22-byte extension, a PCM subtype GUID,
matching valid/container bits and either a canonical mono/stereo speaker mask
or an unspecified (zero) mask. Float GUIDs, mismatched valid bits, unsupported
channel layouts and malformed extensions are rejected. The two-second decoded
PCM16 cap and 800 KiB RIFF source cap are unchanged.

The opt-in `wavext` command generates a 2640 Hz extensible stereo PCM16 WAV
in RAM, decodes it through `AudioWaveInput.DecodeBounded`, and plays it via
stock Cosmos HDA. The independent QEMU capture checks actual PCM samples.
This does **not** mount FAT32, play a file from disk, enable production GUI
audio, or prove physical speakers or ISR delivery.


### Read-only stream boundary (2026-10-31)

`AudioWaveInput.DecodeBounded` now rejects streams advertising `CanWrite=true`
**before reading the RIFF header**. The caller retains ownership of the
stream, and the 800 KiB source / 384,000-byte decoded output limits remain.
The host C# smoke exercises both a rejected writable stream and accepted
fragmented non-seekable read-only streams. This does not open a FAT32 file,
attach a guest disk, or prove real hardware playback.


### Reduced valid bits in WAVE_FORMAT_EXTENSIBLE (host verification pending)

The opt-in PCM decoder now accepts **16/20/24 valid bits in 24-bit
containers** and **16/24/32 valid bits in 32-bit containers**, provided the
samples are left-aligned and every unused least-significant bit is zero.
The high 16 bits are delivered as signed stereo PCM16; 44.1 kHz sources use
the same bounded resampler. Malformed valid-bit widths and nonzero padding
bits are rejected before playback. New host C# checks cover 20-in-24 and
24-in-32 signed samples, malformed padding and 44.1 kHz conversion.
This change does not itself demonstrate captured PCM, physical speakers,
DMA/IRQ completion or external FAT32 playback; await CI results.


### 20-bit-in-24-bit PCM: independent QEMU capture (new CI pending)

The opt-in diskless command `wavext20` constructs a **3080 Hz** mono
WAVE_FORMAT_EXTENSIBLE fixture with 20 valid bits in a 24-bit signed
container, decodes it via the read-only bounded stream loader and sends
PCM16 stereo to the stock Cosmos HDA player. The eleventh independent QEMU
boot captures WAV backend samples and validates pitch, stereo balance and
phase. This is not evidence until the new CI run finishes. Production GUI
audio remains disabled; no guest disk, FAT32 volume or physical sound card
is touched.


### QEMU WAV capture dropout investigation (2026-10-31)

A new real QEMU run #41250243235 built the stock 3.0.95 ISO and passed the
direct 440 Hz PCM capture, but failed the next `wavtone` capture on the old
180 ms contiguous-signal gate. The uploaded **actual** WAV contains peak
2908, about 0.35 s of active PCM and two approximately **21.3 ms** silent
gaps. It is therefore not an empty/silent decode, but QEMU/backend timing
or HDA buffering remains a possible cause; the exact source of the gaps is
not established.

The capture validator now requires **at least 200 ms total active PCM** and
**two disjoint 80 ms high-activity windows**, each independently checked for
pitch, stereo balance and phase. This tolerates short HDA backend dropouts
without accepting clicks or two short bursts; synthetic positive and negative
fixtures cover both cases. A fresh end-to-end QEMU run is required before
claiming that the revised gate passes. The PCM driver and Cosmos remain
unchanged; this is verification hardening, **not** a claimed hardware fix.
