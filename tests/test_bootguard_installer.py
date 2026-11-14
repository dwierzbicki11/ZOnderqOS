#!/usr/bin/env python3
"""File-level tests on isolated directories; NEVER touches a host EFI partition."""
import hashlib
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
INSTALLER = ROOT / "tools/bootguard/install_bootguard.py"
STATE = (
    "# ZonderqOS A/B Bootloader State Configuration\n"
    "ACTIVE_SLOT=A\nFALLBACK_SLOT=B\nPENDING_SLOT=\n"
    "BOOT_ATTEMPTS=0\nBOOTGUARD_PREBOOT=0\nBOOTGUARD_FALLBACK=0\n"
)

LIMINE_CONFIG = (
    "timeout: 5\n"
    "default_entry: 1\n"
    "remember_last_entry: no\n"
    "/ZonderqOS Slot A\n"
    "    protocol: limine\n"
    "    path: fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf\n"
    "/ZonderqOS Slot B\n"
    "    protocol: limine\n"
    "    path: fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf\n"
)


def fake_efi(marker: int) -> bytes:
    data = bytearray(512)
    data[0:2] = b"MZ"
    data[0x3c:0x40] = (0x80).to_bytes(4, "little")
    data[0x80:0x84] = b"PE\0\0"
    data[0x84:0x86] = (0x8664).to_bytes(2, "little")
    data[0x80+24:0x80+26] = (0x20b).to_bytes(2, "little")
    data[0x80+24+68:0x80+24+70] = (10).to_bytes(2, "little")
    data[-1] = marker
    return bytes(data)


class GuardInstallerTests(unittest.TestCase):
    def test_dry_run_then_guarded_install_and_no_repeat(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            esp = root / "esp"
            data = root / "data"
            boot = esp / "EFI" / "BOOT"
            boot.mkdir(parents=True)
            state_dir = data / "boot"
            state_dir.mkdir(parents=True)
            state_file = state_dir / "boot_state.cfg"
            state_file.write_text(STATE, encoding="ascii")
            (state_dir / "limine.conf").write_text(LIMINE_CONFIG)
            limine = boot / "BOOTX64.EFI"
            old_data = fake_efi(1)
            new_data = fake_efi(2)
            limine.write_bytes(old_data)
            guard = root / "bootguard.efi"
            guard.write_bytes(new_data)
            expected = hashlib.sha256(old_data).hexdigest()
            args = [
                sys.executable, str(INSTALLER),
                "--esp", str(esp), "--data", str(data),
                "--bootguard-efi", str(guard), "--limine-sha256", expected,
                "--fixture"
            ]

            def run(*extra):
                return subprocess.run(args + list(extra), capture_output=True, text=True)

            self.assertEqual(run().returncode, 0)
            self.assertEqual(limine.read_bytes(), old_data)
            self.assertEqual(state_file.read_text(), STATE)
            self.assertFalse((boot / "LIMINE.EFI").exists())
            self.assertEqual(run("--apply").returncode, 0)
            self.assertEqual(limine.read_bytes(), new_data)
            self.assertEqual((boot / "LIMINE.EFI").read_bytes(), old_data)
            self.assertIn("BOOTGUARD_PREBOOT=1\n", state_file.read_text())
            self.assertNotEqual(run("--apply").returncode, 0, "repeat install must be rejected")

    def test_esp_shadowing_limine_config_is_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "esp/EFI/BOOT").mkdir(parents=True)
            (root / "data/boot").mkdir(parents=True)
            (root / "esp/EFI/BOOT/BOOTX64.EFI").write_bytes(fake_efi(1))
            guard = root / "guard.efi"
            guard.write_bytes(fake_efi(2))
            (root / "data/boot/boot_state.cfg").write_text(STATE)
            (root / "data/boot/limine.conf").write_text(LIMINE_CONFIG)
            shadow = root / "esp/EFI/BOOT/limine.conf"
            shadow.write_text("default_entry: 1\n")
            result = subprocess.run([
                sys.executable, str(INSTALLER), "--esp", str(root / "esp"),
                "--data", str(root / "data"),
                "--bootguard-efi", str(guard),
                "--limine-sha256", hashlib.sha256(fake_efi(1)).hexdigest(),
                "--fixture", "--apply",
            ], capture_output=True, text=True)
            self.assertNotEqual(result.returncode, 0, result.stdout)
            self.assertIn("higher-priority", result.stderr)
            self.assertEqual(
                (root / "esp/EFI/BOOT/BOOTX64.EFI").read_bytes(), fake_efi(1))

    def test_bad_original_hash_and_pending_update_rejected(self):
        with tempfile.TemporaryDirectory() as tmp:
            root = Path(tmp)
            (root / "esp/EFI/BOOT").mkdir(parents=True)
            (root / "data/boot").mkdir(parents=True)
            (root / "esp/EFI/BOOT/BOOTX64.EFI").write_bytes(fake_efi(1))
            guard = root / "guard.efi"
            guard.write_bytes(fake_efi(2))
            state = root / "data/boot/boot_state.cfg"
            state.write_text(STATE.replace("PENDING_SLOT=\n", "PENDING_SLOT=B\n"))
            (root / "data/boot/limine.conf").write_text(LIMINE_CONFIG)
            args = [
                sys.executable, str(INSTALLER), "--esp", str(root / "esp"),
                "--data", str(root / "data"), "--bootguard-efi", str(guard),
                "--limine-sha256", hashlib.sha256(fake_efi(1)).hexdigest(),
                "--fixture", "--apply"
            ]
            self.assertNotEqual(subprocess.run(args, capture_output=True).returncode, 0)
            state.write_text(STATE)
            args[args.index("--limine-sha256")+1] = "0" * 64
            self.assertNotEqual(subprocess.run(args, capture_output=True).returncode, 0)
            self.assertEqual((root / "esp/EFI/BOOT/BOOTX64.EFI").read_bytes(), fake_efi(1))


if __name__ == "__main__":
    unittest.main()
