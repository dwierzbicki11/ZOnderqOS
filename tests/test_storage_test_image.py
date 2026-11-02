"""Inspect generated on-disk structures independently; no Cosmos/QEMU claim."""
import importlib.util
import json
import struct
import tempfile
import unittest
from pathlib import Path

spec = importlib.util.spec_from_file_location('fixture', Path(__file__).resolve().parents[1] / 'tools/create-storage-test-image.py')
fixture = importlib.util.module_from_spec(spec)
spec.loader.exec_module(fixture)


def u32(buf, offset):
    return struct.unpack_from('<I', buf, offset)[0]


class StorageImageTests(unittest.TestCase):
    def inspect(self, scenario):
        with tempfile.TemporaryDirectory() as temp:
            folder = Path(temp) / 'new'
            image = fixture.build(folder, scenario)
            self.assertEqual(image.stat().st_size, 64 * 1024 * 1024)
            with image.open('rb') as stream:
                def read(lba, count=1):
                    stream.seek(lba * 512)
                    return stream.read(count * 512)
                mbr = read(0)
                self.assertEqual(mbr[510:512], b'\x55\xaa')
                self.assertEqual(mbr[450], 0xEF if scenario == 'efi' else 0x0C)
                start, total = struct.unpack_from('<II', mbr, 454)
                self.assertEqual(start + total, image.stat().st_size // 512)
                self.assertEqual(mbr[462:510], bytes(48))  # no other partitions
                boot = read(start)
                self.assertEqual(boot, read(start + 6))
                self.assertEqual(boot[510:512], b'\x55\xaa')
                self.assertEqual(boot[71:82], b'ZONDERQDATA')
                self.assertEqual(struct.unpack_from('<H', boot, 11)[0], 512)
                self.assertEqual(u32(boot, 28), start)
                self.assertEqual(u32(boot, 32), total)
                reserved = struct.unpack_from('<H', boot, 14)[0]
                fats, spc, fat_size = boot[16], boot[13], u32(boot, 36)
                data = reserved + fats * fat_size
                clusters = (total - data) // spc
                self.assertGreaterEqual(clusters, 65525)
                fat = read(start + reserved, fat_size)
                self.assertEqual(fat, read(start + reserved + fat_size, fat_size))
                entries = [u32(fat, i * 4) & 0x0FFFFFFF for i in range(clusters + 2)]
                self.assertEqual(entries[:2], [0x0FFFFFF8, 0x0FFFFFFF])
                free = sum(v == 0 for v in entries[2:])
                self.assertEqual(free, 0 if scenario == 'full' else clusters - 3)
                info = read(start + 1)
                self.assertEqual(info, read(start + 7))
                self.assertEqual((u32(info, 0), u32(info, 484), u32(info, 508)), (0x41615252, 0x61417272, 0xAA550000))
                self.assertEqual(u32(info, 488), free)
                root = read(start + data + (u32(boot, 44) - 2) * spc)
                self.assertEqual(root[:12], b'ZONDERQDATA\x08')
                visited = {2}
                for offset, expected_name in [(32, b'PAYLOAD BIN')] + ([(64, b'FILLER  BIN')] if scenario == 'full' else []):
                    self.assertEqual(root[offset:offset + 11], expected_name)
                    cluster = (struct.unpack_from('<H', root, offset + 20)[0] << 16) | struct.unpack_from('<H', root, offset + 26)[0]
                    size = u32(root, offset + 28)
                    chain = []
                    while cluster < 0x0FFFFFF8:
                        self.assertTrue(2 <= cluster < len(entries))
                        self.assertNotIn(cluster, visited)
                        visited.add(cluster)
                        chain.append(cluster)
                        cluster = entries[cluster]
                    self.assertEqual(len(chain), (size + 511) // 512)
                    if offset == 32:
                        payload = b''.join(read(start + data + c - 2) for c in chain)[:size]
                        self.assertEqual(payload, bytes(range(256)) * 2 + b'ZonderqOS binary storage fixture\x00\xff')
                self.assertEqual(len(visited), clusters - free)  # no lost allocated clusters
                manifest = json.loads((folder / 'manifest.json').read_text())
                self.assertEqual(manifest['free_clusters'], free)
                self.assertEqual(manifest['allocated_clusters'], len(visited))

    def test_normal(self): self.inspect('normal')
    def test_full(self): self.inspect('full')
    def test_efi(self): self.inspect('efi')

    def test_existing_directory_untouched(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp)
            marker = path / 'storage.raw'
            marker.write_bytes(b'keep')
            with self.assertRaises(FileExistsError): fixture.build(path)
            self.assertEqual(marker.read_bytes(), b'keep')

    def test_existing_file_untouched(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'existing'
            path.write_bytes(b'keep')
            with self.assertRaises(FileExistsError): fixture.build(path)
            self.assertEqual(path.read_bytes(), b'keep')

    def test_symlink_untouched(self):
        with tempfile.TemporaryDirectory() as temp:
            target = Path(temp) / 'target'
            target.mkdir()
            link = Path(temp) / 'link'
            link.symlink_to(target, target_is_directory=True)
            with self.assertRaises(FileExistsError): fixture.build(link)
            self.assertEqual(list(target.iterdir()), [])

    def test_bad_scenario_creates_nothing(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / 'new'
            with self.assertRaises(ValueError): fixture.build(path, 'invalid')
            self.assertFalse(path.exists())

if __name__ == '__main__': unittest.main()
