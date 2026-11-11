# ZonderqOS EFI BootGuard — current state and release contract

**Status: C decision core + native regression tests; not an installed EFI application.**
No update is allowed to activate a pending slot until a **separately installed,
verified, power-loss-tested** EFI loader drives this policy before Limine.
The kernel deliberately rejects the writable `BOOTGUARD_PREBOOT=1` flag as proof
of an installed guard.

## Boot chain needed for a production release

```
UEFI firmware
  -> EFI BootGuard (executed on every boot, independently of kernel)
     -> read validated ZONDERQDATA boot state
     -> validate pending/fallback ELF (at least ELF64 structure + stored digest)
     -> zq_bootguard_prepare(...)
     -> persist incremented attempt count, flush, reread/verify
     -> select verified Limine entry WITHOUT modifying foreign ESPs
     -> chainload Limine for chosen slot, or rescue entry
  -> ZonderqOS kernel
     -> authenticate session and render intended GUI / operate console
     -> confirm pending slot only after verified healthy mode
```

### Required invariant

If a new ELF never runs, it cannot reset the attempt counter. The preboot
program must count **before** chainloading the candidate. Attempts 1–3 run
pending; the next boot selects a verified fallback. A corrupt pending ELF
selects fallback immediately. If fallback is unavailable, use a separately
bootable rescue image; do not continue booting the corrupt candidate.

The boot-state flag `BOOTGUARD_PREBOOT` is a requested configuration only,
**not** installation attestation. An EFI executable must be present on the
actual UEFI boot path, and chainloading as well as fallback must be observed
with QEMU/OVMF power cycles, including after an ELF corruption or interrupted
FAT32 write.

### Persistence contract

1. Check that the volume is the dedicated `ZONDERQDATA` volume; do not write
   to any other FAT, host EFI System Partition or unrecognized disk.
2. Read a bounded, validated state record; reject duplicate keys, malformed
   integers, inconsistent pending/fallback slots and unexpected locations.
3. Persist attempt counter before executing an ELF. Use a redundant generation
   and checksum/CRC record with recovery, not a single truncate-and-rewrite
   operation on FAT32.
4. Verify recorded state after flushing it to the selected FAT32 device.
5. Store a separate rescue image and do not overwrite it during normal A/B
   staging, update or rollback.
6. Select the intended Limine entry from the **actual loaded configuration**.
   Validate which config path the firmware/bootloader consumes. The kernel's
   `/boot/limine.conf` may not be the firmware's active config.
7. Retain a healthy old image **and compatible shared files** through
   probation, not just an ELF.
8. Test at least three power-cycled failed starts and a corrupted pending ELF.
   QEMU + OVMF scenarios must assert the actual ELF selected, not merely
   simulate counter increments on host.
9. Require real physical-device test evidence on an approved test disk and
   preserve other partitions. Never infer successful recovery from a native
   policy unit test.

## What is currently tested

The portable C policy in `boot_policy.c` and `boot_policy.h` is compiled
with strict GCC warnings by `P0 Update Security Gate`. Its host smoke tests
cover failed attempts, attempt #3 success, immediate corrupt-ELF fallback,
missing fallback, impossible state, and wrong-session confirmation.

**This is necessary but not sufficient.** It is **not** a UEFI executable;
it cannot change firmware boot variables, identify an ESP, persist state,
validate a real disk FAT32 filesystem, or chainload Limine. The C# kernel
cannot provide pre-kernel fallback by itself.

## Release decision

Until the EFI adapter exists, ships with the ISO, and passes the above tests,
`BootSlotManager.HasVerifiedIndependentPrebootRecovery()` MUST remain false.
Do not replace it with a disk flag, file-exists check, environment override,
test key, or user setting. Draft PR #199 must not be merged as a complete
automatic updater.
