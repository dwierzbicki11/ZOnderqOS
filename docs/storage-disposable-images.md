# Disposable FAT32 fixtures for stock Cosmos 3.0.95

Create a new 64 MiB sparse image in a **nonexistent** directory:

```sh
python3 tools/create-storage-test-image.py /tmp/storage-normal --scenario normal
python3 tools/create-storage-test-image.py /tmp/storage-full --scenario full
python3 tools/create-storage-test-image.py /tmp/storage-efi --scenario efi
python3 -m unittest discover -s tests -p test_storage_test_image.py -v
```

The generator refuses existing paths (including directories and symlinks).
It never accepts a block-device output and does not format, mount or enumerate
host disks. Failure can leave a partial new fixture directory: do not reuse it;
choose another fresh path. `manifest.json` records expected initial allocation.
Each scenario has two matching FATs, backup boot/FSInfo sectors and a root volume
label ZONDERQDATA. PAYLOAD.BIN crosses a cluster boundary and contains arbitrary
binary bytes, including NUL and invalid UTF-8. The full fixture uses a real
FILLER.BIN chain covering all remaining clusters, rather than lost allocations.
The EFI fixture retains the label but uses MBR type 0xEF: policy must reject it.

Use a separately built stock 3.0.95 ISO with normal disk-backed boot, not the
RAM-root or diagnostic profile. Example with a **single generated fixture**:

```sh
qemu-system-x86_64 -m 1024 -cdrom /path/to/ZonderqOS.iso \
  -drive file=/tmp/storage-normal/storage.raw,format=raw,if=ide,snapshot=on \
  -boot d
```

Attach no host block devices or other ZONDERQDATA volumes. `snapshot=on` keeps
writes in a temporary overlay; restarting restores fixture contents. Use a fresh
VM for each scenario. Never substitute a real disk path for the fixture file.

Integration checks to execute after a working kernel build:

- Normal: boot, compare initial `space` and disk-window allocation with manifest,
  then copy PAYLOAD.BIN to another physical path and compare all bytes. Initial
  free space will decrease if boot creates accounts/settings; account for those
  writes instead of asserting the pristine manifest after boot.
- Full: boot may fail while creating initial files. Verify explicit errors and
  absence of false success; this fixture does not guarantee reaching a shell.
  To test terminal full-disk behavior, first boot a normal fixture, then fill it
  through the running OS before testing copy/move failures and source retention.
- EFI: boot must refuse a persistent root before account/filesystem writes even
  though FAT32 and the ZONDERQDATA label are valid. No automatic formatting.

The Python suite independently parses on-disk geometry, FAT mirrors, free counts,
root entries and chains. It checks no cycles, cross-links or lost allocated
clusters in these fixtures, payload bytes, backup sectors and non-overwrite
behavior. This is **not** a Cosmos driver, QEMU boot or power-loss test. Generated
images and local manifests are disposable and must not be committed.

## Independent dosfstools check

With `fsck.fat` from dosfstools installed, run:

```sh
python3 -m unittest discover -s tests -p test_storage_image_fsck.py -v
# Or select a specific locally built executable:
FSCK_FAT=/path/to/fsck.fat python3 -m unittest discover -s tests -p test_storage_image_fsck.py -v
```

These five tests create fresh images and extract their partition into a temporary
regular file. They run `fsck.fat -n` (never repair), require status 0 for all three
valid FAT volumes, and require status 1 for a free root cluster and an orphan
cluster deliberately introduced into both FAT copies. SHA-256 before/after checks
verify that neither fsck nor extraction changes the source image or inspected
volume. Invocation failures/timeouts do not count as detected corruption.

Without dosfstools the five tests explicitly SKIP; the seven Python-only tests
still run separately. An invalid explicit FSCK_FAT fails instead of skipping.
The EFI case validates the filesystem only: rejecting its MBR type is Cosmos's
responsibility and remains an unexecuted kernel integration check.

Executed on 2026-11-02 with a locally compiled dosfstools revision
`289a48b9cb5b3c589391d28aa2515c325c932c7a`: all five checks passed, including
both corruption cases without modifying bytes. This is independent filesystem
validation, not a kernel boot or Cosmos write-driver test.

The historical `tests/test_3094_storage_policy.py` now checks stock **3.0.95**:
exact project SDK plus one unconditional reference each to Cosmos.Kernel and
Cosmos.Kernel.System at 3.0.95. Missing, duplicated, downgraded, conditional and
version-overridden references are rejected. Negative mutation tests exercise
both package pins; checks also run with Python optimization enabled. Existing
host/EFI, unique-label, mount and disabled-format source contracts remain intact.
This source-level gate does not resolve the invalid global.json or replace a
kernel build and hardware/QEMU tests. No SDK/package version was changed.
