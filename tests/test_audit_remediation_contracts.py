#!/usr/bin/env python3
"""Regression contracts for the 4-wave ZonderqOS RAM, Process, Security & UI Audit."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


class AuditRemediationContracts(unittest.TestCase):
    def test_c1_auto_lock_uses_buffered_log_and_wallpaper_uses_static_cache(self):
        gui = read("source/GUI/GuiManager.cs")
        auto_lock_block = gui.split("else if (ShouldAutoLock())", 1)[1].split("bool leftForUi", 1)[0]
        self.assertNotIn("SecurityLogger.LogEvent(", auto_lock_block)
        self.assertIn("SecurityLogger.LogSessionLockEvent(cachedIdleLockLogMessage)", auto_lock_block)
        self.assertIn("private static Canvas sharedWallpaperCanvas;", gui)
        self.assertIn("sharedWallpaperWidth == width", gui)
        self.assertNotIn('"/root/.zonderq-wallpapers"', gui)

    def test_c2_installer_hidden_on_installed_systems(self):
        gui = read("source/GUI/GuiManager.cs")
        reg = gui.split("private void InitializeAppRegistry()", 1)[1].split("private void PopulateStartMenu()", 1)[0]
        self.assertIn("if (global::ZonderqOS.Disk.IsLiveSession)", reg)
        self.assertLess(reg.index("if (global::ZonderqOS.Disk.IsLiveSession)"), reg.index("AppIds.Installer"))

    def test_c3_c8_permission_manager_enforces_ancestors_and_skips_noop_writes(self):
        pm = read("source/Security/PermissionManager.cs")
        self.assertIn("CanTraverseExplicitAncestors", pm)
        self.assertIn("if (!CanTraverseExplicitAncestors(path, user)) return false;", pm)
        self.assertIn("existing.Perms == perms", pm)

    def test_c4_sysreport_and_config_backup_enforce_auth_and_acl(self):
        sr = read("source/Shell/Commands/CmdSysReport.cs")
        self.assertIn("if (!SecurityContext.IsAuthenticated)", sr)
        self.assertIn("PermissionManager.CanCreate(path, SecurityContext.CurrentUser)", sr)

        ss = read("source/Core/SystemSettings.cs")
        backup = ss.split("public static bool BackupTo(", 1)[1].split("public static bool ", 1)[0]
        self.assertIn("if (!SecurityContext.IsAuthenticated)", backup)
        self.assertIn("PermissionManager.CanCreate(normalizedDestination, user)", backup)

    def test_c5_c6_disk_and_installer_root_and_fail_closed_live_guards(self):
        disk = read("source/Storage/Disk.cs")
        self.assertIn("if (!liveCmdline && !opticalLiveMedia)", disk)
        self.assertNotIn('!PermissionManager.GetPermission(path).Owner.Equals(SecurityContext.CurrentUser', disk)

        dfe = read("source/Storage/DiskFormattingEngine.cs")
        self.assertIn("EnsureRootPrivilege(out error)", dfe)
        self.assertIn("FindRawMbrSlotByStartLba", dfe)
        self.assertIn("FormatPartitionWithIdentity", dfe)

        sie = read("source/Storage/SystemInstallerEngine.cs")
        self.assertIn("SecurityContext.CurrentUid != 0", sie)
        self.assertIn("/etc/acl.map", sie)
        self.assertNotIn("/etc/security/permissions.db", sie)
        self.assertNotIn('EfiMountPoint + "/boot/limine/limine.conf"', sie)
        self.assertIn("(slotAHandle as IDisposable)?.Dispose();", sie)

    def test_c7_system_logger_timestamp_is_thread_safe(self):
        sl = read("source/Core/SystemLogger.cs")
        ts = sl.split("private static string GetTimestamp()", 1)[1]
        self.assertIn("lock (TimestampSync)", ts)

    def test_c9_c10_h3_no_idle_60fps_or_static_screen_heartbeats(self):
        bench = read("source/GUI/Application/SysBenchmarkApp.cs")
        close_body = bench.split("public override void Close()", 1)[1].split("private void EnsureBuffersAllocated()", 1)[0]
        self.assertIn("SystemDiagnosticRunner.Shared.RequestAbort();", close_body)

        demo = read("source/GUI/Application/Demo3DApp.cs")
        self.assertIn("private bool isRotating = false;", demo)

        sol = read("source/GUI/Application/SolitaireApp.cs")
        self.assertIn("public override bool NeedsContinuousRedraw => false;", sol)

        pv = read("source/GUI/Application/PassVaultApp.cs")
        self.assertIn("public override bool NeedsContinuousRedraw => false;", pv)
        self.assertIn("ZONDERQ_VAULT_v4", pv)
        self.assertIn("Crypto.HashPasswordV2", pv)

        for rel in (
            "source/GUI/LoginScreenManager.cs",
            "source/GUI/SecureSessionScreenManager.cs",
            "source/GUI/InitialSetupManager.cs",
        ):
            self.assertNotIn("bool heartbeat = frame % 134 == 0;", read(rel), rel)

    def test_m1_m2_m6_security_and_settings_validation(self):
        scp = read("source/Security/SensitiveCommandPolicy.cs")
        self.assertIn("inSingle", scp)
        self.assertIn("inDouble", scp)

        ag = read("source/Security/AuthenticationGuard.cs")
        self.assertIn('string.Equals(users[candidate], "root", StringComparison.Ordinal)', ag)

        ss = read("source/Core/SystemSettings.cs")
        self.assertIn('case "language":', ss)
        self.assertIn("number <= 6;", ss)
        self.assertIn("number <= 7;", ss)

    def test_h11_h12_m10_ui_hitboxes_and_english_strings(self):
        ss_app = read("source/GUI/Application/ScreenshotApp.cs")
        self.assertIn("canvas.DrawRectangle(Border,", ss_app)
        self.assertNotIn("Rozdzielczosc", ss_app)

        welcome = read("source/GUI/Application/WelcomeApp.cs")
        self.assertNotIn("LocalizationManager.SetLanguage(global::ZonderqOS.Localization.Language.Polish)", welcome)

        lock = read("source/GUI/LockScreenManager.cs")
        self.assertNotIn('"ODBLOKUJ"', lock)
        self.assertIn("ClearSessionState()", lock)


if __name__ == "__main__":
    unittest.main()
