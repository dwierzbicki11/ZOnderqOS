#!/usr/bin/env python3
"""Validate that full Cosmos 3.0.94 GUI's optional root is RAM-only."""
from pathlib import Path
import unittest
ROOT=Path(__file__).resolve().parents[1]
PROJECT=(ROOT/"ZonderqOS.csproj").read_text()
RAM=(ROOT/"source/Storage/RamRootVolume.cs").read_text()
DISK=(ROOT/"source/Storage/Disk.cs").read_text()
POLICY=(ROOT/"source/Storage/DataVolumePolicy.cs").read_text()
WORKFLOW=(ROOT/".github/workflows/experimental-cosmos-3094-usb.yml").read_text()
class RamRootContract(unittest.TestCase):
    def test_3094_only_and_explicit_profile(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('ZONDERQ_3094_RAM_ROOT', PROJECT)
        self.assertIn('ZonderqRamRoot=true', WORKFLOW)
        self.assertIn('ZonderqReadOnlyHardwareProbe=false', WORKFLOW)
        self.assertIn('ZonderqInputDiagnosticBoot=false', WORKFLOW)
        self.assertIn('RAM-root GUI and read-only probe are mutually exclusive', PROJECT)
    def test_writes_only_private_ram_buffer(self):
        self.assertIn('new byte[Capacity]', RAM)
        self.assertIn('64 * 1024 * 1024', RAM)
        self.assertIn('data.Slice(0, size).CopyTo(storage.AsSpan(', RAM)
        self.assertIn('storage.AsSpan(', RAM)
        self.assertIn('No persistence and no physical disk I/O', RAM)
        self.assertNotIn('File.', RAM)
        self.assertNotIn('PhysicalAddress', RAM)
        self.assertNotIn('Disk.Initialize', RAM)
    def test_explicit_partition_identity_and_format(self):
        self.assertIn('p.Host, disk', RAM)
        self.assertIn('new FatFileSystemType(selected)', RAM)
        self.assertIn('FatType.Fat32', RAM)
        self.assertIn('DataVolumePolicy.RequiredLabel', RAM)
        self.assertIn('DataVolumePolicy.IsApprovedDataPartition(selected)', RAM)
        self.assertNotIn('VfsManager.TryFormat', RAM)
        self.assertNotIn('TryFormat("fat", "0"', RAM)
    def test_root_never_falls_back_to_physical_disk(self):
        self.assertIn('#if ZONDERQ_3094_RAM_ROOT', DISK)
        self.assertIn('RamRootVolume.TryCreate(out Partition data)', DISK)
        self.assertIn('refusing host-disk fallback', DISK)
        self.assertIn('VfsManager.TryMount("fat", data', DISK)
    def test_no_physical_secondary_mounts(self):
        self.assertIn('return RamRootVolume.IsRamPartition(partition)', POLICY)
        self.assertIn('object.ReferenceEquals(partition.Host, disk)', RAM)

    def test_efi_write_gate(self):
        self.assertIn('QEMU RAM GUI boot - prove EFI canary unchanged', WORKFLOW)
        self.assertIn('sha256sum --check "$canary.sha256"', WORKFLOW)
if __name__=="__main__":
    unittest.main(verbosity=2)
