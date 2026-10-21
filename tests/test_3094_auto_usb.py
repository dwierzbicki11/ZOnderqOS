#!/usr/bin/env python3
"""Offline verification of the automatic Cosmos 3.0.94 SanDisk updater."""
import importlib.util
import hashlib
import io
from pathlib import Path
import tempfile
import unittest
from unittest import mock
import zipfile

ROOT = Path(__file__).resolve().parents[1] / "tools"
spec = importlib.util.spec_from_file_location("zonderq_auto_usb", ROOT / "auto-update-3094-usb.py")
mod = importlib.util.module_from_spec(spec)
spec.loader.exec_module(mod)
MAIN_SHA = "e2f39c986bda6d2aca99f7ce96cd421edf0271ec"


def iso_bytes():
    image = bytearray(128 * 512)
    image[510:512] = b"\x55\xaa"
    return bytes(image)


def zip_bytes(files):
    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w", zipfile.ZIP_DEFLATED) as zipf:
        for name, data in files.items():
            zipf.writestr(name, data)
    return buf.getvalue()


class GithubFake:
    def __init__(self, sha=MAIN_SHA, run_sha=MAIN_SHA, event="push", conclusion="success"):
        self.sha = sha
        self.run_sha = run_sha
        self.event = event
        self.conclusion = conclusion

    def json(self, url):
        if url.endswith("branches/main"):
            return {"commit": {"sha": self.sha}}
        if "/workflows/" in url:
            return {"workflow_runs": [{"id": 321, "status": "completed",
                                      "conclusion": self.conclusion, "head_branch": "main",
                                      "head_sha": self.run_sha, "event": self.event}]}
        if "/runs/321/artifacts" in url:
            return {"artifacts": [{"id": 777, "name": mod.ARTIFACT, "expired": False,
                                   "workflow_run": {"head_sha": self.run_sha}}]}
        raise AssertionError(url)


class AutomatedUsbTests(unittest.TestCase):
    def test_chooses_only_current_successful_main(self):
        rid, a, head = mod.select_artifact(GithubFake())
        self.assertEqual((rid, a["id"], head), (321, 777, MAIN_SHA))
        for cfg in ({"run_sha": "a" * 40}, {"event": "pull_request"},
                    {"conclusion": "failure"}):
            with self.assertRaises(mod.Refused):
                mod.select_artifact(GithubFake(**cfg))

    def test_iso_validity_and_reserved_fourth_partition(self):
        with tempfile.TemporaryDirectory() as temp:
            p = Path(temp) / "ZonderqOS.iso"
            image = iso_bytes()
            p.write_bytes(image)
            mod.verify_iso(p)
            broken = bytearray(image)
            broken[506:510] = (400).to_bytes(4, "little")
            p.write_bytes(broken)
            with self.assertRaises(mod.Refused):
                mod.verify_iso(p)
            p.write_bytes(image[:-1])
            with self.assertRaises(mod.Refused):
                mod.verify_iso(p)

    def test_artifact_zip_digest_good_and_bad(self):
        with tempfile.TemporaryDirectory() as temp:
            p = Path(temp) / "archive.zip"
            data = zip_bytes({"ZonderqOS.iso": iso_bytes()})
            p.write_bytes(data)
            mod.verify_zip_digest(p, {"digest": "sha256:" + hashlib.sha256(data).hexdigest()})
            with self.assertRaises(mod.Refused):
                mod.verify_zip_digest(p, {"digest": "sha256:" + "a" * 64})

    def test_download_safely_extracts_exact_filename(self):
        class Downloader:
            def __init__(self, data):
                self.data = data
            def request(self, url):
                if url != mod.API + "/actions/artifacts/777/zip":
                    raise AssertionError(url)
                return io.BytesIO(self.data)
        with tempfile.TemporaryDirectory() as temp:
            data = zip_bytes({"ZonderqOS.iso": iso_bytes()})
            artifact = {"id": 777, "digest": "sha256:" + hashlib.sha256(data).hexdigest()}
            result = mod.download_and_extract(Downloader(data), artifact, MAIN_SHA, Path(temp))
            self.assertEqual(result.read_bytes(), iso_bytes())
            data = zip_bytes({"../../evil.iso": iso_bytes()})
            with self.assertRaises(mod.Refused):
                mod.download_and_extract(Downloader(data), {"id": 777}, MAIN_SHA, Path(temp))

    def test_unsafe_device_and_mount_refused_before_write(self):
        with self.assertRaises(mod.Refused):
            mod.check_usb("/dev/nvme0n1")
        with mock.patch.object(mod.Path, "exists", return_value=True), \
             mock.patch.object(mod, "cmd", side_effect=[
                 "disk", "usb", "SanDisk Ultra", str(mod.EXPECTED_BYTES),
                 mod.DATA_START, "vfat", "ZONDERQDATA",
                 '{"blockdevices":[{"path":"/dev/sdb","mountpoints":[],"children":'
                 '[{"path":"/dev/sdb4","type":"part","mountpoints":["/"]}]}]}'
             ]):
            with self.assertRaises(mod.Refused):
                mod.check_usb()

    def test_writer_remains_interactive_no_raw_device_commands(self):
        source = (ROOT / "auto-update-3094-usb.py").read_text()
        original_writer = (ROOT / "update-3094-one-usb.py").read_text()
        self.assertIn("updater_call(iso, apply=False)", source)
        self.assertIn("updater_call(iso, apply=True)", source)
        self.assertIn('["sudo", "umount", mount_device]', source)
        self.assertIn('EXPECTED_DEVICE = "/dev/sdb"', source)
        self.assertIn('No dd', source)
        self.assertNotIn('open(device, "r+b"', source)
        self.assertIn("check_partition_table(disk_mbr, new_mbr, iso_size, capacity)", original_writer)
        self.assertIn("Saved original first 32 MiB", original_writer)


if __name__ == "__main__":
    unittest.main(verbosity=2)
