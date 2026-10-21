#!/usr/bin/env python3
"""Pure unit checks for the one-USB, FAT32-preserving MBR update guard."""
import importlib.util
from pathlib import Path
import struct
import unittest

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location(
    "update_3094_usb", ROOT / "tools/update-3094-one-usb.py")
update = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(update)

SECTOR = 512
SIZE = update.EXPECTED_DISK_BYTES
ISO_SIZE = 15853568


def mbr(boot=b"B", data_type=0x0C, data_start=65536, p3_count=25000):
    sector = bytearray(512)
    sector[:440] = boot * 440
    sector[440:446] = b"ABCDEF"
    table = [
        (7, 64, 76), (0xEF, 140, 5760), (7, 5900, p3_count),
        (data_type, data_start, SIZE // 512 - data_start),
    ]
    for i, (kind, start, count) in enumerate(table):
        offset = 446 + i * 16
        sector[offset + 4] = kind
        struct.pack_into("<II", sector, offset + 8, start, count)
    sector[510:] = b"\x55\xaa"
    return bytes(sector)


class SafeUsbUpdateTests(unittest.TestCase):
    def test_composed_mbr_uses_new_limine_and_preserves_data_entry(self):
        old = mbr(boot=b"O")
        new = bytearray(mbr(boot=b"N", p3_count=25100))
        new[440:446] = b"123456"
        new[494:510] = b"\0" * 16
        result = update.check_partition_table(old, bytes(new), ISO_SIZE, SIZE)
        self.assertEqual(result[:440], b"N" * 440)
        self.assertEqual(result[440:446], b"ABCDEF")
        self.assertEqual(result[446:494], bytes(new)[446:494])
        self.assertEqual(result[494:510], old[494:510])
        self.assertEqual(result[510:], b"\x55\xaa")

    def test_iso_must_end_before_data_partition(self):
        old, new = mbr(), bytearray(mbr())
        new[494:510] = b"\0" * 16
        with self.assertRaisesRegex(RuntimeError, "overlaps the ZONDERQDATA"):
            update.check_partition_table(old, bytes(new), 65537 * 512, SIZE)

    def test_reject_data_partition_type_or_start_change(self):
        new = bytearray(mbr())
        new[494:510] = b"\0" * 16
        for old in (mbr(data_type=0xEF), mbr(data_start=32768)):
            with self.assertRaisesRegex(RuntimeError, "Unexpected USB FAT32 partition"):
                update.check_partition_table(old, bytes(new), ISO_SIZE, SIZE)

    def test_reject_iso_partition_overlap(self):
        new = bytearray(mbr(p3_count=64000))
        new[494:510] = b"\0" * 16
        with self.assertRaisesRegex(RuntimeError, "overlaps FAT32"):
            update.check_partition_table(mbr(), bytes(new), ISO_SIZE, SIZE)

    def test_reject_unexpected_iso_fourth_partition(self):
        with self.assertRaisesRegex(RuntimeError, "fourth MBR"):
            update.check_partition_table(mbr(), mbr(), ISO_SIZE, SIZE)

    def test_reject_boot_signature_corruption(self):
        new = bytearray(mbr())
        new[494:510] = b"\0" * 16
        new[511] = 0
        with self.assertRaisesRegex(RuntimeError, "DOS MBR magic"):
            update.check_partition_table(mbr(), bytes(new), ISO_SIZE, SIZE)


if __name__ == "__main__":
    unittest.main(verbosity=2)
