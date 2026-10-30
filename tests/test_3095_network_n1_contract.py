#!/usr/bin/env python3
"""Source-only, fail-closed regression checks for the opt-in 3.0.95 E1000E probe."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SOURCE = (ROOT / "source/Core/NetworkN1ProbeKernel.cs").read_text()
BUILD = (ROOT / "tools/build-cosmos-3095-local.sh").read_text()
SMOKE = (ROOT / "tools/smoke-network-n1-3095-qemu.sh").read_text()
MAIN = (ROOT / "Kernel.cs").read_text()

class NetworkN1Isolation(unittest.TestCase):
    def test_upstream_3095_and_off_by_default(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.95"', PROJECT)
        self.assertIn('Version="3.0.95"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertIn('>false</ZonderqNetworkN1Probe>', PROJECT)
        self.assertIn('>ZonderqOS.NetworkN1ProbeKernel</CosmosKernelClass>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N1_PROBE', PROJECT)
        self.assertIn('GuardNetworkN1Probe', PROJECT)
        for feature in ("Storage", "Mouse", "Usb", "Keyboard", "Graphics"):
            self.assertIn(
                f'<CosmosEnable{feature} Condition="\'$(ZonderqNetworkN1Probe)\' == \'true\'">false</CosmosEnable{feature}>',
                PROJECT)
        self.assertIn('<CosmosEnableUART>true</CosmosEnableUART>', PROJECT)
        self.assertIn('Network N1 probe is x64-only', PROJECT)

    def test_no_storage_or_gui_in_probe(self):
        self.assertTrue(SOURCE.startswith("#if ZONDERQ_NETWORK_N1_PROBE"))
        self.assertIn("NetworkManager.IsInitialized", SOURCE)
        self.assertIn("Cosmos.Kernel.System.Diagnostics.Log", SOURCE)
        self.assertNotIn("Cosmos.Kernel.Core.IO", SOURCE)
        self.assertNotIn("Serial.WriteString", SOURCE)
        self.assertIn("NetworkManager.GetAdapter(i)", SOURCE)
        self.assertIn("adapter.MacAddress", SOURCE)
        self.assertIn("adapter.LinkUp", SOURCE)
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount(", "WriteBlock(",
                          "File.Write", "UserManager.Initialize(", "DhcpClient(",
                          "TryFormat(", "GuiManager("):
            self.assertNotIn(forbidden, SOURCE)
        self.assertIn("if (!Disk.Initialize())", MAIN)

    def test_exact_tag_and_real_qemu_gate(self):
        self.assertIn('VERSION="3.0.95"', BUILD)
        self.assertIn('git clone --depth 1 --branch "v$VERSION"', BUILD)
        self.assertIn('Wrong Cosmos package origin', BUILD)
        self.assertIn('network-n1)', BUILD)
        self.assertIn('-p:ZonderqNetworkN1Probe="$network_n1"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n1.iso', BUILD)
        self.assertIn('-smp 1', SMOKE)
        self.assertIn('-device e1000e,netdev=net0', SMOKE)
        self.assertIn('tests/verify_3095_network_n1.py', SMOKE)
        self.assertIn('sleep 8', SMOKE)
        self.assertIn('filter-dump', SMOKE)
        self.assertIn('Refusing to overwrite existing PCAP', SMOKE)
        self.assertIn('Refusing symlinked serial output', SMOKE)
        self.assertNotIn('rm -f -- "$PCAP"', SMOKE)
        self.assertIn('"$LOG" "$PCAP"', SMOKE)
        self.assertIn('NetworkManager.Send(frame, frame.Length)', SOURCE)
        self.assertIn('TX_QUEUED=', SOURCE)
        self.assertIn('0x88', SOURCE)
        self.assertIn('0xB5', SOURCE)
        self.assertIn('DHCP=NOT_ATTEMPTED IP=UNASSIGNED', SOURCE)

if __name__ == "__main__":
    unittest.main(verbosity=2)
