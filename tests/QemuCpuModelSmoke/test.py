"""Exercise the real Stage-23 preflight with a fake QEMU (no boot claim)."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest


ROOT = Path(__file__).resolve().parents[2]


class CpuModelPreflight(unittest.TestCase):
    def run_case(self, listing, accepted, model="Nehalem", help_status=0):
        with tempfile.TemporaryDirectory(dir=ROOT) as directory:
            tmp = Path(directory)
            qemu = tmp / "qemu-system-x86_64"
            qemu.write_text(
                '#!/bin/bash\n'
                'if [[ "$*" == "-cpu help" ]]; then\n'
                '  printf "%s\\n" "$TEST_CPU_LIST"\n'
                '  exit "$TEST_HELP_STATUS"\n'
                'fi\n'
                'printf "%s\\n" "$@" > "$TEST_LAUNCH"\n'
                'exit 1\n'
            )
            qemu.chmod(0o755)
            iso = tmp / "test.iso"
            iso.touch()
            launch = tmp / "launch"
            env = dict(os.environ, PATH=f"{tmp}:{os.environ['PATH']}",
                       TEST_CPU_LIST=listing, TEST_HELP_STATUS=str(help_status),
                       TEST_LAUNCH=str(launch), STAGE23_QEMU_CPU_MODEL=model,
                       STAGE23_QEMU_CPUS="8", STAGE23_QEMU_SOCKETS="1",
                       STAGE23_QEMU_CORES="8", STAGE23_QEMU_THREADS="1",
                       STAGE23_QEMU_TIMEOUT_SECONDS="210",
                       STAGE23_QEMU_STABLE_SECONDS="60")
            result = subprocess.run(
                ["bash", "tools/smoke-stage23-qemu.sh", str(iso), str(tmp / "serial")],
                cwd=ROOT, env=env, capture_output=True, text=True, timeout=10)
            self.assertNotEqual(result.returncode, 0)  # Fake QEMU never boots.
            self.assertEqual(launch.exists(), accepted, result.stdout + result.stderr)
            if accepted:
                args = launch.read_text().splitlines()
                self.assertEqual(args[args.index("-cpu") + 1], model)
                self.assertEqual(args[args.index("-smp") + 1],
                                 "cpus=8,sockets=1,cores=8,threads=1")
                self.assertNotIn("is not available", result.stderr)
            else:
                self.assertIn("cannot list" if help_status else "is not available",
                              result.stderr)

    def test_legacy_format(self):
        self.run_case("Available CPUs:\nx86 Nehalem Intel Core i7\n", True)

    def test_unprefixed_format(self):
        self.run_case("Available CPUs:\nNehalem (alias configured by machine type)\n"
                      "Nehalem-v1 Intel Core i7\n", True)

    def test_versioned_exact_match(self):
        self.run_case("Nehalem-v1 Intel Core i7\n", True, model="Nehalem-v1")

    def test_no_partial_or_description_matches(self):
        for listing in ("Nehalem-v1 Intel Core i7", "Other Nehalem description",
                        "x86 Other Nehalem", "", "NehalemExtra description"):
            with self.subTest(listing=listing):
                self.run_case(listing, False)

    def test_help_failure(self):
        self.run_case("Nehalem description", False, help_status=1)


if __name__ == "__main__":
    unittest.main()
