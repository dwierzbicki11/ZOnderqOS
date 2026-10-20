# Cosmos 3.0.94 — safe persistent storage on bare metal

Status: **experimental / not merged into main**. The ISO tested on a real laptop
previously wrote into Kali's internal EFI System Partition because ZonderqOS
mounted FAT partition index 0 as `/`. That is explicitly forbidden now.

## Defaults

The **3.0.94 USB mouse diagnostic image** retains
`ZonderqReadOnlyHardwareProbe=true`. It does **not** mount any filesystem,
initialize user accounts or run startup services. Its commands are
`inputscan`, `mouse`, `volumes`, `help`. The `volumes` command inspects
Cosmos 3.0.94 block devices and checks data-volume eligibility without
mounting or modifying host partitions.

In a *non-probe* 3.0.94 build (`-p:ZonderqReadOnlyHardwareProbe=false`),
persistent boot is allowed **only** if storage probing finds exactly one
FAT32 volume with the on-disk 11-character FAT label **ZONDERQDATA**.

- A missing label, a failed sector read, malformed FAT BPB or two matching
  partitions causes **fail-closed boot before filesystem/account writes**.
- A FAT12/FAT16 EFI System Partition does not qualify.
- Even a FAT32 volume with the matching label is rejected if its partition
  table describes a GPT ESP or MBR EFI (0xEF) entry. Approved partition
  types are GPT Basic Data and primary MBR FAT32 (0x0B or 0x0C) only.
  Unknown, extended and damaged table layouts are rejected by default.
- The selected `Partition` object is passed to Cosmos 3.0.94's partition
  overload of `VfsManager.TryMount` rather than trusting integer index 0.
- GUI and shell secondary mounting routes enforce the same policy.
- `format` and `mkpart` are disabled in this experimental build. There is
  no automatic partition creation or formatting.

This is a safety gate, not an installer and **not** a guarantee of safe
operation on an arbitrarily prepared disk. FAT volume labels are not
cryptographic authentication. Never relabel an existing EFI or Kali
partition to `ZONDERQDATA`; use a separate, deliberately prepared data
partition on a backup/USB device. Confirm the *device*, free space and its
partition table in Kali before any partition editing. Back up data first.

On the observed laptop, `/dev/sda1` is Kali's FAT EFI partition. It must
remain untouched. The USB stick's ISO9660/FAT12 boot regions are not a data
volume, even though its underlying device has unused capacity.

## Validation

```bash
python3 tests/test_3094_storage_policy.py
```

The standalone GitHub Actions workflow
`Experimental ZonderqOS Cosmos 3.0.94 USB input` additionally builds all
SDK/runtime dependencies from upstream **v3.0.94**, compiles the image, and
tests QEMU xHCI USB HID enumeration. The regression script is a source-level
gate, **not** proof of a successful physical read/write or a working managed
SMP scheduler.

## Remaining work

1. Physical xHCI/HID read-only test on the Broadwell laptop.
2. Dedicated data volume provisioning and positive/negative hardware tests.
3. Migrate the complete production OrionGC / managed SMP Stage 23 patches
   into 3.0.94, then run real 1/2/4/8/16+ CPU regression gates.
4. Enable normal writable boot/GUI only after storage and GC correctness
   have been demonstrated. No merge to `main` before these gates.
