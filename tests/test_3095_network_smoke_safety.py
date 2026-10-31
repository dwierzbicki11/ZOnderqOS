#!/usr/bin/env python3
"""Host-only negative tests: QEMU smoke must not overwrite prior evidence.

Uses a fake qemu executable. No NIC, guest boot, or real QEMU success is
claimed by these tests.
"""
from __future__ import annotations

import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SMOKE = Path(__file__).resolve().parents[1] / "tools/smoke-network-n1-3095-qemu.sh"


class EvidenceSafety(unittest.TestCase):
    def test_existing_symlinked_and_aliased_paths_fail_closed(self):
        cases = (
            "serial", "stderr", "tcp_host", "http_host", "pcap",
            "serial_symlink", "pcap_symlink", "alias_stderr",
            "alias_http_host", "alias_serial",
        )
        for case in cases:
            with self.subTest(case=case), tempfile.TemporaryDirectory() as root:
                directory = Path(root)
                fake_bin = directory / "bin"
                fake_bin.mkdir()
                qemu_called = directory / "qemu-called"
                fake_qemu = fake_bin / "qemu-system-x86_64"
                fake_qemu.write_text(
                    "#!/bin/sh\n" + f"touch '{qemu_called}'\n" + "exit 99\n"
                )
                fake_qemu.chmod(0o755)
                iso = directory / "test.iso"
                iso.write_bytes(b"isolated dummy ISO")
                log = directory / "probe.serial.log"
                pcap = directory / "probe.pcap"
                protected = directory / "protected"
                protected.write_text("KEEP")
                targets = {
                    "serial": log,
                    "stderr": Path(str(log) + ".stderr"),
                    "tcp_host": Path(str(log) + ".tcp-host"),
                    "http_host": Path(str(log) + ".http-host"),
                    "pcap": pcap,
                }
                if case in targets:
                    targets[case].write_text("KEEP")
                elif case == "serial_symlink":
                    log.symlink_to(protected)
                elif case == "pcap_symlink":
                    pcap.symlink_to(directory / "missing")
                elif case == "alias_stderr":
                    pcap = targets["stderr"]
                elif case == "alias_http_host":
                    pcap = targets["http_host"]
                elif case == "alias_serial":
                    pcap = log
                else:
                    raise AssertionError("unknown case")
                env = dict(os.environ)
                env["PATH"] = str(fake_bin) + os.pathsep + env.get("PATH", "")
                env["NET3095_N1_PCAP"] = str(pcap)
                run = subprocess.run(
                    ["bash", str(SMOKE), str(iso), str(log), "n1"],
                    cwd=directory, env=env, capture_output=True, text=True,
                    timeout=10, check=False,
                )
                self.assertNotEqual(run.returncode, 0)
                self.assertIn(
                    "Refusing to overwrite existing evidence"
                    if not case.startswith("alias_") else "Evidence paths alias",
                    run.stderr,
                )
                self.assertEqual(protected.read_text(), "KEEP")
                if case in targets:
                    self.assertEqual(targets[case].read_text(), "KEEP")
                self.assertFalse(qemu_called.exists(), "fake QEMU was invoked")


if __name__ == "__main__":
    unittest.main(verbosity=2)
