"""Run actual QEMU wrapper preflight with fake devices/tools and disposable files."""
import os
from pathlib import Path
import subprocess
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[1] / 'tools/smoke-services-qemu.sh'

class SmokeImageCreation(unittest.TestCase):
    def exercise(self, kind):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            fake = root / 'bin'
            fake.mkdir()
            for name in ('qemu-system-x86_64', 'mkfs.vfat', 'sfdisk', 'mtype', 'mcopy'):
                tool = fake / name
                tool.write_text('#!/bin/sh\necho FAKE_PARTITION_STOP >&2\nexit 97\n')
                tool.chmod(0o755)
            iso = root / 'boot.iso'
            iso.write_bytes(b'test')
            out = root / 'out'
            out.mkdir()
            disk = out / 'services-fat.img'
            target = root / 'must-not-change'
            if kind == 'file': disk.write_bytes(b'keep')
            elif kind == 'directory': disk.mkdir()
            elif kind == 'dangling': disk.symlink_to(target)
            elif kind == 'symlink':
                target.write_bytes(b'keep')
                disk.symlink_to(target)
            env = dict(os.environ, PATH=str(fake) + ':' + os.environ['PATH'])
            result = subprocess.run(['bash', str(SCRIPT), str(iso), str(out)],
                                    env=env, capture_output=True, text=True, timeout=15)
            self.assertNotEqual(result.returncode, 0)
            if kind == 'new':
                self.assertEqual(result.returncode, 97, result.stderr)
                self.assertIn('FAKE_PARTITION_STOP', result.stderr)
                self.assertEqual(disk.stat().st_size, 256 * 1024 * 1024)
            else:
                self.assertNotIn('FAKE_PARTITION_STOP', result.stderr)
                if kind == 'file': self.assertEqual(disk.read_bytes(), b'keep')
                elif kind == 'directory': self.assertEqual(list(disk.iterdir()), [])
                elif kind == 'dangling':
                    self.assertTrue(disk.is_symlink())
                    self.assertFalse(target.exists(), 'dangling symlink target was created')
                else:
                    self.assertTrue(disk.is_symlink())
                    self.assertEqual(target.read_bytes(), b'keep')

    def test_dangling_symlink(self): self.exercise('dangling')
    def test_existing_symlink(self): self.exercise('symlink')
    def test_existing_file(self): self.exercise('file')
    def test_existing_directory(self): self.exercise('directory')
    def test_new_image(self): self.exercise('new')

if __name__ == '__main__': unittest.main()
