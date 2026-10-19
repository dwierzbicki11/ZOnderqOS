"""Host-only regression tests for the Stage-23 serial validator, not QEMU boots.

Run with: python3 -m unittest discover -s tests -p 'test_stage23_log.py'
"""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[1] / "tools/smoke-stage23-qemu.sh"


class Stage23LogTests(unittest.TestCase):
    def validate(self, cpus, htop_lines):
        # Run the actual embedded validator independently of the Stage 0-22
        # boot gates. Fixtures below only represent Stage-23 serial evidence.
        validator = SCRIPT.read_text().split("<<'PY'\n", 1)[1].rsplit("\nPY", 1)[0]
        mask = (1 << cpus) - 1
        lines = [
            "[SCHED-PRODUCTION-SMP] begin",
            f"[SCHED-PRODUCTION-SMP] workers={cpus} unique-cpus={cpus} "
            f"cpu-mask=0x{mask:X} registry=restored aps=parked",
            "[SCHED-PRODUCTION-SMP-TEST] PASS",
            *htop_lines,
        ]
        with tempfile.TemporaryDirectory() as directory:
            log = Path(directory) / "synthetic-serial.log"
            log.write_text("\n".join(lines) + "\n")
            return subprocess.run(
                [sys.executable, "-", str(log), str(cpus)],
                input=validator, text=True, capture_output=True, check=False,
            )

    def test_htop_marker_order(self):
        for cpus in (1, 2, 4, 8, 16):
            begin = "[HTOP-TEST] BEGIN"
            passed = (
                f"[HTOP-TEST] PASS cpus={cpus} "
                f"mask=0x{(1 << cpus) - 1:X} processes=restored"
            )
            with self.subTest(cpus=cpus, order="begin-pass"):
                result = self.validate(cpus, [begin, passed])
                self.assertEqual(result.returncode, 0, result.stderr)
            with self.subTest(cpus=cpus, order="pass-begin"):
                result = self.validate(cpus, [passed, begin])
                self.assertEqual(result.returncode, 1, result.stderr)
                self.assertIn("htop runtime verification markers are out of order", result.stderr)


if __name__ == "__main__":
    unittest.main()
