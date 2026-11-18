#!/usr/bin/env python3
"""Source-contract checks for low-allocation idle paths.

These checks protect the production code structure. They are not kernel/QEMU
measurements and cannot establish absence of a memory leak.
"""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
UPDATER = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text()
GUARDIAN = (ROOT / "source/Core/MemoryGuardian.cs").read_text()
LOCK_SCREEN = (ROOT / "source/GUI/LockScreenManager.cs").read_text()
SERVICE_MGR = (ROOT / "source/Core/Services/ServiceManager.cs").read_text()
FLASH_ISO = (ROOT / "zonderq-wgraj-iso.sh").read_text()
DEPLOY_USB = (ROOT / "tools/deploy-to-usb.sh").read_text()


class IdleMemoryBackgroundContracts(unittest.TestCase):
    def test_manual_checks_still_persist_last_check_time(self):
        self.assertIn(
            "return CheckForUpdatesCore(forceRefresh, true, out package, out message);",
            UPDATER,
        )
        self.assertIn(
            "if (persistCheckTime)\n                    SaveConfig();",
            UPDATER,
        )

    def test_background_checks_do_not_rewrite_settings_each_poll(self):
        self.assertIn(
            "CheckForUpdatesCore(false, false, out pkg, out msg);", UPDATER
        )
        self.assertNotIn(
            "bool found = CheckForUpdates(false, out pkg, out msg);", UPDATER
        )
        self.assertIn("backgroundManifestScanCompleted", UPDATER)

    def test_manifest_candidate_paths_are_reused(self):
        self.assertIn(
            "private static readonly string[] ManifestCandidatePaths", UPDATER
        )
        self.assertIn(
            "for (int p = 0; p < ManifestCandidatePaths.Length; p++)", UPDATER
        )
        self.assertNotIn("string[] candidatePaths = new string[]", UPDATER)

    def test_guardian_timestamp_formatting_is_lazy(self):
        self.assertIn(
            "private static DateTime lastCheckTimeUtc = DateTime.MinValue;",
            GUARDIAN,
        )
        self.assertIn("lastCheckTimeUtc = now;", GUARDIAN)
        self.assertIn("FormatTime(checkedAt)", GUARDIAN)
        self.assertNotIn("lastCheckTime = FormatTime(now);", GUARDIAN)
        self.assertNotIn('Disk.AppendFile("/sysmon.log"', GUARDIAN)
        self.assertNotIn("new FileInfo(logPath)", GUARDIAN)

    def test_lock_screen_samples_telemetry_and_avoids_idle_full_redraw(self):
        self.assertIn("SerialMemoryTelemetry.SampleIfDue", LOCK_SCREEN)
        self.assertIn("cursorDamageRenderer.TryMove(mouseX, mouseY)", LOCK_SCREEN)
        self.assertNotIn("bool heartbeat = frame % 134 == 0;", LOCK_SCREEN)

    def test_service_manager_skips_continuous_fat32_appends_after_initial_entry(self):
        self.assertIn("if (wroteInitialHeartbeat)\n                        skipDiskAppend = true;", SERVICE_MGR)

    def test_usb_iso_flasher_syncs_ab_slots_on_zonderqdata(self):
        self.assertIn("/boot/slot_a/ZonderqOS.elf", FLASH_ISO)
        self.assertIn("/boot/slot_b/ZonderqOS.elf", FLASH_ISO)
        self.assertIn("ACTIVE_SLOT", FLASH_ISO)
        self.assertIn('! sudo test -s "$MOUNT_DATA$FALLBACK_SLOT_ELF"', FLASH_ISO)

    def test_deploy_to_usb_preserves_existing_fallback_slot(self):
        self.assertIn('ACTIVE_SLOT="$(printf', DEPLOY_USB)
        self.assertIn('[[ ! -s "$MOUNT_PATH/boot/$FALLBACK_DIR/ZonderqOS.elf" ]]', DEPLOY_USB)
        self.assertIn("BOOTGUARD_PREBOOT=$BOOTGUARD_PREBOOT", DEPLOY_USB)

    def test_no_forced_gc_was_added(self):
        self.assertNotIn("GC.Collect(", GUARDIAN)
        self.assertNotIn("GarbageCollector.Collect(", GUARDIAN)
        self.assertNotIn("GC.Collect(", UPDATER)
        self.assertNotIn("GC.Collect(", LOCK_SCREEN)


if __name__ == "__main__":
    unittest.main()
