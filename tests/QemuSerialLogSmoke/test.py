"""Serial argument regression tests; these do not boot QEMU."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]


class SerialLogArguments(unittest.TestCase):
    def test_check_log_uses_serial_argument(self):
        with tempfile.TemporaryDirectory() as directory:
            tmp = Path(directory)
            iso = tmp / "not-used.iso"
            log = tmp / "serial.log"
            env = dict(os.environ, STAGE23_QEMU_CPUS="4",
                       STAGE23_QEMU_CORES="4", STAGE23_QEMU_THREADS="1")
            for script, args in (
                ("12", [str(log)]),
                ("12", [str(iso), str(log)]),
                ("23", [str(iso), str(log)]),
            ):
                for content, status in (("", 2), ("panic: regression sentinel\n", 1)):
                    with self.subTest(script=script, args=args, content=content):
                        log.write_text(content)
                        result = subprocess.run(
                            ["bash", f"tools/smoke-stage{script}-qemu.sh",
                             "--check-log", *args], cwd=ROOT, env=env,
                            capture_output=True, text=True, timeout=10)
                        self.assertEqual(result.returncode, status, result.stderr)
                        self.assertNotIn("serial log not found", result.stderr)
                        if content:
                            self.assertIn("kernel failure or SMP warning: panic: regression sentinel",
                                          result.stderr)


if __name__ == "__main__":
    unittest.main()
