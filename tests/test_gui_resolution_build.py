#!/usr/bin/env python3
"""Host-only contracts for the isolated Limine framebuffer test profile."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
BUILD = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SELECTOR = (ROOT / "source/GUI/DisplayModeSelector.cs").read_text()
LIMINE = (ROOT / "Bootloader/limine.conf").read_text()


class IsolatedFramebufferBuildTests(unittest.TestCase):
    def test_production_keeps_native_mode_override_disabled(self):
        self.assertIn(
            '<ZonderqGuiTestNativeFramebuffer Condition="\'$(ZonderqGuiTestNativeFramebuffer)\' == \'\'">false',
            PROJECT)
        self.assertIn("ZONDERQ_GUI_TEST_NATIVE_FRAMEBUFFER", PROJECT)
        self.assertIn("#if ZONDERQ_GUI_TEST_NATIVE_FRAMEBUFFER", SELECTOR)
        self.assertIn("return canvas;", SELECTOR)
        self.assertIn("chosen = SelectBest(current, canvas.AvailableModes);", SELECTOR)
        self.assertIn('-p:ZonderqGuiTestNativeFramebuffer="$GUI_TEST_NATIVE"', BUILD)

    def test_only_staged_bootloader_is_rewritten(self):
        self.assertIn('git -C "$ROOT" archive HEAD | tar -xf - -C "$STAGE"', BUILD)
        self.assertIn('python3 - "$STAGE/Bootloader/limine.conf" "$GUI_TEST_MODE"', BUILD)
        self.assertIn('[[ "$MODE" == disk ]]', BUILD)
        self.assertIn('800x600|1360x768|1366x768|1920x1080', BUILD)
        self.assertIn('GUI_TEST_NATIVE=false', BUILD)
        self.assertIn('GUI_TEST_NATIVE=true', BUILD)
        self.assertIn('resolution: 1366x768x32', LIMINE)

    def test_resolution_jobs_track_gui_and_probe_changes(self):
        common = ("source/GUI/**", "Bootloader/limine.conf",
                  "ZonderqOS.csproj", "tools/build-cosmos-3095-local.sh",
                  "tools/smoke-services-qemu.sh", "tools/qmp-gui-*.py",
                  "tests/test_gui_resolution_build.py",
                  "tests/test_qmp_gui_taskmanager.py")
        for name in ("gui-800-qemu.yml", "gui-hd-qemu.yml"):
            workflow = (ROOT / ".github/workflows" / name).read_text()
            for path in common:
                self.assertIn("- '" + path + "'", workflow, name + ": " + path)

    def test_hd_qemu_uses_disposable_uefi_gop_and_keeps_bios_default(self):
        smoke = (ROOT / "tools/smoke-services-qemu.sh").read_text()
        hd = (ROOT / ".github/workflows/gui-hd-qemu.yml").read_text()
        regular = (ROOT / ".github/workflows/services-qemu.yml").read_text()
        self.assertIn('FIRMWARE="${SERVICE_QEMU_FIRMWARE:-bios}"', smoke)
        self.assertIn('[[ "$FIRMWARE" == bios || "$FIRMWARE" == ovmf ]]', smoke)
        self.assertIn('cp "$OVMF_VARS" "$OUT/ovmf-vars.fd"', smoke)
        self.assertIn('VIDEO="${SERVICE_QEMU_VIDEO:-std}"', smoke)
        self.assertIn('[[ "$VIDEO" == std || "$VIDEO" == virtio || "$VIDEO" == qxl ]]', smoke)
        self.assertIn('"${FIRMWARE_ARGS[@]}" -vga "$VIDEO"', smoke)
        self.assertIn("mtools ovmf", hd)
        self.assertIn("SERVICE_QEMU_FIRMWARE: 'ovmf'", hd)
        self.assertIn("SERVICE_QEMU_VIDEO: ${{ matrix.video }}", hd)
        self.assertIn("resolution: '1360x768'", hd)
        self.assertIn("resolution: '1366x768'", hd)
        self.assertIn("video: 'qxl'", hd)
        self.assertIn("resolution: '1920x1080'", hd)
        self.assertIn("video: 'std'", hd)
        for mode, adapter in (("1360x768", "std"), ("1366x768", "qxl"),
                              ("1920x1080", "std")):
            self.assertRegex(hd, r"resolution: '" + mode +
                             r"'\\s*video: '" + adapter + r"'")
        self.assertNotIn("SERVICE_QEMU_FIRMWARE: 'ovmf'", regular)
        self.assertIn('tail -n 50 "$OUT/$phase.filemanager-probe.log" >&2', smoke)
        self.assertIn('tail -n 50 "$OUT/$phase.taskmanager-probe.log" >&2', smoke)

    def test_qemu_workflows_do_not_mask_failed_guest_probes_with_tee(self):
        # A previous HD probe emitted [SERVICES-QEMU][FAIL] but the workflow
        # appeared green because tee, not the failing guest probe, set status.
        for filename in ("services-qemu.yml", "gui-800-qemu.yml",
                         "gui-hd-qemu.yml"):
            workflow = (ROOT / ".github/workflows" / filename).read_text()
            self.assertIn(
                "run: |\n          set -euo pipefail\n          bash tools/smoke-services-qemu.sh",
                workflow, filename)

    def test_limines_resolution_regex_preserves_newline_and_other_entries(self):
        match = re.search(r'modified, count = re.subn\(r"([^"]+)"', BUILD)
        self.assertIsNotNone(match)
        expression = match.group(1)
        sample = "timeout: 5\n/ZonderqOS\n    resolution: 1366x768x32\n    path: boot():/boot/ZonderqOS.elf\n"
        replaced, count = re.subn(expression,
                                  lambda found: found.group(1) + "800x600x32",
                                  sample)
        self.assertEqual(count, 1)
        self.assertEqual(replaced,
                         sample.replace("1366x768x32", "800x600x32"))
        self.assertEqual(re.subn(expression,
                                 lambda found: found.group(1) + "800x600x32",
                                 "timeout: 5\n")[1], 0)


    def test_invalid_firmware_mode_cannot_outvote_safe_32bit_mode(self):
        self.assertIn("bool currentUsable = (int)current.ColorDepth == 32", SELECTOR)
        self.assertIn("current.Width <= MaxWidth && current.Height <= MaxHeight", SELECTOR)
        self.assertIn("long bestPixels = currentUsable ? currentPixels : -1;", SELECTOR)
        self.assertIn("pixels <= bestPixels", SELECTOR)
        # Host-only selection model; the real Cosmos driver still needs a
        # compile and a boot test before this is considered hardware-verified.
        candidates = [(640, 480, 32), (800, 600, 32),
                      (1366, 768, 32), (1920, 1080, 32),
                      (4000, 900, 32), (1280, 720, 24)]
        def select(current):
            w, h, depth = current
            usable = depth == 32 and 640 <= w <= 3840 and 480 <= h <= 2160 and w * h <= 3840 * 2160
            best, pixels = current, w * h if usable else -1
            for width, height, bpp in candidates:
                area = width * height
                if bpp != 32 or width < 640 or height < 480 or width > 3840 or height > 2160 or area > 3840 * 2160 or area <= pixels:
                    continue
                best, pixels = (width, height, bpp), area
            return best
        self.assertEqual(select((3840, 2160, 24)), (1920, 1080, 32))
        self.assertEqual(select((3840, 2160, 32)), (3840, 2160, 32))
        self.assertEqual(select((320, 200, 32)), (1920, 1080, 32))

if __name__ == "__main__":
    unittest.main(verbosity=2)
