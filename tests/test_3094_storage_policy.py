#!/usr/bin/env python3
"""Source regression gate for the stock Cosmos 3.0.95 storage policy.

This is intentionally a *source contract* test, not proof of bare-metal I/O.
Kernel compilation and QEMU validation remain separate checks.
The historical filename is retained for existing callers.
"""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[1]
POLICY = (ROOT / "source/Storage/DataVolumePolicy.cs").read_text()
DISK = (ROOT / "source/Storage/Disk.cs").read_text()
KERNEL = (ROOT / "Kernel.cs").read_text()
GUI = (ROOT / "source/GUI/StorageMountManager.cs").read_text()
CSPROJ = (ROOT / "ZonderqOS.csproj").read_text()


def require_stock_cosmos(project: str) -> None:
    """Require the exact SDK and both unconditional stock package references."""
    def require(condition, message):
        if not condition:
            raise AssertionError(message)

    root = ET.fromstring(project)
    require(root.attrib.get('Sdk') == 'Cosmos.Sdk/3.0.95', 'stock SDK 3.0.95 required')
    expected = {'Cosmos.Kernel', 'Cosmos.Kernel.System'}
    found = []
    for group in root:
        for item in group:
            if item.tag != 'PackageReference':
                continue
            name = item.attrib.get('Include') or item.attrib.get('Update') or item.attrib.get('Remove', '')
            if name not in expected:
                continue
            require(group.tag == 'ItemGroup', 'invalid package group')
            require(not group.attrib.get('Condition') and not item.attrib.get('Condition'), 'conditional stock package')
            require(item.attrib.get('Include') == name, 'stock package must use Include')
            require(item.attrib.get('Version') == '3.0.95', 'stock package version mismatch: ' + name)
            require('VersionOverride' not in item.attrib and len(item) == 0, 'package metadata overrides stock pin')
            found.append(name)
    require(sorted(found) == sorted(expected), 'missing or duplicate stock package')


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
    def test_stock_3095_only(self):
        require_stock_cosmos(CSPROJ)
        self.assertIn('ZONDERQ_READONLY_HARDWARE_PROBE', KERNEL)

    def test_stock_gate_rejects_sdk_downgrade(self):
        with self.assertRaises(AssertionError):
            require_stock_cosmos(CSPROJ.replace('Cosmos.Sdk/3.0.95', 'Cosmos.Sdk/3.0.94'))

    def test_stock_gate_rejects_package_drift(self):
        for name in ('Cosmos.Kernel', 'Cosmos.Kernel.System'):
            for mutation in ('downgrade', 'missing', 'duplicate', 'conditional', 'override', 'child-version', 'update', 'conditional-group'):
                with self.subTest(package=name, mutation=mutation):
                    root = ET.fromstring(CSPROJ)
                    group, item = next((g, i) for g in root for i in g
                                       if i.tag == 'PackageReference' and i.attrib.get('Include') == name)
                    if mutation == 'downgrade': item.set('Version', '3.0.94')
                    elif mutation == 'missing': group.remove(item)
                    elif mutation == 'duplicate': group.append(ET.fromstring(ET.tostring(item)))
                    elif mutation == 'conditional': item.set('Condition', "'$(Configuration)' == 'Debug'")
                    elif mutation == 'conditional-group': group.set('Condition', "'$(Configuration)' == 'Debug'")
                    elif mutation == 'override': item.set('VersionOverride', '3.0.94')
                    elif mutation == 'child-version': ET.SubElement(item, 'Version').text = '3.0.94'
                    elif mutation == 'update': item.set('Update', item.attrib.pop('Include'))
                    with self.assertRaises(AssertionError):
                        require_stock_cosmos(ET.tostring(root, encoding='unicode'))

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

    def test_normal_disk_gui_is_default_and_diagnostics_are_explicit(self):
        self.assertIn("<ZonderqInputDiagnosticBoot Condition=", CSPROJ)
        self.assertIn(">false</ZonderqInputDiagnosticBoot>", CSPROJ)
        self.assertIn("<ZonderqReadOnlyHardwareProbe Condition=", CSPROJ)
        self.assertIn(">false</ZonderqReadOnlyHardwareProbe>", CSPROJ)
        self.assertIn("<ZonderqRamRoot Condition=", CSPROJ)
        self.assertIn(">false</ZonderqRamRoot>", CSPROJ)

    def test_no_partition_zero_root_mount(self):
        init = method(DISK, "public static bool Initialize()")
        self.assertIn("DataVolumePolicy.TryFindDataPartition", init)
        self.assertIn('new FatFileSystemType(data)', init)
        self.assertIn('VfsManager.TryMount("rootfat", ReadOnlySpan<char>.Empty, MountFlags.None, "/"', init)
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
        self.assertIn("Manual mounts disabled in Cosmos 3.0.94", mount)
        self.assertIn("Manual mounts disabled in Cosmos 3.0.94", GUI)
        self.assertNotIn('VfsManager.TryMount(', mount)
        self.assertNotIn('VfsManager.TryMount(', GUI)
        self.assertNotIn('mountedPartitions[0] = "/"', GUI)
        self.assertNotIn('GetMountPoint(0) ?? "/"', GUI)


if __name__ == "__main__":
    unittest.main(verbosity=2)
