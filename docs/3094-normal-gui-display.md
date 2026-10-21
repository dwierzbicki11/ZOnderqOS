# ZonderqOS — full GUI and display resolution on Cosmos **3.0.94**

This branch never builds a 3.0.85 kernel.

## Two mutually exclusive 3.0.94 images

1. `zonderq-3094-usb-input-EXPERIMENTAL` — read-only boot. The `desktop`
   command opens the diskless GUI with actual ZonderqOS Window/Button/Cursor
   widgets, but no accounts, VFS, file cache or installed applications.
2. `zonderq-3094-NORMAL-GUI-REQUIRES-ZONDERQDATA` — builds with
   `-p:ZonderqReadOnlyHardwareProbe=false` and
   `-p:ZonderqInputDiagnosticBoot=false`. It starts the standard
   `InitialSetupManager` / `LoginScreenManager` / `GuiManager` code path
   (full Start menu, taskbar, app manager, terminal, tools), with the same
   working USB HID `MouseManager` from Cosmos 3.0.94.

**The normal image must NEVER be booted as a production writable OS using
the laptop's Kali EFI partition.** The disk policy requires an intentionally
provisioned, *unique* FAT32 `ZONDERQDATA` partition with a GPT Basic Data
or MBR FAT32 type. The system refuses to mount any other root, including
GPT ESP and MBR `0xEF`. It does not automatically create or format
partitions. Back up data and identify devices before preparing any new
volume. The filesystem metadata and first-run user database WILL be
written on the selected dedicated data volume.

Normal GUI remains an **experimental candidate** until it has passed
multi-CPU managed runtime, authenticated session, USB storage and
physical-laptop validation. The successful USB mouse test alone is not
evidence of production managed SMP/OrionGC correctness.

## Resolution policy

`DisplayModeSelector.OpenNativeOrBest()` is used in the normal desktop,
initial setup, login, USB mouse GUI and diskless desktop.

- Start with the actual framebuffer mode from the driver kit.
- Inspect `Canvas.AvailableModes` and prefer the largest *advertised*
  32-bit mode up to 3840x2160, rather than hard-coding 1024x768 or
  1920x1080.
- Use the real `canvas.Width` and `canvas.Height` to configure mouse
  coordinates, widgets and layout.
- If the boot framebuffer cannot switch modes, preserve the firmware/
  Limine GOP mode. The bootloader config intentionally specifies no
  forced display resolution.
- The current Cosmos 3.0.94 display API does **not** expose a verified
  monitor EDID native-preferred mode. Therefore "largest supported" is
  a reasonable automatic policy, **not proof** it matches a laptop panel
  when its firmware exposes only a smaller framebuffer. Inspect the
  `[DISPLAY3094]` diagnostic output and the actual screen geometry.

## Physical laptop 800x600 fallback / 1366x768 request

A laptop with an actual 1366x768 (HD/768p) panel has been observed to boot the
normal disk-backed ZOnderqOS GUI at only **800x600**. That is a framebuffer
mode-selection problem, not the Recovery GUI. Cosmos cannot simply change
`canvas.Width` to fake more pixels: the bootloader and graphics hardware must
provide a backing framebuffer with matching pitch and allocated memory.

`Bootloader/limine.conf` now includes the **Limine v10 per-entry**
`resolution: 1366x768x32` for the **kernel** (not the menu
`interface_resolution`). Limine falls back to a mode the BIOS/VBE or UEFI/GOP
supports if the request cannot be fulfilled. The Cosmos display selector still
honours `Canvas.AvailableModes` and checks the actual framebuffer size rather
than assuming the request succeeded.

When testing on a UEFI-capable laptop choose the **UEFI** entry for the USB
stick if available; Legacy/CSM video firmware may not expose 1366x768. If the
desktop still reports 800x600, inspect `[DISPLAY3094]` output, `canvas.Name`
(driver vs firmware framebuffer) and advertised modes to locate the missing
graphics capability. No claim is made that forcing the bootloader request
provides EDID detection or a working Intel native modeset.

**USB data preservation:** the user can have a separate FAT32 `ZONDERQDATA`
partition after the hybrid boot ISO on the same stick. Reflashing a newer ISO
with `dd of=/dev/sdb` rewrites the stick's MBR and removes its partition-table
entry (even if the FAT32 data sectors survive). A new ISO can also grow into
those data sectors. Before reflashing, back up the entire `ZONDERQDATA`
partition/files and verify partition boundaries and the new image size. Do not
assume a simple `dd` upgrade preserves the persistent root. For first-time
provisioning use the separately documented storage-safety process.

## A real HD 1366x768 laptop still boots at 800x600

The per-entry Limine `resolution: 1366x768x32` request did not upgrade the
physical laptop from 800x600. The request is best-effort and may be refused by
BIOS/VBE or UEFI/GOP; changing `Canvas.Width` in C# cannot create the missing
physical framebuffer. Cosmos v3.0.94's Broadwell IntelGraphicsDriver currently
takes over the firmware-programmed mode rather than performing a complete
panel modeset. A real Intel iGPU modeset would require validated register
timings/PLL/link programming and EDID/panel data; do not fabricate modes.

**Read-only physical troubleshooting** (normal ZOnderqOS GUI terminal):

```text
displayinfo
lspci -v
```

`displayinfo` reports the actual Cosmos display provider (for example
`intel-graphics` versus `firmware framebuffer`), real framebuffer size,
refresh rate and the display driver's enumerated modes. It does **not**
reprogram the framebuffer or mount any disk.

Try the **UEFI: SanDisk** boot entry (not Legacy USB / CSM) when offered; GOP
may expose an LCD-native mode not available through VBE. If both still show
only 800x600, diagnose firmware modes and Intel modeset support before
claiming HD is fixed.

Read-only validation that the new Limine config is actually installed on
the USB stick from Kali Linux:

```bash
sudo mkdir -p /mnt/zonderq-iso
sudo mount -t iso9660 -o ro /dev/sdb /mnt/zonderq-iso
grep -n 'resolution:' /mnt/zonderq-iso/boot/limine/limine.conf
sudo umount /mnt/zonderq-iso
```

Expected is `resolution: 1366x768x32`; if absent, an older ISO is still
installed on the USB. This does not verify whether UEFI selected the mode.

## Test gates

```sh
python3 tests/test_3094_display_gui.py
python3 tests/test_3094_storage_policy.py
```

The isolated 3.0.94 GitHub Actions workflow builds matching Cosmos SDK /
runtime packages, compiles both read-only and standard GUI images, runs
the read-only xHCI HID QEMU boot and SHA256 EFI canary test. The normal
GUI image is a **compile gate**, not a completed authenticated GUI/QEMU
hardware validation. Do not merge the entire version migration PR #156
to main until that and the 3.0.94 managed SMP/GC regression matrix pass.
