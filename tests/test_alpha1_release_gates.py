"""Host-only release-gate regression tests. No QEMU or Cosmos runtime required.

These tests verify reporting and timing logic. They DO NOT replace real E2E.
"""
import subprocess
import sys
import tempfile
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOAK = ROOT / "tools/qemu-ram-soak-60min.sh"
HARNESS = ROOT / "tools/alpha1_e2e_harness.py"
MEASURER = ROOT / "tools/measure_scenarios.py"


def embedded_soak_report():
    source = SOAK.read_text(encoding="utf-8")
    marker = 'python3 - "$OUT_DIR" "$BRANCH_NAME" "$COMMIT_SHA" "$ISO_SHA256" <<\'PY\'\n'
    if source.count(marker) != 1:
        raise AssertionError("Embedded soak report marker changed")
    return source.split(marker, 1)[1].split("\nPY\n", 1)[0]


def run_report(sample_seconds):
    with tempfile.TemporaryDirectory(prefix="zq-soak-contract-") as temp:
        folder = Path(temp)
        lines = [
            f"[MEM_SAMPLE] sec={second} used_mb={158.89 + i * 0.02:.2f} "
            f"used_p=40675 p_gc=38800 gc_comm_kb=155203 "
            f"gc_heap_kb=150240 threads=5"
            for i, second in enumerate(sample_seconds)
        ]
        (folder / "serial.log").write_text("\n".join(lines) + "\n", encoding="utf-8")
        result = subprocess.run(
            [sys.executable, "-c", embedded_soak_report(),
             str(folder), "test", "test-commit", "test-iso-sha"],
            cwd=ROOT, capture_output=True, text=True,
        )
        report = (folder / "report.md")
        return result, report.read_text(encoding="utf-8") if report.exists() else ""


class Alpha1ReleaseGateContractTests(unittest.TestCase):
    def test_short_3480s_soak_cannot_pass(self):
        result, report = run_report([32, 636, 1230, 1834, 3512])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Niepełne 60 min", result.stderr)
        self.assertNotIn("WERDYKT", report)

    def test_full_3600s_soak_passes(self):
        result, report = run_report([32, 632, 1232, 1832, 3632])
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn("span `3600s`", report)
        self.assertIn("**WERDYKT**: PASS", report)
        self.assertIn("| 60 min | 3632 |", report)

    def test_non_monotonic_clock_cannot_pass(self):
        result, _ = run_report([32, 632, 400, 1232, 1832, 3632])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Non-monotonic", result.stderr)

    def test_missing_samples_cannot_pass(self):
        result, _ = run_report([])
        self.assertNotEqual(result.returncode, 0)
        self.assertIn("Brak próbek", result.stderr)

    def test_harness_does_not_fake_fat32_or_efi_pass(self):
        source = HARNESS.read_text(encoding="utf-8")
        self.assertNotIn("qemu_audit_e2e.py", source)
        self.assertIn('"guest_fat32_acl_e2e": "NOT_TESTED"', source)
        self.assertIn('"ovmf_efi_stub": "PASS"', source)
        self.assertIn('"real_uefi_kernel_chain": "NOT_TESTED"', source)
        self.assertIn('if result.get("status") != "PASS"', source)

    def test_measure_harness_has_portable_repo_root(self):
        source = MEASURER.read_text(encoding="utf-8")
        self.assertIn("ROOT = Path(__file__).resolve().parent.parent", source)
        self.assertNotIn('ROOT = Path("/home/', source)


if __name__ == "__main__":
    unittest.main()
