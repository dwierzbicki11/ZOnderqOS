# FAT32 allocation smoke tests

Run `dotnet run --project tests/Fat32AllocationSmoke/Fat32AllocationSmoke.csproj`
with a working .NET 10 SDK. The current repository global.json requests the
invalid SDK feature-band version 10.0.0; an isolated copy/manual compilation with
a working SDK can test the scanner without changing Cosmos package versions.

The harness compiles the production scanner against a read-only sector interface
and disposable in-memory FAT32 images. It checks geometry, active/mirrored FATs,
cluster counts, bad clusters, full disk, allocation/release, reserved bits, read
failures, scan budget and publication of results only after complete success.
It does not exercise Cosmos block drivers, filesystem writes, QEMU or the GUI.

The scanner:

- reads only the boot sector and required FAT sectors, never FSInfo hints/data;
- excludes reserved FAT entries, metadata, unused FAT padding and a trailing
  incomplete data cluster;
- reports allocated, free and bad cluster bytes separately;
- compares mirrored tables and refuses invalid pointers or geometry;
- validates the media descriptor and reserved headers of every selected FAT,
  while accepting the clean-shutdown/hard-error status bits in FAT[1];
- refuses statistics if the root directory cluster is free or bad, including
  when that entry is in a later FAT sector. This is not a full root-chain
  consistency check (cycles, cross-links and orphan clusters are not detected);
- reads at most 16 MiB of FAT data synchronously and keeps at most two sectors
  in memory; the synchronous wrapper rejects larger scans, while TryBegin /
  Scan.TryAdvance permits caller-scheduled incremental scans of larger volumes;
- returns an on-disk, non-atomic scan, not a reservation or coherent snapshot
  during concurrent writes; cached/unflushed filesystem updates may not appear.

Disk retains the root Partition object chosen by the existing ZONDERQDATA/RAM
mount policy. No discovery index is used to choose the scan target. `space`
uses the bounded synchronous API. The storage information window starts a bound
session on view selection/F5 and advances four FAT sectors (at most eight reads
with mirroring) per Update, never during rendering. Switching views cancels the
session; minimized/hidden windows pause it. This bounds read count, not driver
latency. Only a completed scan publishes totals. Sessions are single-caller
objects; abandon one to cancel, begin a new one to refresh. No physical
partition is written, formatted, mounted or relabelled by this feature.

Validation on 2026-11-01: 38 allocation checks and 11 command behavior checks
passed using production sources compiled with Roslyn and .NET 11 RC in an
isolated scratch directory. The new free-root regression fails against the
previous production scanner ("invalid image accepted"). The image fixture now
sets BPB_Media to 0xF8 to match its pre-existing FAT[0] value. No physical disks
were accessed. Full kernel build was attempted but stopped at SDK resolution
(global.json 10.0.0); QEMU is unavailable. The policy suite remains 7/8 due to
its existing 3.0.94-only assertion; stock Cosmos remains 3.0.95.

Incremental coverage includes a 2,200,000-cluster image above the 16 MiB
synchronous budget, per-step read limits, terminal disconnect/mirror failures,
zero-budget retries, completion without rereads and independent sessions.

`tests/StorageInfoSmoke` adds five UI boundary checks using the production
StorageInfoApp/scanner with stub window/driver APIs and the same image fixture.
They verify selection/render/update reads, minimize/pause, completion, refresh
and view cancellation, failure display and closed-window behavior. All five
passed; they are not tests of actual Cosmos rendering or device timing.
