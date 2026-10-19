"""Runner regression tests using a fake QEMU; these do not boot Cosmos."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


RUNNER = Path(__file__).resolve().parents[2] / "tools/smoke-cosmos-3094-qemu.sh"


class GuestFailureTests(unittest.TestCase):
    def test_guest_failure_stops_boot_wait(self):
        for evidence in (
            "KERNEL PANIC: bootstrap failed",
            "System halted.",
            "[SMP3094] AP bootstrap failed",
            "[SMP3094] AP bootstrap timeout",
        ):
            with self.subTest(evidence=evidence), tempfile.TemporaryDirectory(dir=RUNNER.parent) as directory:
                root = Path(directory)
                iso = root / "probe.iso"
                iso.write_bytes(b"fake ISO for runner test")
                qemu = root / "qemu-system-x86_64"
                qemu.write_text(
                    "#!/bin/bash\n"
                    "for argument in \"$@\"; do\n"
                    "  if [[ $argument == file:* ]]; then\n"
                    "    printf '%s\\n' \"$FAKE_GUEST_FAILURE\" > \"${argument#file:}\"\n"
                    "  fi\n"
                    "done\n"
                    "# Stay alive until the runner cleans up its guest process.\n"
                    "exec sleep 30\n"
                )
                qemu.chmod(0o755)
                result = subprocess.run(
                    ["bash", str(RUNNER), str(iso), str(root / "serial.log")],
                    env={
                        **os.environ,
                        "PATH": str(root) + os.pathsep + os.environ["PATH"],
                        "FAKE_GUEST_FAILURE": evidence,
                        "SMP3094_QEMU_CPUS": "2",
                        "SMP3094_QEMU_CORES": "2",
                        "SMP3094_QEMU_THREADS": "1",
                        "SMP3094_QEMU_TIMEOUT": "3",
                        "SMP3094_QEMU_STABLE": "1",
                    },
                    capture_output=True, text=True, timeout=8,
                )
                self.assertEqual(result.returncode, 1, result.stderr)
                self.assertIn("Guest failed before BOOT", result.stderr)
                self.assertIn(evidence, result.stderr)
                self.assertNotIn("proof did not arrive", result.stderr)


if __name__ == "__main__":
    unittest.main()
