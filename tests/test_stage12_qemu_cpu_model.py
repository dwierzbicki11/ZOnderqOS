"""CPU preflight regression tests with a fake QEMU; these do not test boot."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "tools/smoke-stage12-qemu.sh"


class CpuModelPreflightTests(unittest.TestCase):
    def run_preflight(self, listing, available, help_status=0):
        with tempfile.TemporaryDirectory(dir=SCRIPT.parent.parent) as directory:
            root = Path(directory)
            qemu = root / "qemu-system-x86_64"
            qemu.write_text(
                '#!/bin/bash\n'
                'if [[ "$*" == "-cpu help" ]]; then\n'
                '  printf "%s\\n" "$TEST_CPU_LIST"\n'
                '  exit "$TEST_HELP_STATUS"\n'
                'fi\n'
                'printf "%s\\n" "$@" > "$TEST_LAUNCH_ARGS"\n'
                'exit 1\n'
            )
            qemu.chmod(0o755)
            iso = root / "fixture.iso"
            iso.touch()
            launch = root / "launch.args"
            env = dict(os.environ, PATH=f"{root}:{os.environ['PATH']}",
                       TEST_CPU_LIST=listing, TEST_HELP_STATUS=str(help_status),
                       TEST_LAUNCH_ARGS=str(launch), STAGE12_QEMU_CPU_MODEL="Nehalem",
                       STAGE12_QEMU_CPUS="16", STAGE12_QEMU_SOCKETS="1",
                       STAGE12_QEMU_CORES="8", STAGE12_QEMU_THREADS="2",
                       STAGE12_QEMU_TIMEOUT_SECONDS="1", STAGE12_QEMU_STABLE_SECONDS="30")
            result = subprocess.run(
                ["bash", str(SCRIPT), str(iso), str(root / "serial.log")],
                env=env, capture_output=True, text=True, timeout=10,
            )
            self.assertNotEqual(result.returncode, 0)  # Fake QEMU cannot prove boot.
            if available:
                self.assertTrue(launch.exists(), result.stdout + result.stderr)
                self.assertIn("-cpu\nNehalem\n", launch.read_text())
                self.assertNotIn("is not available", result.stderr)
            else:
                self.assertFalse(launch.exists(), result.stdout + result.stderr)
                self.assertIn("cannot list" if help_status else "is not available", result.stderr)

    def test_cpu_listing_formats(self):
        for listing in (
            "Available CPUs:\nx86 Nehalem Intel Core i7 9xx",
            "Available CPUs:\nNehalem Intel Core i7 9xx",
        ):
            with self.subTest(listing=listing):
                self.run_preflight(listing, True)

    def test_missing_and_non_exact_models(self):
        for listing in ("", "Nehalem-v1 Intel Core", "x86 Nehalem-v1 Intel Core",
                        "Other description mentions Nehalem", "Other Nehalem description"):
            with self.subTest(listing=listing):
                self.run_preflight(listing, False)

    def test_failed_cpu_listing(self):
        self.run_preflight("Nehalem Intel Core", False, help_status=1)


if __name__ == "__main__":
    unittest.main()
