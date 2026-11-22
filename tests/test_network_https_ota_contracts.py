#!/usr/bin/env python3
"""Static contract checks for ZonderqOS networking and HTTPS TLS 1.2/1.3 OTA."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class TestNetworkHttpsOtaContracts(unittest.TestCase):
    def test_cosmos_enable_network(self):
        content = (ROOT / "ZonderqOS.csproj").read_text(encoding="utf-8")
        self.assertIn(
            "<CosmosEnableNetwork Condition=\"'$(CosmosEnableNetwork)' == ''\">true</CosmosEnableNetwork>",
            content,
        )

    def test_https_to_http_downgrade_blocked(self):
        content = (ROOT / "source/Core/Networking/OtaUrlParser.cs").read_text(encoding="utf-8")
        self.assertIn("Refused HTTPS-to-HTTP redirect downgrade.", content)

    def test_tls_version_and_chain_verification(self):
        content = (ROOT / "source/Core/Networking/ManagedTlsClient.cs").read_text(encoding="utf-8")
        self.assertIn("ProtocolVersion.TLSv13.DownTo(ProtocolVersion.TLSv12)", content)
        self.assertIn("TlsTrustStore.VerifyChain", content)

    def test_ota_update_client_ed25519_and_github_url(self):
        content = (ROOT / "source/Core/Updates/OtaUpdateClient.cs").read_text(encoding="utf-8")
        self.assertIn("https://github.com/dwierzbicki11/ZOnderqOS/releases/latest/download", content)
        self.assertIn("ReleaseSignatureVerifier.VerifyProductionManifest", content)

    def test_update_manager_ota_download_wired(self):
        content = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text(encoding="utf-8")
        self.assertIn("OtaUpdateClient.DownloadVerifiedPackageToDirectory", content)

    def test_network_mutations_require_root(self):
        net = (ROOT / "source/Core/Network.cs").read_text(encoding="utf-8")
        cmd_ip = (ROOT / "source/Shell/Commands/CmdIp.cs").read_text(encoding="utf-8")
        cmd_net = (ROOT / "source/Shell/Commands/CmdNetwork.cs").read_text(encoding="utf-8")
        self.assertIn("CanMutateNetworkConfig", net)
        self.assertIn("SecurityContext.CurrentUid != 0", net)
        self.assertIn('RequireRoot("static")', cmd_ip)
        self.assertIn('RequireRoot("dhcp")', cmd_ip)
        self.assertIn('RequireRoot("dhcp")', cmd_net)
        self.assertIn('RequireRoot("device")', cmd_net)

    def test_manifest_arch_tracks_kernel_build_info_and_settings_opens_network_center(self):
        manifest = (ROOT / "source/Core/Updates/UpdateManifest.cs").read_text(encoding="utf-8")
        settings = (ROOT / "source/GUI/Application/SettingsApp.cs").read_text(encoding="utf-8")
        self.assertIn("ExpectedArchitecture = KernelBuildInfo.Architecture;", manifest)
        self.assertIn("new NetworkCenterApp(", settings)


if __name__ == "__main__":
    unittest.main()
