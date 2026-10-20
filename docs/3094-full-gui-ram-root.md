# Full ZonderqOS Gen 3 GUI in RAM — Cosmos **3.0.94**

This image gives the **real** login/setup and GuiManager path to physical
laptops without risking Kali's EFI partition.

## Build flags (required as a set)

```text
-p:ZonderqReadOnlyHardwareProbe=false
-p:ZonderqInputDiagnosticBoot=false
-p:ZonderqRamRoot=true
```

The standalone CI artifact is `zonderq-3094-FULL-GUI-RAM-ONLY`.
The same workflow still creates the strictly read-only `desktop` diagnostic
and a separate persistent-GUI candidate that requires a dedicated data disk.

## Storage security contract

- A private 64 MiB managed `byte[]` implements Cosmos 3.0.94's
  `IBlockDevice` interface. It contains an MBR primary FAT32-LBA partition
  starting at sector 2048.
- The kernel explicitly registers this RAM device with `StorageManager`.
  It formats only its `Partition` object using a new, device-bound
  `FatFileSystemType` and `FatFormatOptions`.
- Disk writes go exclusively to the private `byte[]`, and flush is a no-op.
  The `Disk.Initialize` RAM path never uses partition index 0 and does not
  automatically fall back to any physical `sda`/ESP/USB partition.
- The result is FAT32 with volume label `ZONDERQDATA`; the normal user
  creation, config files, wallpaper/icon caches and application files are
  stored there, in memory only.
- **All changes disappear on reboot.** This is for validating the full GUI,
  not a durable operating system installation. The initial user/password
  setup must be repeated after each restart.
- The 64 MiB large managed allocation and non-trivial userspace graphics
  work remain subject to real-hardware OrionGC/runtime validation.

## Acceptance

The 3.0.94 workflow:
1. Checks source and RAM-only write boundaries.
2. Builds matching 3.0.94 Cosmos SDK/runtime and separate ISOs.
3. Boots a QEMU xHCI HID read-only probe with a spoof-labelled EFI canary.
4. Boots this full GUI RAM image in QEMU with an EFI canary attached.
   Checks no serial panic for 35 seconds and byte-identical EFI canary.
   This does **not** replace interactive mouse + full-GUI laptop QA.

On bare metal confirm first-run login, buttons, taskbar, applications,
and monitor resolution. Do not mark the full production managed SMP/
OrionGC release as complete from this limited proof.
