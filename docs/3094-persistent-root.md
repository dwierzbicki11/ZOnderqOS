# Persistent ZonderqOS 3.0.94 on a physical disk (NOT RAM)

**Primary supported prototype:** the `zonderq-3094-NORMAL-GUI-REQUIRES-ZONDERQDATA`
ISO built from the Cosmos **v3.0.94** SDK. This image enables normal
`InitialSetupManager`, `LoginScreenManager`, `GuiManager`, USB HID input
and persistent FAT32 files. It is **not** the `FULL-GUI-RAM-ONLY` image.

## Disk and boot are two separate things

- The ISO still boots the kernel from a USB drive (or another supported
  boot medium). A full automatic bootloader installation onto an internal
  SSD is **not implemented/validated by this test**.
- The filesystem used as `/` is a **real persistent** FAT32 partition on a
  physical SSD/HDD/USB drive, explicitly identified as `ZONDERQDATA`.
  The account database, `/etc`, `/root`, icons and settings survive reboot.
- A **dedicated data partition** (not an existing ESP, Kali root or swap) is
  required. It must be:
  - FAT32 with exact 11-byte boot-sector label `ZONDERQDATA`;
  - a GPT Basic Data partition or MBR primary FAT32 type `0x0B`/`0x0C`;
  - unique across currently connected drives.
- It is selected from registered Cosmos block devices; if the partition is
  absent, ambiguous, malformed or inaccessible, boot **fails closed** before
  any user/configuration writes.
- Root FAT is explicitly bound to the chosen partition object, not to
  mutable partition index zero. Secondary/duplicate mounts are disabled
  until a safe stable-identity implementation is tested.

## Do NOT destroy the laptop's Kali partition

The previously observed laptop disk had Kali's ESP at `/dev/sda1`
(about 976 MiB), Kali root at `/dev/sda2` and swap at `/dev/sda3`.
Never reformat, relabel or repartition those based on stale assumptions.
A `ZONDERQDATA` label by itself is not permission to write to a disk.

**Before any physical provisioning:** in Kali collect and review
`lsblk -o NAME,SIZE,MODEL,FSTYPE,PARTTYPE,LABEL,MOUNTPOINTS,TRAN`,
`sudo parted -l`, and the free space/backups. If no independent empty disk
or already-unused region is available, STOP and choose a new drive instead
of changing the Kali root/EFI partition. Partition-table changes and
formatting are deliberate destructive operations and are not performed
by the ZonderqOS kernel in this branch.

## Reboot/persistence regression test

`tests/qemu-3094-persistent-disk.sh` runs in GitHub Actions on the normal
Cosmos 3.0.94 GUI candidate:
1. Creates an adversarial MBR `0xEF` EFI disk (FAT32 with spoofed
   `ZONDERQDATA` label), hashes its complete contents.
2. Creates a *separate* 512 MiB FAT32 MBR `0x0C` data disk with
   `ZONDERQDATA` label and an initial `PERSIST.TXT` sentinel.
3. Boots full GUI in QEMU with both disk images attached. Requires normal
   init-created `/etc`, `/root`, `/var` and unchanged EFI disk.
4. Boots again from the same data image and checks those directories and
   the persisted sentinel still exist; EFI SHA-256 must still match.

The gate detects missing persistent writes, wrong partition selection and
an inadvertent EFI write on the test hardware model. The result is **not**
proof of safe QEMU/live partitioning on the user's physical laptop;
physical USB/SATA controller and power-cycle persistence must be verified.

## Remaining limitations

- Native monitor resolution depends on modes exposed by Cosmos 3.0.94's
  graphics driver and Limine/GOP.
- The full Cosmos 3.0.94 managed SMP/OrionGC production regression matrix
  has not yet been fully ported. No blanket `main` merge until validated.
- FAT32 has no POSIX user/group permissions, is not journaled, and is
  unsuitable as a long-term multi-user OS root. This is a temporary
  persistent milestone; a safe ext2/other POSIX-capable FS, proper
  credentials storage and full bootloader installer remain future work.
