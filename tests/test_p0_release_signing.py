#!/usr/bin/env python3
"""Real OpenSSL Ed25519 smoke tests for offline release tooling."""
from pathlib import Path
import hashlib
import subprocess
import sys
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
SCRIPT = ROOT / "tools/verify-release-bundle.py"


class ReleaseSigningSmoke(unittest.TestCase):
    def test_sign_verify_and_detect_mutation(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            bundle = root / "bundle"
            bundle.mkdir()
            key = root / "release.key"
            public = root / "release.pub"
            subprocess.run(["openssl", "genpkey", "-algorithm", "ED25519",
                            "-out", str(key)], check=True, capture_output=True)
            subprocess.run(["openssl", "pkey", "-in", str(key), "-pubout",
                            "-out", str(public)], check=True, capture_output=True)
            payload = bundle / "payload" / "system" / "bin"
            payload.mkdir(parents=True)
            kernel = b"ELF test fixture"
            (bundle / "ZonderqOS.elf").write_bytes(kernel)
            data = b"release file"
            (payload / "hello.txt").write_bytes(data)
            original = (
                "UPDATE_FORMAT=2\nSIGNATURE_ALG=ED25519\nVERSION=1.0.1\nBUILD=3110\nARCH=x86_64\n"
                "KERNEL_IMAGE=ZonderqOS.elf\nKERNEL_SHA256=" +
                hashlib.sha256(kernel).hexdigest() + "\nPAYLOAD_DIR=payload\n" +
                "FILE=system/bin/hello.txt:" + hashlib.sha256(data).hexdigest() +
                ":" + str(len(data)) + "\nTITLE=Example release\n"
            )
            manifest = bundle / "update.manifest"
            manifest.write_text(original, encoding="utf-8")

            def call(mode):
                option = "--private-key" if mode == "sign" else "--public-key"
                keypath = key if mode == "sign" else public
                return subprocess.run([sys.executable, str(SCRIPT), mode,
                                       "--bundle", str(bundle), option, str(keypath)],
                                      capture_output=True, text=True)

            self.assertEqual(call("sign").returncode, 0, "signing should pass")
            self.assertEqual(call("verify").returncode, 0, "verification should pass")
            manifest.write_text(original.replace("Example release", "Tampered release"))
            self.assertNotEqual(call("verify").returncode, 0, "tampered signed manifest accepted")
            manifest.write_text(original)
            (payload / "hello.txt").write_bytes(b"changed")
            self.assertNotEqual(call("verify").returncode, 0, "tampered file accepted")
            (payload / "hello.txt").write_bytes(data)
            (payload / "hidden.txt").write_bytes(b"extra")
            self.assertNotEqual(call("verify").returncode, 0, "undeclared payload accepted")
            (payload / "hidden.txt").unlink()
            (payload / "hello.txt").unlink()
            (payload / "hello.txt").symlink_to(root / "release.key")
            self.assertNotEqual(call("verify").returncode, 0, "symlink payload accepted")


if __name__ == "__main__":
    unittest.main()
