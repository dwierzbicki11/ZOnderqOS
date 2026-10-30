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
        self.assertIn('>false</ZonderqNetworkN2Dhcp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N2_DHCP', PROJECT)
        self.assertIn('>false</ZonderqNetworkN3Icmp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N3_ICMP', PROJECT)
        self.assertIn('Network N3 ICMP proof requires the isolated N2 DHCP probe.', PROJECT)
        self.assertIn('Network N2 DHCP proof requires the isolated N1 probe.', PROJECT)
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
        # The N1 build compiles out the N2-only DHCP block. Keep the N1
        # isolation contract strict without forbidding the explicit N2 mode.
        n1_source = SOURCE.split("#if ZONDERQ_NETWORK_N2_DHCP", 1)[0]
        for forbidden in ("Disk.Initialize(", "VfsManager.TryMount(", "WriteBlock(",
                          "File.Write", "UserManager.Initialize(", "DhcpClient(",
                          "TryFormat(", "GuiManager("):
            self.assertNotIn(forbidden, n1_source)
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
        self.assertIn('new DhcpClient().SendDiscoverPacket()', SOURCE)
        self.assertIn('NetworkManager.Primary.IPConfig', SOURCE)
        self.assertIn('[NET3095-N2] DHCP=PASS ip=', SOURCE)
        self.assertIn('network-n2)', BUILD)
        self.assertIn('-p:ZonderqNetworkN2Dhcp="$network_n2"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n2.iso', BUILD)
        self.assertIn('tests/verify_3095_network_n2.py', SMOKE)
        self.assertIn('No correlated DHCP RX/TX capture', SMOKE)
        self.assertIn('new IcmpClient()', SOURCE)
        self.assertIn('ping.SendEcho(id: 0x3095, sequence: 1)', SOURCE)
        self.assertIn('ping.Receive(ref remote, 5000)', SOURCE)
        self.assertIn('[NET3095-N3] ICMP=PASS from=', SOURCE)
        self.assertIn('network-n3)', BUILD)
        self.assertIn('-p:ZonderqNetworkN3Icmp="$network_n3"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n3.iso', BUILD)
        self.assertIn('tests/verify_3095_network_n3.py', SMOKE)
        self.assertIn('No correlated ICMP echo RX/TX capture', SMOKE)
        self.assertIn('>false</ZonderqNetworkN4Tcp>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N4_TCP', PROJECT)
        self.assertIn('Network N4 TCP proof requires the isolated N3 ICMP probe.', PROJECT)
        self.assertIn('network-n4)', BUILD)
        self.assertIn('-p:ZonderqNetworkN4Tcp="$network_n4"', BUILD)
        self.assertIn('ZonderqOS-3095-network-n4.iso', BUILD)
        self.assertIn('new System.Net.Sockets.TcpClient()', SOURCE)
        self.assertIn('client.Client.ReceiveTimeout = 5000', SOURCE)
        self.assertIn('[NET3095-N4] TCP=PASS from=', SOURCE)
        self.assertIn('tests/verify_3095_network_n4.py', SMOKE)
        self.assertIn('tests/network_3095_tcp_host.py', SMOKE)
        self.assertIn('No correlated TCP RX/TX capture', SMOKE)

if __name__ == "__main__":
    unittest.main(verbosity=2)
