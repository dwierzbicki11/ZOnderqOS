"""Serial argument regressions; these tests do not boot QEMU."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[1]


class CheckLogArgumentTests(unittest.TestCase):
    def check_log(self, stage, content, two_arguments=True, missing=False, iso_exists=True):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            log = root / "captured serial.log"
            if not missing:
                log.write_text(content)
            # Even an existing ISO argument must never override the serial log.
            iso = root / "not-used.iso"
            if iso_exists:
                iso.write_text("panic: wrong input selected\n")
            args = [str(iso), str(log)] if two_arguments else [str(log)]
            env = dict(os.environ)
            for prefix in ("STAGE12", "STAGE23"):
                env.update({f"{prefix}_QEMU_CPUS": "4",
                            f"{prefix}_QEMU_SOCKETS": "1",
                            f"{prefix}_QEMU_CORES": "2",
                            f"{prefix}_QEMU_THREADS": "2"})
            result = subprocess.run(
                ["bash", f"tools/smoke-stage{stage}-qemu.sh", "--check-log", *args],
                cwd=ROOT, env=env, text=True, capture_output=True, timeout=10,
            )
            return result, str(log)

    def test_two_argument_form_reads_serial_through_stage23(self):
        for stage in (12, 23):
            with self.subTest(stage=stage):
                result, _ = self.check_log(stage, "panic: serial sentinel\n")
                self.assertEqual(result.returncode, 1)
                self.assertIn("kernel failure or SMP warning: panic: serial sentinel", result.stderr)

    def test_single_argument_form_remains_supported(self):
        result, _ = self.check_log(12, "panic: serial sentinel\n", two_arguments=False)
        self.assertEqual(result.returncode, 1)
        self.assertIn("kernel failure or SMP warning: panic: serial sentinel", result.stderr)

    def test_unused_iso_does_not_have_to_exist(self):
        for stage in (12, 23):
            with self.subTest(stage=stage):
                result, _ = self.check_log(stage, "panic: serial sentinel\n", iso_exists=False)
                self.assertEqual(result.returncode, 1)
                self.assertIn("kernel failure or SMP warning: panic: serial sentinel", result.stderr)

    def test_missing_log_names_serial_path(self):
        for stage in (12, 23):
            with self.subTest(stage=stage):
                result, log = self.check_log(stage, "", missing=True)
                self.assertEqual(result.returncode, 1)
                self.assertIn(f"serial log not found: {log}", result.stderr)

    def test_empty_log_is_incomplete_not_pass(self):
        for stage in (12, 23):
            with self.subTest(stage=stage):
                result, _ = self.check_log(stage, "")
                self.assertEqual(result.returncode, 2, result.stdout + result.stderr)
                self.assertNotIn("[OK]", result.stdout)


if __name__ == "__main__":
    unittest.main()
