#!/usr/bin/env python3
"""Test Stage 23's CPU preflight with a fake QEMU; this is not a boot test."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parent.parent


class CpuModelPreflightTest(unittest.TestCase):
    def check_listing(self, listing, accepted, help_status=0):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            temp = Path(directory)
            qemu = temp / "qemu-system-x86_64"
            qemu.write_text(
                '#!/usr/bin/env bash\n'
                'if [[ "$*" == "-cpu help" ]]; then\n'
                '  printf "%s\\n" "$TEST_CPU_LIST"\n'
                '  exit "$TEST_HELP_STATUS"\n'
                'fi\n'
                'touch "$TEST_LAUNCHED"\n'
                'exit 99\n'
            )
            qemu.chmod(0o755)
            iso = temp / "test.iso"
            iso.touch()
            launched = temp / "launched"
            result = subprocess.run(
                ["bash", "tools/smoke-stage23-qemu.sh", str(iso), str(temp / "serial.log")],
                cwd=ROOT,
                env={**os.environ, "PATH": f'{temp}:{os.environ["PATH"]}',
                     "TEST_CPU_LIST": listing, "TEST_HELP_STATUS": str(help_status),
                     "TEST_LAUNCHED": str(launched), "STAGE23_QEMU_CPU_MODEL": "Nehalem"},
                capture_output=True, text=True, timeout=10,
            )
            self.assertNotEqual(result.returncode, 0)  # fake QEMU cannot boot
            self.assertEqual(launched.exists(), accepted, result.stdout + result.stderr)
            if accepted:
                self.assertNotIn("is not available", result.stderr)
            elif help_status:
                self.assertIn("cannot list QEMU CPU models", result.stderr)
            else:
                self.assertIn("CPU model 'Nehalem' is not available", result.stderr)

    def test_architecture_prefixed_listing(self):
        self.check_listing("Available CPUs:\nx86 Nehalem Intel Core i7 9xx", True)

    def test_unprefixed_listing(self):
        self.check_listing("Available CPUs:\n  Nehalem Intel Core i7 9xx", True)

    def test_similar_names_and_description_do_not_match(self):
        self.check_listing("x86 Nehalem-v1 Intel CPU\nNehalem-IBRS Intel CPU\nx86 other Nehalem", False)

    def test_empty_listing(self):
        self.check_listing("", False)

    def test_failed_help_command(self):
        self.check_listing("Nehalem Intel CPU", False, help_status=1)


if __name__ == "__main__":
    unittest.main()
