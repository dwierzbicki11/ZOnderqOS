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
