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
NEGATIVE_QEMU = (ROOT / "tools/smoke-network-rtl-negative-3095-qemu.sh").read_text()


class RealtekReadOnlyIsolation(unittest.TestCase):
    def test_stock_and_disabled_by_default(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertNotIn('<CosmosEnableNetwork Condition="\'$(ZonderqRealtekReadOnlyMmio)\'', PROJECT)
        self.assertIn("The read-only RTL probe needs PCI/DriverKit", PROJECT)
        self.assertIn(">false</ZonderqRealtekReadOnlyMmio>", PROJECT)
        self.assertIn("ZONDERQ_REALTEK_READONLY_MMIO", PROJECT)
        self.assertIn("[Driver(Default = false)]", DRIVER)
        self.assertIn('CosmosDriverInclude Include="ZonderqOS.Drivers.Realtek810xEReadOnlyProbeDriver"', PROJECT)
        self.assertIn("RTL810xE MMIO probe must use its own isolated serial boot profile", PROJECT)
        self.assertIn("RTL810xE MMIO probe must not enable storage", PROJECT)
        self.assertIn("RTL810xE UART-only probe must keep graphics disabled", PROJECT)
        self.assertIn("RTL810xE screen probe requires the text-console display", PROJECT)
        self.assertIn("RTL810xE screen output requires the isolated read-only MMIO probe", PROJECT)
        self.assertIn("<ZonderqRealtekScreen Condition=", PROJECT)
        self.assertIn("network-rtl-screen)", BUILD)
        self.assertIn("-nic none", NEGATIVE_QEMU)
        self.assertIn("offers=0 samples=0 reason=no matching PCI probe offer", NEGATIVE_QEMU)
        self.assertIn("NOT physical RTL or screen proof", NEGATIVE_QEMU)
        self.assertIn('realtek_screen=true', BUILD)
        self.assertIn('-p:ZonderqRealtekScreen="$realtek_screen"', BUILD)
        self.assertIn("RTL810xE MMIO probe is x64-only", PROJECT)
        self.assertIn("network-rtl-readonly)", BUILD)
        self.assertIn('-p:ZonderqRealtekReadOnlyMmio="$realtek_mmio"', BUILD)

    def test_exact_hardware_and_bounded_register_access(self):
        self.assertIn("vendorId: 0x10EC, deviceId: 0x8136", DRIVER)
        self.assertIn("subsystemVendorId: 0x1043, subsystemId: 0x200F", DRIVER)
        self.assertIn("revision: 0x06", DRIVER)
        self.assertIn("RegisterBar = 2", DRIVER)
        self.assertIn("MinimumRegisterBytes = 0x6D", DRIVER)
        self.assertIn("TxConfigOffset = 0x40", SAMPLER)
        self.assertIn("GetChipVariant(ushort xid)", SAMPLER)
        self.assertIn("phy=0x", DRIVER)
        self.assertIn("txconfig=0x", DRIVER)
        self.assertIn("variant=", DRIVER)
        self.assertIn("phy=0x", KERNEL)
        self.assertIn("txconfig=0x", KERNEL)
        self.assertIn("variant=", KERNEL)
        self.assertIn("pci.ReadConfig16(0x04)", DRIVER)
        self.assertIn("IsExactAsusRtl810xE(", DRIVER)
        self.assertIn("PCI identity/header mismatch; no MMIO reads", DRIVER)
        self.assertIn("IsExactAsusRtl810xE(ushort vendorId", SAMPLER)
        self.assertIn("Multifunction bit must not invalidate", SMOKE)
        self.assertIn("Gigabit Realtek device must be rejected", SMOKE)
        self.assertIn("pci.FindCapability(0x01)", DRIVER)
        self.assertIn("binding.MapRegisters(RegisterBar)", DRIVER)
        self.assertIn("Realtek810xERegisterSampler.TrySample", DRIVER)
        self.assertIn("return ProbeResult.Declined", DRIVER)
        self.assertIn("for (ushort i = 0; i < 6; i++)", SAMPLER)
        self.assertIn("PhyStatusOffset", SAMPLER)
        self.assertIn("for (ushort i = 0; i < 4; i++)", SAMPLER)
        self.assertIn("registers.Read8((ushort)(Realtek810xERegisterDecoder.TxConfigOffset + i))", SAMPLER)
        self.assertIn("ReadCount == 22", SMOKE)
        self.assertIn("ChangePhyOnSecondPass", SMOKE)
        self.assertIn("ChangeMacOnSecondPass", SMOKE)
        self.assertIn("ChangeTxConfigOnSecondPass", SMOKE)
        self.assertIn("first.RawPhyStatus != second.RawPhyStatus", SAMPLER)
        self.assertIn("first.RawTxConfig != second.RawTxConfig", SAMPLER)
        self.assertIn("first.MacAddress != second.MacAddress", SAMPLER)
        self.assertIn("samples=2", DRIVER)
        self.assertIn("samples=2", KERNEL)
        self.assertIn('" duplex=" + (!sample.Carrier', KERNEL)
        self.assertIn("malformed driver snapshot marker", (ROOT / "tests/verify_3095_realtek_readonly.py").read_text())
        self.assertIn("RTL8106e", SMOKE)
        self.assertIn("RTL8105e", SMOKE)
        self.assertIn("Failed TxConfig byte read", SMOKE)
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
        self.assertIn('return Decline("invalid or unstable MAC/PHY/TxConfig snapshot;', DRIVER)
        self.assertIn("Realtek810xEReadOnlyProbeDriver.LastDeclineReason", KERNEL)
        self.assertIn("reason=" + '" + reason', KERNEL)
        self.assertIn("SuccessfulSamples != 1", KERNEL)

    def test_serial_only_and_no_false_link_pass(self):
        self.assertIn("class NetworkRealtekReadOnlyKernel", KERNEL)
        self.assertIn("Console.WriteLine(message)", KERNEL)
        self.assertIn("catch (Exception)", KERNEL)
        self.assertIn("REGISTER SNAPSHOT / NOT A NETWORK TEST", KERNEL)
        self.assertIn("PROBE FAILED / NO NIC BOUND", KERNEL)
        self.assertIn("Photograph this screen for diagnosis.", KERNEL)
        self.assertIn("Display(\"MAC: \" + sample.MacAddress)", KERNEL)
        self.assertNotIn("Display(\"HEARTBEAT", KERNEL)
        self.assertIn("SuccessfulSamples != 1", KERNEL)
        self.assertIn("[NET3095-RTL810XE][FAIL]", KERNEL)
        self.assertIn("SNAPSHOT=PASS", KERNEL)
        self.assertIn("TX/RX/DMA/IRQ not tested", KERNEL)
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount(", "File.Write",
                          "GuiManager(", "DhcpClient(", "NetworkManager.Send("):
            self.assertNotIn(forbidden, KERNEL)


if __name__ == "__main__":
    unittest.main(verbosity=2)
