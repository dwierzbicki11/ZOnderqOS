# Experimental Cosmos 3.0.94 USB mouse bring-up

This branch is intentionally **not** the production ZonderqOS 3.0.85 release.

## Target hardware
- Intel Wildcat Point-LP xHCI: PCI `8086:9CB1` (`0C:03:30`)
- Maxxter optical mouse: USB `18F8:0F97`; driver match is HID boot mouse interface `03:01:02`, not vendor/product IDs
- ELAN touchpad: ACPI/I2C `ELAN1000:00` through `INT3433`; **not** supported by the upstream Cosmos 3.0.94 USB mouse driver

## Build and validation
- Only use the isolated `Experimental ZonderqOS Cosmos 3.0.94 USB input` GitHub Actions workflow.
- This workflow builds matching Cosmos 3.0.94 SDK and runtime packages, validates local NuGet provenance, and attempts to publish an experimental ISO.
- After CI succeeds, its `zonderq-3094-usb-input-EXPERIMENTAL` artifact is a **test-only** ISO; do not use `run.sh` from this branch because that launcher is intentionally pinned to production 3.0.85.
- Do **not** flash to the laptop until the ISO build succeeds and QEMU boot is verified.
- The test image enables `ZONDERQ_READONLY_HARDWARE_PROBE`: it does **not mount disks**, initialize users or start services. It presents a minimal console with `desktop` (real Window/Button/Cursor graphical interactions), `inputscan`, `mouse`, `mousegui`, `volumes`, and `help`. Only allowlisted diagnostic commands are accepted.
- **Bare metal USB mouse first:** boot the isolated 3.0.94 ISO on the laptop. Plug USB mouse directly into a laptop USB port. Type `inputscan` and look for `XhciDriver` and `UsbMouseDriver BOUND` + published pointer. Type `mousegui`; move mouse and press all buttons. Cursor should move and button indicators should light up; the screen logs counts. Press ESC to leave (firmware framebuffer may not restore text visibility; reboot if necessary).
- If no movement, run `mouse` from the console and copy/photograph its `Observed mouse state changes` output plus `inputscan` output; this distinguishes PCI controller detection from actual HID input.
- `mousegui` is **not yet** the authenticated desktop. The latter still requires a safely selected dedicated FAT32 data volume and the unported 3.0.94 managed SMP/GC runtime.
- The `ZONDERQ_INPUT_DIAGNOSTIC_BOOT` flag remains for ordinary console startup when the read-only probe is disabled.
- `inputscan` reports both raw PCI controller classes and real `DriverDiagnostics` USB nodes / bound pointer devices. It does not mistake finding a USB host for finding a mouse.
- On real hardware, verify keyboard, `inputscan` PCI xHCI, USB HID mouse response, storage, and system logs. No touchpad support is claimed.

## USB-to-GUI read-only integration
- Boot the Cosmos **3.0.94** isolated ISO on the laptop; keep the Kali EFI partition untouched.
- At `input-diag>`, enter `desktop` (alias `gui`).
- Test actual Cosmos `MouseManager` pointer dispatch into the same ZonderqOS `Window`, `Button` and `Cursor` classes used by the production GUI:
  - drag the window title; resize corners; maximize/minimize/close using caption buttons
  - click **TEST MOUSE** and **ABOUT**; use taskbar buttons to restore minimized/closed windows
  - click **START** to open the menu, switch to About or exit
  - press ESC to close menu/help, then leave the desktop
- This is a restricted **diskless desktop**, **not** the full authenticated user session. The ordinary `GuiManager.Run()` calls settings/services and caches PNG icons under `/root`, so it is intentionally not used until a safe filesystem is available. The diskless desktop reuses actual GUI widget classes and the live USB HID device without touching VFS.
- In diskless builds, caption controls are drawn from Canvas primitives; this bypasses the persistent `IconManager` file cache.
- This proves native GUI integration. It does not claim that all installed applications have been tested, or that ELAN I²C touchpad support exists.

## Production upgrade blockers
- Port full managed Stage-23 SMP, OrionGC STW/stack scanning, per-CPU timers and scheduler accounting to 3.0.94 (the native stages 1–8 are separate preliminary proofs).
- QEMU SMP matrix (1/2/4/8/16 logical CPUs), real CPU telemetry `htop`, ARM64 compile and physical Broadwell xHCI/USB boot verification.
- Preserve stable 3.0.85 branch and do not merge this draft before all gates are met.
