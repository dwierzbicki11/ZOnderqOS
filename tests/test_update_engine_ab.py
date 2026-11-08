#!/usr/bin/env python3
"""Tests and regression contracts for ZonderqOS Gen 3 Real Update Engine and A/B Bootloader."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
LIMINE_CONF = (ROOT / "Bootloader/limine.conf").read_text()
UPDATE_MGR = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text()
UPDATE_MANIFEST = (ROOT / "source/Core/Updates/UpdateManifest.cs").read_text()
BOOT_SLOT_MGR = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text()
KERNEL_BUILD_INFO = (ROOT / "source/Core/KernelBuildInfo.cs").read_text()
KERNEL_CS = (ROOT / "Kernel.cs").read_text()
CMD_UPDATE = (ROOT / "source/Shell/Commands/CmdUpdate.cs").read_text()
UPDATER_APP = (ROOT / "source/GUI/Application/SystemUpdaterApp.cs").read_text()


class UpdateEngineABTests(unittest.TestCase):
    def test_limine_conf_has_ab_slots_and_valid_schemes(self):
        # Limine v10 syntax: fslabel(...) and boot():/...
        self.assertIn("fslabel(ZONDERQDATA):/boot/slot_a/ZonderqOS.elf", LIMINE_CONF)
        self.assertIn("fslabel(ZONDERQDATA):/boot/slot_b/ZonderqOS.elf", LIMINE_CONF)
        self.assertIn("boot():/boot/ZonderqOS.elf", LIMINE_CONF)
        # Ensure invalid 'fs_label' is completely absent
        self.assertNotIn("fs_label", LIMINE_CONF)
        self.assertIn("default_entry:", LIMINE_CONF)

    def test_no_mock_payloads_or_fake_sha256(self):
        # Eliminate all ASCII.GetBytes fake payload generations
        self.assertNotIn('Encoding.ASCII.GetBytes("ZONDERQ_UPDATE_PAYLOAD', UPDATE_MGR)
        self.assertNotIn('Encoding.ASCII.GetBytes("ZONDERQ_CLOUD_PAYLOAD', UPDATE_MGR)
        self.assertNotIn("mockPayload", UPDATE_MGR)

    def test_kernel_build_info_embedded_identity(self):
        self.assertIn('public const string Architecture = "x86_64";', KERNEL_BUILD_INFO)
        self.assertIn('public const string CosmosVersion = "Cosmos 3.0.95 Native Core";', KERNEL_BUILD_INFO)
        self.assertIn("public static string RunningSlot", KERNEL_BUILD_INFO)
        self.assertIn("public static bool IsHealthyAndConfirmed", KERNEL_BUILD_INFO)

    def test_boot_slot_manager_contracts(self):
        self.assertIn("public const string SlotAPath = \"/boot/slot_a/ZonderqOS.elf\";", BOOT_SLOT_MGR)
        self.assertIn("public const string SlotBPath = \"/boot/slot_b/ZonderqOS.elf\";", BOOT_SLOT_MGR)
        self.assertIn("StageKernelToInactiveSlot", BOOT_SLOT_MGR)
        self.assertIn("ActivateSlot", BOOT_SLOT_MGR)
        self.assertIn("ConfirmSlotHealth", BOOT_SLOT_MGR)
        self.assertIn("CheckWatchdogFailover", BOOT_SLOT_MGR)
        self.assertIn("SyncLimineConfig", BOOT_SLOT_MGR)

    def test_kernel_cs_initialization_order(self):
        # BootSlotManager initialized after Disk
        disk_pos = KERNEL_CS.find("Disk.Initialize()")
        bootslot_pos = KERNEL_CS.find("BootSlotManager.Initialize()")
        self.assertGreater(bootslot_pos, disk_pos, "BootSlotManager must be initialized after Disk.Initialize")

        # IntegrityGuard initialized after SystemIdentity
        sysid_pos = KERNEL_CS.find("SystemIdentity.Initialize()")
        integ_pos = KERNEL_CS.find("IntegrityGuard.Initialize()")
        self.assertGreater(integ_pos, sysid_pos, "IntegrityGuard must be initialized after SystemIdentity to prevent false violations")

        # Slot health confirmed on successful kernel boot
        confirm_pos = KERNEL_CS.find("BootSlotManager.ConfirmSlotHealth()")
        self.assertGreater(confirm_pos, 0, "BootSlotManager.ConfirmSlotHealth must be called upon successful boot")

    def test_terminal_zupdate_subcommands(self):
        self.assertIn('"status"', CMD_UPDATE)
        self.assertIn('"slots"', CMD_UPDATE)
        self.assertIn('"check"', CMD_UPDATE)
        self.assertIn('"verify"', CMD_UPDATE)
        self.assertIn('"install"', CMD_UPDATE)
        self.assertIn('"rollback"', CMD_UPDATE)
        self.assertIn('"history"', CMD_UPDATE)
        self.assertIn('"drives"', CMD_UPDATE)

    def test_manifest_verification_and_signing(self):
        self.assertIn("VerifySignature", UPDATE_MANIFEST)
        self.assertIn("ComputeHmacSha256", UPDATE_MANIFEST)
        self.assertIn("VerifyPackageIntegrity", UPDATE_MGR)


if __name__ == "__main__":
    unittest.main()
