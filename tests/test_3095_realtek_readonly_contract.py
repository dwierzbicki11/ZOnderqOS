#!/usr/bin/env python3
"""Source-only isolation checks; never claim physical RTL810xE hardware proof."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
DRIVER = (ROOT / "source/Drivers/Realtek810xEReadOnlyProbeDriver.cs").read_text()
KERNEL = (ROOT / "source/Core/NetworkRealtekReadOnlyKernel.cs").read_text()
BUILD = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
SAMPLER = (ROOT / "source/Hardware/Realtek810xERegisterSnapshot.cs").read_text()
SMOKE = (ROOT / "tests/PciMetadataSmoke/Program.cs").read_text()


class RealtekReadOnlyIsolation(unittest.TestCase):
    def test_stock_and_disabled_by_default(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertIn(">false</ZonderqRealtekReadOnlyMmio>", PROJECT)
        self.assertIn("ZONDERQ_REALTEK_READONLY_MMIO", PROJECT)
        self.assertIn("[Driver(Default = false)]", DRIVER)
        self.assertIn('CosmosDriverInclude Include="ZonderqOS.Drivers.Realtek810xEReadOnlyProbeDriver"', PROJECT)
        self.assertIn("RTL810xE MMIO probe must use its own isolated serial boot profile", PROJECT)
        self.assertIn("RTL810xE MMIO probe must not enable disk or GUI", PROJECT)
        self.assertIn("RTL810xE MMIO probe is x64-only", PROJECT)
        self.assertIn("network-rtl-readonly)", BUILD)
        self.assertIn('-p:ZonderqRealtekReadOnlyMmio="$realtek_mmio"', BUILD)

    def test_exact_hardware_and_bounded_register_access(self):
        self.assertIn("vendorId: 0x10EC, deviceId: 0x8136", DRIVER)
        self.assertIn("subsystemVendorId: 0x1043, subsystemId: 0x200F", DRIVER)
        self.assertIn("revision: 0x06", DRIVER)
        self.assertIn("RegisterBar = 2", DRIVER)
        self.assertIn("MinimumRegisterBytes = 0x6D", DRIVER)
        self.assertIn("pci.ReadConfig16(0x04)", DRIVER)
        self.assertIn("pci.FindCapability(0x01)", DRIVER)
        self.assertIn("binding.MapRegisters(RegisterBar)", DRIVER)
        self.assertIn("Realtek810xERegisterSampler.TrySample", DRIVER)
        self.assertIn("return ProbeResult.Declined", DRIVER)
        self.assertIn("for (ushort i = 0; i < 6; i++)", SAMPLER)
        self.assertIn("PhyStatusOffset", SAMPLER)
        self.assertIn("[NET3095-RTL810XE-READER] PASS", SMOKE)
        for forbidden in ("Write8(", "Write16(", "Write32(", "EnableMemorySpace(",
                          "EnableBusMastering(", "AllocateDma(", "TryAllocateDma(",
                          "TryRequestInterrupt(", "PublishNetwork(", "PublishChild("):
            self.assertNotIn(forbidden, DRIVER)

    def test_probe_failures_are_bounded_and_reported(self):
        self.assertIn("LastDeclineReason", DRIVER)
        self.assertIn("LastDeclineReason = null;", DRIVER)
        self.assertIn("catch (Exception ex)", DRIVER)
        self.assertIn('return Decline("PCI/MMIO probe exception: " + ex.GetType().Name)', DRIVER)
        self.assertIn('return Decline("PCI memory decoding disabled;', DRIVER)
        self.assertIn('return Decline("PCI device not in D0;', DRIVER)
        self.assertIn('return Decline("BAR2 is not an assigned MMIO window', DRIVER)
        self.assertIn('return Decline("mapped BAR2 window too short', DRIVER)
        self.assertIn('return Decline("invalid MAC/PHY snapshot;', DRIVER)
        self.assertIn("Realtek810xEReadOnlyProbeDriver.LastDeclineReason", KERNEL)
        self.assertIn("reason=" + '" + reason', KERNEL)
        self.assertIn("SuccessfulSamples != 1", KERNEL)

    def test_serial_only_and_no_false_link_pass(self):
        self.assertIn("class NetworkRealtekReadOnlyKernel", KERNEL)
        self.assertIn("SuccessfulSamples != 1", KERNEL)
        self.assertIn("[NET3095-RTL810XE][FAIL]", KERNEL)
        self.assertIn("SNAPSHOT=PASS", KERNEL)
        self.assertIn("TX/RX/DMA/IRQ not tested", KERNEL)
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount(", "File.Write",
                          "GuiManager(", "DhcpClient(", "NetworkManager.Send("):
            self.assertNotIn(forbidden, KERNEL)


if __name__ == "__main__":
    unittest.main(verbosity=2)
