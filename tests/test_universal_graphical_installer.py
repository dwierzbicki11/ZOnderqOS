#!/usr/bin/env python3
"""Regression & safety contracts for ZonderqOS Gen3 Universal Graphical Installer."""
import subprocess
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
INSTALLER_GUI = (ROOT / "source/GUI/Application/ZonderqInstallerApp.cs").read_text(encoding="utf-8")
INSTALLER_ENGINE = (ROOT / "source/Storage/SystemInstallerEngine.cs").read_text(encoding="utf-8")
FORMAT_ENGINE = (ROOT / "source/Storage/DiskFormattingEngine.cs").read_text(encoding="utf-8")
DISK_CATALOG = (ROOT / "source/Storage/DiskCatalog.cs").read_text(encoding="utf-8")
DISK_TOKEN = (ROOT / "source/Storage/DiskIdentityToken.cs").read_text(encoding="utf-8")
STEP_TRACKER = (ROOT / "source/Storage/InstallerStepTracker.cs").read_text(encoding="utf-8")
INPUT_VALIDATOR = (ROOT / "source/Storage/InstallerInputValidator.cs").read_text(encoding="utf-8")
DISK_MANAGER = (ROOT / "source/GUI/Application/DiskManagerApp.cs").read_text(encoding="utf-8")
DISK_CS = (ROOT / "source/Storage/Disk.cs").read_text(encoding="utf-8")


class UniversalGraphicalInstallerContracts(unittest.TestCase):
    def test_all_seven_installer_screens_present(self):
        for screen_const in (
            "ScreenWelcome = 0",
            "ScreenRequirements = 1",
            "ScreenSelectDisk = 2",
            "ScreenInstallType = 3",
            "ScreenUserSetup = 4",
            "ScreenProgress = 5",
            "ScreenComplete = 6",
        ):
            self.assertIn(screen_const, INSTALLER_GUI)

        # Screen 1 required strings
        self.assertIn("Welcome to ZonderqOS Setup", INSTALLER_GUI)
        self.assertIn("Install ZonderqOS", INSTALLER_GUI)
        self.assertIn("Try ZonderqOS without installing", INSTALLER_GUI)

        # Screen 4 required options
        self.assertIn("Erase Disk and Install ZonderqOS", INSTALLER_GUI)
        self.assertIn("Advanced Installation", INSTALLER_GUI)

        # Screen 7 required completion text & buttons
        self.assertIn("ZonderqOS has been successfully installed.", INSTALLER_GUI)
        self.assertIn("Restart Now", INSTALLER_GUI)
        self.assertIn("Shut Down", INSTALLER_GUI)

    def test_nine_real_installation_progress_steps(self):
        expected_steps = (
            "Preparing disk",
            "Creating partitions",
            "Formatting filesystem",
            "Installing bootloader",
            "Copying system files",
            "Configuring boot slots",
            "Setting up user account",
            "Verifying installation",
            "Finishing installation",
        )
        for step in expected_steps:
            self.assertIn(f'"{step}"', STEP_TRACKER)

    def test_destructive_erase_warning_and_typed_confirmation_gate(self):
        self.assertIn("WARNING: ALL DATA WILL BE DELETED", INSTALLER_GUI)
        self.assertIn("You are about to erase the selected disk.", INSTALLER_GUI)
        self.assertIn("This action cannot be undone.", INSTALLER_GUI)
        self.assertIn("Cancel", INSTALLER_GUI)
        self.assertIn("Erase Disk and Install", INSTALLER_GUI)
        self.assertIn("selectedDiskIndex = -1", INSTALLER_GUI)
        self.assertIn("InstallerInputValidator.ValidateDiskConfirmation", INSTALLER_GUI)
        self.assertIn("DiskCatalog.VerifyIdentityBeforeWrite", FORMAT_ENGINE)
        self.assertIn("object.ReferenceEquals(DeviceReference, currentDevice)", DISK_TOKEN)

    def test_password_security_and_no_manual_gc(self):
        self.assertIn("Crypto.HashPasswordV2", INSTALLER_ENGINE)
        self.assertIn("PasswordPolicy.Validate", INSTALLER_ENGINE)
        self.assertIn("ClearSensitiveBuffers()", INSTALLER_GUI)
        self.assertIn("request.ClearSensitiveData()", INSTALLER_ENGINE)
        for source in (INSTALLER_GUI, INSTALLER_ENGINE, FORMAT_ENGINE, DISK_CATALOG, DISK_MANAGER):
            self.assertNotIn("GC.Collect", source)

    def test_live_ram_root_fallback_when_no_zonderqdata_present(self):
        self.assertIn("public static bool IsLiveSession", DISK_CS)
        self.assertIn("TryMountLiveRamRoot", DISK_CS)
        self.assertIn("LimineBootMediaDetector.IsLiveModeRequestedByCmdline()", DISK_CS)

    def test_disk_manager_protects_active_system_and_boot_partitions(self):
        self.assertIn("DiskCatalog.IsDeviceHostingActiveRoot", DISK_MANAGER)
        self.assertIn("LimineBootMediaDetector.IsBootOrInstallationMedia", DISK_MANAGER)
        self.assertIn("InstallerInputValidator.ValidateDiskConfirmation", DISK_MANAGER)

    def test_universal_installer_dotnet_smoke_suite_passes(self):
        res = subprocess.run(
            ["dotnet", "run", "--project", str(ROOT / "tests/UniversalInstallerSmoke")],
            cwd=str(ROOT),
            capture_output=True,
            text=True,
            timeout=120,
        )
        self.assertEqual(res.returncode, 0, f"UniversalInstallerSmoke failed:\n{res.stdout}\n{res.stderr}")
        self.assertIn("ALL UNIVERSAL INSTALLER SMOKE TESTS PASSED", res.stdout)

    def test_dual_boot_installation_and_chainloader_contract(self):
        dual_src = (ROOT / "source/Storage/DualBootManager.cs").read_text(encoding="utf-8")
        slot_src = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text(encoding="utf-8")
        cmd_src = (ROOT / "source/Shell/Commands/CmdDualBoot.cs").read_text(encoding="utf-8")

        self.assertIn("DualBootAlongsideExistingOs", FORMAT_ENGINE)
        self.assertIn("PrepareDiskForDualBoot", FORMAT_ENGINE)
        self.assertIn("PreserveExistingEfiPartition", FORMAT_ENGINE)
        self.assertIn("/EFI/kali/grubx64.efi", dual_src)
        self.assertIn("/EFI/Microsoft/Boot/bootmgfw.efi", dual_src)
        self.assertIn("/EFI/BOOT/BOOTX64_PREV.EFI", dual_src)
        self.assertIn("/boot/dualboot_entries.cfg", dual_src)
        self.assertIn("Dual-Boot Alongside Existing OS", INSTALLER_GUI)
        self.assertIn("DualBootManager.LoadSavedEntries", slot_src)
        self.assertIn('public string Name => "dualboot"', cmd_src)


if __name__ == "__main__":
    unittest.main(verbosity=2)
