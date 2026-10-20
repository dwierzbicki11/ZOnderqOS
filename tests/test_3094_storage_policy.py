#!/usr/bin/env python3
"""Source regression gate for the Cosmos 3.0.94 persistent-storage policy.

This is intentionally a *source contract* test, not proof of bare-metal I/O.
Full compilation and the QEMU USB smoke run independently in the 3.0.94 CI.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
POLICY = (ROOT / "source/Storage/DataVolumePolicy.cs").read_text()
DISK = (ROOT / "source/Storage/Disk.cs").read_text()
KERNEL = (ROOT / "Kernel.cs").read_text()
GUI = (ROOT / "source/GUI/StorageMountManager.cs").read_text()
CSPROJ = (ROOT / "ZonderqOS.csproj").read_text()


def method(code: str, signature: str) -> str:
    begin = code.index(signature)
    brace = code.index("{", begin)
    depth = 0
    for i in range(brace, len(code)):
        if code[i] == "{":
            depth += 1
        elif code[i] == "}":
            depth -= 1
            if depth == 0:
                return code[begin:i + 1]
    raise AssertionError(f"Unclosed method: {signature}")


class StorageSafetyRegression(unittest.TestCase):
    def test_3094_only(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', CSPROJ)
        self.assertIn('Version="3.0.94"', CSPROJ)
        self.assertIn('ZONDERQ_READONLY_HARDWARE_PROBE', KERNEL)

    def test_label_is_unique_fat32(self):
        self.assertIn('RequiredLabel = "ZONDERQDATA"', POLICY)
        self.assertIn("FatType.Fat32", POLICY)
        self.assertIn("FatBootSector.TryParse", POLICY)
        # Relabelled EFI volumes must still be refused by partition-table type.
        self.assertIn("IsDedicatedDataPartitionType(partition)", POLICY)
        self.assertIn("Gpt.BasicDataPartitionType", POLICY)
        self.assertIn("entry.SystemId == 0x0B || entry.SystemId == 0x0C", POLICY)
        self.assertIn("entry.StartSector == partition.StartSector", POLICY)
        self.assertIn("Multiple FAT32 ZONDERQDATA volumes", POLICY)
        self.assertIn("refusing to write to EFI", POLICY)

    def test_no_partition_zero_root_mount(self):
        init = method(DISK, "public static bool Initialize()")
        self.assertIn("DataVolumePolicy.TryFindDataPartition", init)
        self.assertIn('VfsManager.TryMount("fat", data, MountFlags.None, "/"', init)
        self.assertNotIn('TryMount("fat", "0"', init)
        self.assertNotIn("CreateStandardHierarchy();\n                }", init)

    def test_fail_before_mutating_user_database(self):
        boot = method(KERNEL, "protected override void BeforeRun()")
        self.assertRegex(boot, r"if \(!Disk\.Initialize\(\)\)")
        self.assertLess(boot.index("if (!Disk.Initialize())"),
                        boot.index("UserManager.Initialize()"))

    def test_partition_operations_disabled(self):
        fmt = method(DISK, "public static void FormatPartition(")
        part = method(DISK, "public static void PartitionDisk(")
        self.assertNotIn("TryFormat(", fmt)
        self.assertNotIn("WriteBlock(", part)
        self.assertIn("LastCommandSuccess = false", fmt)
        self.assertIn("LastCommandSuccess = false", part)

    def test_readonly_probe_allows_only_bounded_diagnostics(self):
        self.assertIn('command == "volumes" || command == "lsblk"', KERNEL)
        probe = method(KERNEL, "private static void PrintReadOnlyVolumes()")
        self.assertIn("DataVolumePolicy.IsApprovedDataPartition", probe)
        self.assertIn("DataVolumePolicy.TryFindDataPartition", probe)
        self.assertNotIn("WriteBlock(", probe)
        self.assertNotIn("TryMount(", probe)

    def test_interactive_mounts_require_policy(self):
        mount = method(DISK, "public static void MountPartition(")
        self.assertIn("DataVolumePolicy.IsSelectedDataPartition", mount)
        self.assertIn("DataVolumePolicy.IsSelectedDataPartition", GUI)
        self.assertNotIn('mountedPartitions[0] = "/"', GUI)
        self.assertNotIn('GetMountPoint(0) ?? "/"', GUI)


if __name__ == "__main__":
    unittest.main(verbosity=2)
