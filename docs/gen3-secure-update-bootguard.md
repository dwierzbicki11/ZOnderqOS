# ZonderqOS Gen 3 — release trust, EFI BootGuard, and A/B recovery

**Status:** development / non-production integration. Do **not** install the
new EFI first-stage or updater on a real internal SSD/ESP until full exact-image
QEMU and two physical-system tests are passed.

## Trust architecture

- The publisher's Ed25519 **private** key is generated offline and must stay
  outside this repository and release ISO.
- The matching **public** key is compiled into the kernel by
  `tools/provision-release-public-key.py`. Without the generated partial
  class the trusted key is empty, so installing updates is denied.
- `UPDATE_FORMAT=2`, `SIGNATURE_ALG=ED25519` and detached
  `update.manifest.sig` (exactly 64 raw bytes) are mandatory.
- The detached signature covers every original byte of `update.manifest`.
  The file list, metadata, kernel and payload SHA-256 are validated before
  each installation stage, not only once at discovery.
- Unlisted payload files, paths outside the managed `system/` and `usr/`
  roots, malformed hashes, duplicate entries, traversal and symlinks are
  rejected. Old publicly forgeable HMAC release keys are removed.

### Offline publisher setup (example)

```bash
umask 077
openssl genpkey -algorithm ED25519 -out "$HOME/zonderq-release-private.key"
openssl pkey -in "$HOME/zonderq-release-private.key" -pubout \
  -out "$HOME/zonderq-release-public.pem"
python3 tools/provision-release-public-key.py \
  --public-key "$HOME/zonderq-release-public.pem"
```

The generated `TrustedReleaseKey.Provisioned.g.cs` is ignored by Git and
contains only the public key. **Do not commit** the private key, any seed,
private PEM, secret, or signed production artifacts to PR test workflows.

For a release bundle directory containing `update.manifest`,
`ZonderqOS.elf`, and any declared `payload/` files:

```bash
python3 tools/verify-release-bundle.py sign \
  --bundle /path/to/release-bundle \
  --private-key "$HOME/zonderq-release-private.key"
python3 tools/verify-release-bundle.py verify \
  --bundle /path/to/release-bundle \
  --public-key "$HOME/zonderq-release-public.pem"
```

Keep the production private key on a trusted offline host. Using a test key
to build the end-user ISO breaks release authenticity.

## BootGuard, boot order, and recovery

- **UEFI x86_64 only.** BIOS and ARM64 do not gain independent boot
  recovery from this component.
- BootGuard is a GNU-EFI first-stage `BOOTX64.EFI`, and preserves the
  original Limine EFI application as `LIMINE.EFI`.
- BootGuard reads **only one** volume labeled `ZONDERQDATA` with a valid
  `/boot/boot_state.cfg`. It stores the attempt count before chainloading
  Limine and sends a `LoaderEntryOneShot` UEFI variable to request the
  pending slot.
- Three failed attempts boot the pending slot. The fourth UEFI start selects
  the previous fallback. The policy does not require the corrupt ELF to run.
- Limine configuration retains the **fallback as the safe default** during
  probation. Stable entry names are `ZonderqOS Slot A` and `ZonderqOS Slot B`.
- The booted fallback kernel then restores shared system payload and
  configuration before starting services; failure stops normal GUI boot.
- A new update is forbidden until the previous slot is confirmed healthy, so
  the only rollback snapshot cannot be overwritten.
- Kernel only confirms health after authenticated GUI first-frame render or
  successful diagnostic console login.

### Isolated tests

```bash
# Host testing (no writes to internal/foreign partitions):
python3 -m unittest discover -s tests -p 'test_bootguard_installer.py' -v
python3 -m unittest discover -s tests -p 'test_p0_release_signing.py' -v
gcc -std=c11 -O2 -Wall -Wextra -Werror tests/test_bootguard_policy.c \
  -o /tmp/zq_bootguard_policy
/tmp/zq_bootguard_policy

# GNU-EFI+OVMF+QEMU only:
make -C tools/bootguard
make -C tools/bootguard build/limine_stub.efi
bash tests/test_bootguard_ovmf.sh
```

`test_bootguard_ovmf.sh` uses a **test EFI stub**, not a production Limine,
to prove the B→B→B→A firmware-level selection and persistent FAT32 counter.

### EFI installation is NOT automatic

`tools/bootguard/install_bootguard.py` has dry-run default. It requires
explicit separate ESP and ZONDERQDATA mountpoints, the expected SHA-256 of
the current `BOOTX64.EFI`, a valid compiled BootGuard, and matching
Limine config without a shadow copy on ESP. Only use `--real-device --apply`
after a full disk-image QEMU recovery and physical-backup procedure have
passed. Avoid changes to Kali, Windows, other ESPs, or unidentified
partitions.

## Remaining mandatory release gates

1. Actual **production Limine** chainload test: boot a signed, known-good
   ELF in both slots and force a corrupted/absent ELF in the pending slot
   on the same disk image. Confirm three attempts, preboot fallback and
   restored shared payload, including after full guest power cycles.
2. Power-loss injection throughout FAT32 state updates, Limine config copies,
   kernel staging and payload replacement. Ensure no foreign writes and an
   independent rescue path even if FAT32 metadata tears.
3. Actual runtime verification of Ed25519 inside **Cosmos Gen 3 NativeAOT**,
   not only the .NET host smoke and compiler.
4. Two physical x86_64 machines with different displays, SATA/USB storage,
   cold restart, persistence, update, and fallback. Secure Boot scenarios
   require appropriate EFI signing and trust enrollment.
5. 60–120 min idle RAM and 100 application open/close cycle tests at release
   SHA. Check heap/GC vs allocator and page accounting. No unbounded growth.
6. Split/resolve unrelated integration conflicts and merge only with all
   mandatory workflow results on the **same** final SHA.

**IMPORTANT:** This change set is release-hardening code, not a certification
that all Alpha release gates have been met.
