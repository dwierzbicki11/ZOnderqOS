#!/usr/bin/env python3
"""P0 fail-closed update gate regression checks (static contract).

This is not an end-to-end QEMU security test. The install entry point must
refuse all legacy HMAC updates until an independently reviewed asymmetric
manifest and payload verifier is integrated.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
SOURCE = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text(encoding="utf-8")


class UpdateTrustGateTests(unittest.TestCase):
    def test_unconfigurable_gate_stays_disabled(self):
        self.assertIn("return ReleaseSignatureVerifier.HasProductionKey();", SOURCE)
        key = (ROOT / "source/Core/Updates/TrustedReleaseKey.cs").read_text(encoding="utf-8")
        self.assertIn("string result = string.Empty;", key)
        self.assertIn("SupplyPublicKey(ref result);", key)

    def test_gate_precedes_all_update_install_steps(self):
        method = SOURCE.split("public static bool ApplyUpdateStep(", 1)[1]
        guard = method.index("if (!TryVerifyTrustedPackage(pkg, out error))")
        first_install_step = method.index("switch (step)")
        self.assertLess(guard, first_install_step)
        guarded_branch = method[guard:first_install_step]
        self.assertIn("Status = UpdateStatus.Error;", guarded_branch)
        self.assertIn("return false;", guarded_branch)
        self.assertIn("SecurityLogger.LogEvent", guarded_branch)

    def test_every_stage_uses_single_guarded_entry(self):
        method = SOURCE.split("public static bool ApplyUpdateStep(", 1)[1]
        method = method.split("public static bool ApplyUpdate(", 1)[0]
        self.assertRegex(method, r"case 0:")
        self.assertRegex(method, r"case 5:")
        apply = SOURCE.split("public static bool ApplyUpdate(", 1)[1]
        self.assertIn("ApplyUpdateStep(", apply)

    def test_pending_ab_update_cannot_overwrite_fallback(self):
        boot = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text(encoding="utf-8")
        staging = boot.split("public static bool StageKernelToInactiveSlot(", 1)[1].split(
            "public static bool ActivateSlot(", 1)[0]
        stage_guard = staging.index("if (!string.IsNullOrEmpty(PendingSlot))")
        target_selection = staging.index("string targetSlot = GetInactiveSlot();")
        self.assertLess(stage_guard, target_selection)
        activation = boot.split("public static bool ActivateSlot(", 1)[1].split(
            "public static bool ManualRollback(", 1)[0]
        self.assertIn("targetSlot == ActiveSlot || !string.IsNullOrEmpty(PendingSlot)", activation)
        self.assertIn("HasValidX64ElfHeader(candidate)", activation)
        self.assertIn('Path.Combine(Path.GetDirectoryName(kernelPath), "kernel.sha256")', activation)

    def test_slot_activation_restores_state_on_write_failure(self):
        boot = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text(encoding="utf-8")
        activate = boot.split("public static bool ActivateSlot(", 1)[1].split(
            "public static bool ManualRollback(", 1)[0]
        self.assertIn("string previousActive = ActiveSlot;", activate)
        self.assertIn("string previousFallback = FallbackSlot;", activate)
        self.assertIn("ActiveSlot = previousActive;", activate)
        self.assertIn("FallbackSlot = previousFallback;", activate)
        self.assertIn("SyncLimineConfig();\n                        SaveBootState();", activate)
        self.assertIn("NIE UDALO SIE ODTWORZYC KONFIGURACJI ROZRUCHU", activate)

    def test_backup_cleanup_does_not_rollback_committed_kernel(self):
        boot = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text(encoding="utf-8")
        stage = boot.split("public static bool StageKernelToInactiveSlot(", 1)[1].split(
            "public static bool ActivateSlot(", 1)[0]
        self.assertIn('try { File.Delete(backupPath); }', stage)
        self.assertIn('Old inactive kernel backup retained:', stage)

    def test_no_unrestricted_root_payload_deployment(self):
        self.assertNotIn("private static void DeployDirectory(", SOURCE)
        self.assertNotIn('DeployDirectory(payloadDir, "/",', SOURCE)
        self.assertIn("DeployManifestFiles(pkg, payloadDir, backupPayload);", SOURCE)
        self.assertIn("RestoreManifestFiles(backupPayloadDir);", SOURCE)

    def test_deployment_is_allowlisted_and_manifest_bound(self):
        self.assertIn('relativePath.StartsWith("system/"', SOURCE)
        self.assertIn('relativePath.StartsWith("usr/"', SOURCE)
        self.assertIn("UpdateManifest.IsSafeRelativePath(relativePath)", SOURCE)
        self.assertIn("FileAttributes.ReparsePoint", SOURCE)
        self.assertIn('Directory.GetFiles(payloadDir, "*", SearchOption.AllDirectories)', SOURCE)
        self.assertIn("if (!declared.Contains(relative)", SOURCE)
        self.assertIn("File.WriteAllText(createdRecord", SOURCE)
        self.assertIn("string previousCreated = Path.Combine(BackupDir", SOURCE)

    def test_package_never_falls_back_to_foreign_kernel(self):
        kernel = SOURCE.split("public static string FindKernelSource(", 1)[1].split(
            "public static bool CheckForUpdates(", 1)[0]
        self.assertIn("if (pkg != null)", kernel)
        self.assertLess(kernel.index("if (pkg != null)"), kernel.index("string[] candidates"))
        payload = SOURCE.split("public static string FindPayloadDirectory(", 1)[1].split(
            "public static StorageEnvironmentInfo", 1)[0]
        self.assertIn("if (pkg != null)", payload)

    def test_forgeable_embedded_hmac_release_key_has_been_removed(self):
        manifest = (ROOT / "source/Core/Updates/UpdateManifest.cs").read_text(encoding="utf-8")
        self.assertNotIn("ZONDERQ_OFFICIAL_RELEASE_KEY_GEN3", manifest)
        self.assertNotIn("DefaultSignerKey", manifest)
        self.assertNotIn("GetSignaturePayload()", manifest)
        self.assertNotIn("ComputeHmacSha256(secretKey", manifest)
        self.assertNotIn("manifest.VerifySignature(", SOURCE)

    def test_kernel_fallback_restores_shared_files_before_services(self):
        boot = (ROOT / "source/Core/Updates/BootSlotManager.cs").read_text(encoding="utf-8")
        kernel = (ROOT / "Kernel.cs").read_text(encoding="utf-8")
        self.assertIn("UpdateManager.RestoreSharedFilesAfterBootFailure(FallbackSlot", boot)
        self.assertIn("if (ZonderqOS.SystemCore.Updates.BootSlotManager.EmergencyRecoveryNeeded)", kernel)
        self.assertIn("PREVIOUS_OS_RELEASE_PRESENT=", SOURCE)
        self.assertIn("PREVIOUS_UPDATE_CONFIG_PRESENT=", SOURCE)

    def test_explicit_trust_requirement(self):
        self.assertIn("ReleaseSignatureVerifier.VerifyProductionManifest(bytes, signature)", SOURCE)
        self.assertIn("parsed.UpdateFormat != 2", SOURCE)
        self.assertIn("if (!VerifyPackageIntegrity(pkg, null, out error))", SOURCE)


if __name__ == "__main__":
    unittest.main()
