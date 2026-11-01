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
  in memory; larger scans report unavailable until incremental scans exist;
- returns an on-disk, non-atomic scan, not a reservation or coherent snapshot
  during concurrent writes; cached/unflushed filesystem updates may not appear.

Disk retains the root Partition object chosen by the existing ZONDERQDATA/RAM
mount policy. No discovery index is used to choose the scan target. `space`
and the storage information window's mounts view call the same API. GUI scans
happen on explicit view selection/F5, never during rendering. No physical
partition is written, formatted, mounted or relabelled by this feature.

Validation on 2026-11-01: 31 allocation checks and 11 command behavior checks
passed using production sources compiled with Roslyn and .NET 11 RC in an
isolated scratch directory. The new free-root regression fails against the
previous production scanner ("invalid image accepted"). The image fixture now
sets BPB_Media to 0xF8 to match its pre-existing FAT[0] value. No physical disks
were accessed. Full kernel build was attempted but stopped at SDK resolution
(global.json 10.0.0); QEMU is unavailable. The policy suite remains 7/8 due to
its existing 3.0.94-only assertion; stock Cosmos remains 3.0.95.
