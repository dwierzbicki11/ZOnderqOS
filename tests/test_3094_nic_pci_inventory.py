#!/usr/bin/env python3
"""Source-level NIC inventory contract. Separate compilation/runtime gates required."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SHELL = (ROOT / "source/Shell/Command.cs").read_text()
COMMAND = (ROOT / "source/Shell/Commands/CmdNicDiag.cs").read_text()
DIAG = (ROOT / "source/Core/NetworkPciDiagnostics.cs").read_text()
KERNEL = (ROOT / "Kernel.cs").read_text()
NETWORK = (ROOT / "source/Core/Network.cs").read_text()
ARM64 = (ROOT / "source/Platform/Arm64/PciSysfsProvider.cs").read_text()


class Nic3094Safety(unittest.TestCase):
    def test_sdk_and_network_feature_gate_preserved(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_DISABLED', PROJECT)
        self.assertIn('public static bool IsReady => false;', NETWORK)
        self.assertIn('public static bool ConfigureDhcp() => false;', NETWORK)

    def test_real_pci_scan_not_fabricated(self):
        self.assertIn('PciSysfsProvider.TryDiscover(out devices)', DIAG)
        self.assertIn('d.ClassCode != 0x02', DIAG)
        self.assertIn('d.Id.Address', DIAG)
        self.assertIn('d.VendorId.ToString("X4")', DIAG)
        self.assertIn('d.DeviceId.ToString("X4")', DIAG)
        self.assertIn('return false;', DIAG)
        self.assertIn('return false;', ARM64)
        self.assertIn('PCI discovery unavailable', DIAG)

    def test_laptop_and_qemu_pci_ids(self):
        for value in ('0x10EC', '0x8136', '0xB723', '0x8086', '0x100E', '0x10D3'):
            self.assertIn(value, DIAG)
        for label in ('RTL810xE', 'RTL8723BE', 'E1000', 'E1000E'):
            self.assertIn(label, DIAG)
        self.assertIn('link=UNKNOWN', DIAG)
        self.assertIn('driver=UNVERIFIED', DIAG)
        self.assertIn('ip=UNASSIGNED', DIAG)

    def test_no_mutating_network_or_disk_io(self):
        for forbidden in ('WriteBlock(', 'TryMount(', 'ConfigureDhcp(', 'NetworkManager.Send(',
                          'SetRawReceiveHandler(', 'PciConfigIoAccessor(', 'NetworkManager.Initialize('):
            self.assertNotIn(forbidden, DIAG)
        self.assertIn('NetworkPciDiagnostics.PrintReport(true)', COMMAND)
        self.assertIn('new CmdNicDiag()', SHELL)
        self.assertIn('CommandIO.LastCommandSuccess = false;', COMMAND)
        self.assertIn('Usage: nicdiag', COMMAND)

    def test_diskless_probes_can_use_nicdiag_without_full_shell(self):
        self.assertIn('if (command == "nicdiag")', KERNEL)
        self.assertIn('NetworkPciDiagnostics.PrintReport(false)', KERNEL)
        self.assertIn('Commands: desktop | inputscan | nicdiag', KERNEL)
        self.assertIn('if (!Disk.Initialize())', KERNEL)
        self.assertLess(KERNEL.index('if (command == "nicdiag")'),
                        KERNEL.index('private static void PrintReadOnlyVolumes()'))


if __name__ == "__main__":
    unittest.main(verbosity=2)
