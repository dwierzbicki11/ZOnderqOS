"""Host-only CPU preflight regression tests; these do not boot QEMU."""

import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class CpuModelTests(unittest.TestCase):
    def preflight(self, listing, status=0):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            work = Path(directory)
            (work / "models").write_text(listing)
            iso = work / "fixture.iso"
            iso.touch()
            qemu = work / "qemu-system-x86_64"
            qemu.write_text(
                '#!/bin/bash\n'
                'if [[ "$*" == "-cpu help" ]]; then\n'
                '  cat "$FIXTURE_DIR/models"\n'
                '  exit "$HELP_STATUS"\n'
                'fi\n'
                'touch "$FIXTURE_DIR/launch-attempted"\n'
                'exit 1\n'
            )
            qemu.chmod(0o755)
            result = subprocess.run(
                ["bash", "tools/smoke-stage23-qemu.sh", str(iso), str(work / "serial")],
                cwd=ROOT,
                env={**os.environ, "PATH": f"{work}:{os.environ['PATH']}",
                     "FIXTURE_DIR": str(work), "HELP_STATUS": str(status),
                     "STAGE23_QEMU_CPU_MODEL": "Nehalem",
                     "STAGE23_QEMU_CPUS": "8", "STAGE23_QEMU_CORES": "8",
                     "STAGE23_QEMU_THREADS": "1"},
                capture_output=True, text=True, timeout=15,
            )
            return result, (work / "launch-attempted").exists()

    def test_supported_listing_formats_reach_launch(self):
        for listing in ("x86 Nehalem Intel Core i7\n", "Nehalem Intel Core i7\n"):
            with self.subTest(listing=listing):
                result, launched = self.preflight(listing)
                self.assertTrue(launched, result.stderr)
                self.assertNotEqual(result.returncode, 0)  # Stub cannot prove a boot.
                self.assertNotIn("is not available", result.stderr)

    def test_missing_model_is_rejected_exactly(self):
        result, launched = self.preflight(
            "Nehalem-v1 Intel Core i7\nx86 Nehalem-IBRS Intel\n"
            "Other Nehalem description\n")
        self.assertFalse(launched)
        self.assertEqual(result.returncode, 1)
        self.assertIn("QEMU CPU model 'Nehalem' is not available", result.stderr)

    def test_help_failure_is_rejected(self):
        result, launched = self.preflight("Nehalem Intel Core i7\n", status=1)
        self.assertFalse(launched)
        self.assertEqual(result.returncode, 1)
        self.assertIn("cannot list QEMU CPU models", result.stderr)


if __name__ == "__main__":
    unittest.main()
