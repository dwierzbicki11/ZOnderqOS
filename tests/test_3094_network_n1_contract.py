#!/usr/bin/env python3
"""Static fail-closed checks for isolated Cosmos 3.0.94 NIC-link probe."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
PROJECT = (ROOT / "ZonderqOS.csproj").read_text()
SOURCE = (ROOT / "source/Core/NetworkN1ProbeKernel.cs").read_text()
BUILD = (ROOT / "tools/build-cosmos-3094-local.sh").read_text()
SMOKE = (ROOT / "tools/smoke-network-n1-3094-qemu.sh").read_text()
MAIN = (ROOT / "Kernel.cs").read_text()
SHELL = (ROOT / "source/Shell/Command.cs").read_text()


class NetworkN1Isolation(unittest.TestCase):
    def test_cosmos_pin_and_production_default(self):
        self.assertIn('Sdk="Cosmos.Sdk/3.0.94"', PROJECT)
        self.assertIn('<CosmosEnableNetwork>false</CosmosEnableNetwork>', PROJECT)
        self.assertIn('<ZonderqNetworkN1Probe Condition=', PROJECT)
        self.assertIn('>false</ZonderqNetworkN1Probe>', PROJECT)
        self.assertIn("'$(ZonderqNetworkN1Probe)' == 'true'", PROJECT)
        self.assertIn('>true</CosmosEnableNetwork>', PROJECT)
        self.assertIn('>ZonderqOS.NetworkN1ProbeKernel</CosmosKernelClass>', PROJECT)
        self.assertIn('ZONDERQ_NETWORK_N1_PROBE', PROJECT)

    def test_isolated_boot_never_mounts_root_or_touches_efi(self):
        self.assertTrue(SOURCE.startswith("#if ZONDERQ_NETWORK_N1_PROBE"))
        self.assertIn('public sealed class NetworkN1ProbeKernel : Sys.Kernel', SOURCE)
        self.assertIn('NetworkManager.IsInitialized', SOURCE)
        self.assertIn('NetworkManager.GetAdapter(i)', SOURCE)
        self.assertIn('adapter.MacAddress', SOURCE)
        self.assertIn('adapter.LinkUp', SOURCE)
        self.assertIn('adapter.Ready', SOURCE)
        for forbidden in ('Disk.Initialize(', 'VfsManager.TryMount(', 'WriteBlock(',
                          'File.Write', 'UserManager.Initialize(', 'ConfigureDhcp(',
                          'DhcpClient(', 'NetworkManager.Send(', 'TryFormat('):
            self.assertNotIn(forbidden, SOURCE)
        self.assertIn('if (!Disk.Initialize())', MAIN)

    def test_qemu_only_feature_profile_and_immutable_package_provenance(self):
        self.assertIn('GuardNetworkN1Probe', PROJECT)
        self.assertIn('Network N1 proof is x64-only', PROJECT)
        self.assertIn('network-n1)', BUILD)
        self.assertIn('-p:ZonderqNetworkN1Probe="$network_n1"', BUILD)
        self.assertIn('ZonderqOS-3094-network-n1.iso', BUILD)
        self.assertIn('v$VERSION', BUILD)
        self.assertIn('Wrong Cosmos package origin', BUILD)
        self.assertIn('Network N1 proof must use its isolated kernel', PROJECT)

    def test_qemu_requires_actual_link(self):
        self.assertIn('-nic none -netdev user,id=net0 -device e1000e,netdev=net0', SMOKE)
        self.assertIn('tests/verify_3094_network_n1.py', SMOKE)
        self.assertIn('sleep 8', SMOKE)
        self.assertIn('[NET3094-N1] PASS READY_LINK=', SOURCE)
        self.assertIn('DHCP=NOT_ATTEMPTED IP=UNASSIGNED', SOURCE)
        self.assertIn('new CmdNicDiag()', SHELL)


if __name__ == "__main__":
    unittest.main(verbosity=2)
