#!/usr/bin/env python3
"""Offline verification of the automatic Cosmos 3.0.94 SanDisk updater."""
import importlib.util
import hashlib
import io
import json
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

    def test_build_in_progress_has_explicit_state(self):
        class InProgress(GithubFake):
            def json(self, url):
                if "/workflows/" in url:
                    return {"workflow_runs": [{"id": 39740062588,
                        "status": "in_progress", "conclusion": None,
                        "head_branch": "main", "head_sha": MAIN_SHA,
                        "event": "push"}]}
                return super().json(url)

        with self.assertRaises(mod.BuildPending) as context:
            mod.select_artifact(InProgress())
        self.assertIn("39740062588", str(context.exception))

    def test_wait_for_main_build_then_select_exact_sha(self):
        done = (321, {"id": 777, "name": mod.ARTIFACT}, MAIN_SHA)
        now = [0]
        sleeps = []

        def pause(seconds):
            sleeps.append(seconds)
            now[0] += seconds

        with mock.patch.object(mod, "select_artifact",
                side_effect=[mod.BuildPending("Build queued"),
                             mod.BuildPending("Build running"), done]):
            self.assertEqual(
                mod.wait_for_artifact(object(), wait_minutes=10,
                                      pause=pause, clock=lambda: now[0]), done)
        self.assertEqual(sleeps, [90, 90])

    def test_wait_timeout_never_invokes_writer(self):
        with mock.patch.object(mod, "select_artifact",
                               side_effect=mod.BuildPending("Build running")), \\
             mock.patch.object(mod, "updater_call") as writer:
            with self.assertRaises(mod.Refused):
                mod.wait_for_artifact(object(), wait_minutes=0, pause=lambda _: None,
                                      clock=lambda: 1)
            writer.assert_not_called()

    def test_new_main_sha_blocks_using_old_iso(self):
        class ChangedMain:
            def json(self, url):
                return {"commit": {"sha": "b" * 40}}
        with self.assertRaises(mod.Refused):
            mod.assert_current_main(ChangedMain(), MAIN_SHA)
        mod.assert_current_main(GithubFake(), MAIN_SHA)

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

    def test_lsblk_json_explicit_tree_and_only_own_mounts(self):
        # On Kali/util-linux JSON without NAME or --tree may return
        # sdb1..sdb4 as additional roots instead of sdb.children.
        tree = {
            "blockdevices": [{
                "name": "sdb", "path": "/dev/sdb",
                "type": "disk", "mountpoints": [None],
                "children": [
                    {"name": "sdb1", "path": "/dev/sdb1", "type": "part", "mountpoints": [None]},
                    {"name": "sdb2", "path": "/dev/sdb2", "type": "part", "mountpoints": [None]},
                    {"name": "sdb3", "path": "/dev/sdb3", "type": "part", "mountpoints": [None]},
                    {"name": "sdb4", "path": "/dev/sdb4", "type": "part",
                     "mountpoints": ["/media/zonderq/ZONDERQDATA"]},
                ],
            }]
        }
        with mock.patch.object(mod.Path, "exists", return_value=True), \
             mock.patch.object(mod, "cmd", side_effect=[
                 "disk", "usb", "SanDisk Ultra", str(mod.EXPECTED_BYTES),
                 mod.DATA_START, "vfat", "ZONDERQDATA", json.dumps(tree),
             ]) as call:
            self.assertEqual(mod.check_usb(), ["/dev/sdb4"])
            self.assertEqual(call.call_args_list[-1].args, (
                "lsblk", "--json", "--tree", "-o",
                "NAME,PATH,TYPE,MOUNTPOINTS", "/dev/sdb"
            ))

    def test_lsblk_flat_tree_refused(self):
        flat = {"blockdevices": [
            {"name": "sdb", "path": "/dev/sdb", "type": "disk", "mountpoints": [None]},
            {"name": "sdb4", "path": "/dev/sdb4", "type": "part", "mountpoints": [None]},
        ]}
        with mock.patch.object(mod.Path, "exists", return_value=True), \
             mock.patch.object(mod, "cmd", side_effect=[
                 "disk", "usb", "SanDisk Ultra", str(mod.EXPECTED_BYTES),
                 mod.DATA_START, "vfat", "ZONDERQDATA", json.dumps(flat),
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
