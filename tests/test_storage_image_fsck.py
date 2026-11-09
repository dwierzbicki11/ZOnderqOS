"""Optional independent dosfstools validation; never repairs or opens host disks."""
import hashlib
import importlib.util
import os
from pathlib import Path
import shutil
import struct
import subprocess
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('storage_fixture', Path(__file__).resolve().parents[1] / 'tools/create-storage-test-image.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
FSCK = os.environ.get('FSCK_FAT') or shutil.which('fsck.fat')


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


@unittest.skipUnless(FSCK, 'fsck.fat unavailable; independent FAT validation not executed')
class IndependentFatValidation(unittest.TestCase):
    def check_image(self, scenario, corrupt=None):
        with tempfile.TemporaryDirectory(prefix='zonderq-fsck-') as temp:
            root = Path(temp)
            image = fixture.build(root / 'fixture', scenario)
            original_hash = digest(image)
            # fsck.fat takes a volume, not an MBR disk. Extract only our freshly
            # generated partition into another exclusive disposable regular file.
            volume = root / 'partition.fat'
            with image.open('rb') as source, volume.open('xb') as target:
                mbr = source.read(512)
                start, sectors = struct.unpack_from('<II', mbr, 454)
                self.assertEqual((start + sectors) * 512, image.stat().st_size)
                source.seek(start * 512)
                shutil.copyfileobj(source, target, 65536)
            if corrupt == 'root-free':
                with volume.open('r+b') as target:
                    for copy in range(2):
                        target.seek((fixture.RESERVED + copy * fixture.FAT_SECTORS) * 512 + 8)
                        target.write(bytes(4))
            elif corrupt == 'lost-cluster':
                with volume.open('r+b') as target:
                    for copy in range(2):
                        target.seek((fixture.RESERVED + copy * fixture.FAT_SECTORS) * 512 + 5 * 4)
                        target.write(struct.pack('<I', 0x0FFFFFFF))
            before = digest(volume)
            result = subprocess.run([FSCK, '-n', str(volume)], capture_output=True,
                                    text=True, timeout=30, stdin=subprocess.DEVNULL)
            self.assertEqual(digest(volume), before, 'read-only fsck modified the volume')
            self.assertEqual(digest(image), original_hash, 'source image changed')
            output = result.stdout + result.stderr
            if corrupt:
                # dosfstools status 1 means recoverable filesystem errors found;
                # an invocation/IO failure must not masquerade as detection.
                self.assertEqual(result.returncode, 1, output)
            else:
                self.assertEqual(result.returncode, 0, output)
                allocated = fixture.CLUSTERS if scenario == 'full' else 3
                self.assertIn(f'{allocated}/{fixture.CLUSTERS} clusters', output)

    def test_normal_volume(self): self.check_image('normal')
    def test_full_volume(self): self.check_image('full')
    def test_efi_volume_is_valid_fat(self): self.check_image('efi')
    def test_free_root_detected_without_repair(self): self.check_image('normal', 'root-free')
    def test_orphan_detected_without_repair(self): self.check_image('normal', 'lost-cluster')


if __name__ == '__main__': unittest.main()
