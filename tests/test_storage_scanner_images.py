"""Run the production C# scanner on fresh file-backed FAT fixtures, never devices."""
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import struct
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('storage_fixture', ROOT / 'tools/create-storage-test-image.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)
DOTNET = os.environ.get('DOTNET', 'dotnet')
PROJECT = ROOT / 'tests/Fat32FileImageSmoke/Fat32FileImageSmoke.csproj'
DLL = PROJECT.parent / 'bin/Release/net10.0/Fat32FileImageSmoke.dll'


def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()


class ScannerImages(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        # Missing .NET is a failure, not a skipped green integration test.
        subprocess.run([DOTNET, 'build', str(PROJECT), '-c', 'Release'],
                       check=True, timeout=120, cwd=ROOT)

    def check_image(self, scenario='normal', mutation=None, mode='valid'):
        with tempfile.TemporaryDirectory(prefix='zonderq-scanner-') as temp:
            directory = Path(temp) / 'fixture'
            image = fixture.build(directory, scenario)
            manifest = json.loads((directory / 'manifest.json').read_text())
            # All writes below affect only the image just created in this test.
            if mutation:
                with image.open('r+b') as stream:
                    mutation(stream)
            before = digest(image)
            result = subprocess.run([DOTNET, str(DLL), str(image),
                                     str(manifest['partition_start']),
                                     str(manifest['partition_sectors']),
                                     str(manifest['data_clusters']),
                                     str(manifest['free_clusters']),
                                     str(manifest['allocated_clusters']), mode],
                                    capture_output=True, text=True, timeout=30, cwd=ROOT)
            self.assertEqual(digest(image), before, 'scanner changed image bytes')
            self.assertEqual(result.returncode, 0, result.stdout + result.stderr)
            self.assertIn('PASS', result.stdout)

    def test_normal_image(self): self.check_image()
    def test_full_image(self): self.check_image('full')
    def test_closed_device_after_progress(self): self.check_image(mode='disconnect')

    def test_truncated_boot(self):
        self.check_image(mutation=lambda f: f.truncate(fixture.START * 512 + 511), mode='invalid')

    def test_truncated_mirror_after_progress(self):
        self.check_image(mutation=lambda f: f.truncate(
            (fixture.START + fixture.RESERVED + fixture.FAT_SECTORS + 8) * 512 + 511), mode='invalid')

    def test_mirror_disagreement(self):
        def corrupt(f):
            f.seek((fixture.START + fixture.RESERVED + fixture.FAT_SECTORS) * 512 + 5 * 4)
            f.write(struct.pack('<I', 0x0FFFFFFF))
        self.check_image(mutation=corrupt, mode='invalid')

    def test_free_root(self):
        def corrupt(f):
            for copy in range(2):
                f.seek((fixture.START + fixture.RESERVED + copy * fixture.FAT_SECTORS) * 512 + 8)
                f.write(bytes(4))
        self.check_image(mutation=corrupt, mode='invalid')

    def test_stale_fsinfo_does_not_override_fat(self):
        def corrupt(f):
            for sector in (1, 7):
                f.seek((fixture.START + sector) * 512 + 488)
                f.write(bytes(4))
        self.check_image(mutation=corrupt)


if __name__ == '__main__': unittest.main()
