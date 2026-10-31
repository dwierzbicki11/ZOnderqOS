#!/usr/bin/env python3
"""Source-level NIC inventory contract. Separate compilation/runtime gates required."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PCI = (ROOT / "source/Hardware/PciConfigDiscoverySource.cs").read_text()
MODEL = (ROOT / "source/Hardware/PciDeviceModel.cs").read_text()
BARS = (ROOT / "source/Hardware/PciBarInventory.cs").read_text()
X64 = (ROOT / "source/Platform/X64/PciSysfsProvider.cs").read_text()
DEVICE = (ROOT / "source/Hardware/DeviceModel.cs").read_text()
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SHELL = (ROOT / "source/Shell/Command.cs").read_text()
COMMAND = (ROOT / "source/Shell/Commands/CmdNicDiag.cs").read_text()
DIAG = (ROOT / "source/Core/NetworkPciDiagnostics.cs").read_text()
KERNEL = (ROOT / "Kernel.cs").read_text()
NETWORK = (ROOT / "source/Core/Network.cs").read_text()
ARM64 = (ROOT / "source/Platform/Arm64/PciSysfsProvider.cs").read_text()


class Nic3094Safety(unittest.TestCase):
    def test_sdk_and_network_feature_gate_preserved(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
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

    def test_realtek_revision_and_subsystem_are_read_only(self):
        self.assertIn('revisionId = config.Read8(bus, device, function, 0x08)', PCI)
        self.assertIn('if (classCode == 0x02)', PCI)
        self.assertIn('if (headerType == 0x00)', PCI)
        self.assertIn('config.Read16(bus, device, function, 0x2C)', PCI)
        self.assertIn('config.Read16(bus, device, function, 0x2E)', PCI)
        self.assertIn('SubsystemVendorId', MODEL)
        self.assertIn('SubsystemDeviceId', MODEL)
        self.assertIn('RevisionId', DEVICE)
        for token in ('prog_if=', 'rev=', 'subsystem=', 'UNKNOWN'):
            self.assertIn(token, DIAG)
        self.assertNotIn('config.Write', PCI)

    def test_realtek_bar_inventory_is_read_only_and_fail_closed(self):
        self.assertIn("TryReadRealtek810xE", BARS)
        self.assertIn("config.Read16(", BARS)
        self.assertNotIn("config.Write", BARS)
        self.assertIn("if (index == 5)", BARS)
        self.assertIn("PciBarKind.Memory64", BARS)
        self.assertIn("TryReadRealtek810xEState", BARS)
        self.assertIn("LegacyInterruptDisabled", BARS)
        self.assertIn("TryReadRealtekPciState", X64)
        self.assertIn("TryReadRealtekPciState", ARM64)
        self.assertIn("IRQ not verified", DIAG)
        self.assertIn("TryReadRealtekBars", X64)
        self.assertIn("TryReadRealtekBars", ARM64)
        self.assertIn("size=UNKNOWN", DIAG)
        self.assertIn("driver=UNVERIFIED", DIAG)

    def test_realtek_capabilities_are_read_only_and_not_irq_proof(self):
        self.assertIn('TryReadRealtek810xECapabilities', BARS)
        self.assertIn('visited[offset]', BARS)
        self.assertIn('capabilities.Clear()', BARS)
        self.assertIn('TryReadRealtekCapabilities', X64)
        self.assertIn('TryReadRealtekCapabilities', ARM64)
        self.assertIn('PCI_CAPS=INVALID_OR_UNAVAILABLE', DIAG)
        self.assertIn('TryReadRealtek810xEPowerState', BARS)
        self.assertIn('TryReadRealtekPowerState', X64)
        self.assertIn('TryReadRealtekPowerState', ARM64)
        self.assertIn('PCI_POWER=INVALID_OR_UNAVAILABLE', DIAG)
        self.assertIn('no power transition', DIAG)
        self.assertIn('TryResolveRealtek810xEMsiXMemoryBars', BARS)
        self.assertIn('bar.Kind == PciBarKind.Io', BARS)
        self.assertIn('bar.Address > ulong.MaxValue - (ulong)offset', BARS)
        self.assertIn('TryResolveRealtekMsiXMemoryBars', X64)
        self.assertIn('TryResolveRealtekMsiXMemoryBars', ARM64)
        self.assertIn('MSI-X_MEMORY_BARS=INVALID_OR_UNASSIGNED', DIAG)
        self.assertIn('size/mapping/IRQ unverified', DIAG)
        self.assertIn('TryReadRealtek810xEInterruptCapabilities', BARS)
        self.assertIn('TryReadRealtekInterruptCapabilities', X64)
        self.assertIn('TryReadRealtekInterruptCapabilities', ARM64)
        self.assertIn('PCI_IRQ_CAPS=INVALID_OR_UNAVAILABLE', DIAG)
        self.assertIn('unmapped; IRQ not verified', DIAG)
        self.assertIn('IRQ not verified', DIAG)
        self.assertNotIn('config.Write', BARS)

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
