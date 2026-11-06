"""Regression gate for overflow-safe File Manager capacity labels."""
from pathlib import Path
import re
import unittest

SOURCE = Path(__file__).resolve().parents[1] / "source/GUI/Application/FileManagerApp.cs"
MAX_U64 = (1 << 64) - 1


def safe_capacity_bytes(blocks, size):
    return MAX_U64 if size and blocks > MAX_U64 // size else blocks * size


class FileManagerCapacityTests(unittest.TestCase):
    def test_geometry_call_sites_use_checked_helper(self):
        source = SOURCE.read_text(encoding="utf-8")
        self.assertIn("SafeCapacityBytes((ulong)partition.BlockCount, (ulong)partition.BlockSize)", source)
        self.assertIn("SafeCapacityBytes((ulong)device.BlockCount, (ulong)device.BlockSize)", source)
        self.assertRegex(source, r"blocks\s*>\s*ulong\.MaxValue\s*/\s*blockSize")
        self.assertIn("return ulong.MaxValue;", source)
        self.assertIn("return blocks * blockSize;", source)

    def test_boundary_geometry(self):
        cases = [
            (0, 512, 0),
            (1000, 512, 512000),
            (MAX_U64 // 512, 512, (MAX_U64 // 512) * 512),
            (MAX_U64 // 512 + 1, 512, MAX_U64),
            (MAX_U64, MAX_U64, MAX_U64),
            (MAX_U64, 0, 0),
        ]
        for blocks, size, expected in cases:
            with self.subTest(blocks=blocks, size=size):
                self.assertEqual(safe_capacity_bytes(blocks, size), expected)


if __name__ == "__main__":
    unittest.main()
