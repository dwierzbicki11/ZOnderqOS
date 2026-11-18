import os
import pathlib
import re
import subprocess
import sys
import tempfile
import unittest


REPO_ROOT = pathlib.Path(__file__).resolve().parent.parent


def read_repo_file(rel_path: str) -> str:
    return (REPO_ROOT / rel_path).read_text(encoding="utf-8")


def method_body(text: str, signature_prefix: str) -> str:
    idx = text.find(signature_prefix)
    if idx < 0:
        raise AssertionError(f"Signature not found: {signature_prefix}")
    brace = text.find("{", idx)
    if brace < 0:
        raise AssertionError(f"Method body brace not found for: {signature_prefix}")
    depth = 0
    for pos in range(brace, len(text)):
        ch = text[pos]
        if ch == "{":
            depth += 1
        elif ch == "}":
            depth -= 1
            if depth == 0:
                return text[idx : pos + 1]
    raise AssertionError(f"Unbalanced braces for: {signature_prefix}")


class TestCommandParserAndPipeline(unittest.TestCase):
    def test_env_expansion_inside_tokenize_not_before_split(self):
        src = read_repo_file("source/Shell/Command.cs")
        run_body = method_body(src, "public static void Run(")
        self.assertNotIn("EnvironmentExpander.Expand", run_body)
        self.assertIn("SplitOutsideQuotes(fullInput, \";\")", run_body)

        tok_body = method_body(src, "private static string[] Tokenize(")
        self.assertIn("FlushExpandBuffer", tok_body)
        flush_body = method_body(src, "private static void FlushExpandBuffer(")
        self.assertIn("EnvironmentExpander.Expand(", flush_body)

    def test_backslash_escape_disabled_inside_single_quotes(self):
        src = read_repo_file("source/Shell/Command.cs")
        for fn in (
            "private static int FindRedirection(",
            "private static List<string> SplitOutsideQuotes(",
            "private static string[] Tokenize(",
        ):
            body = method_body(src, fn)
            self.assertIn("c == '\\\\' && !inSingleQuotes", body, fn)
            self.assertIn("Unclosed quote in command.", body, fn)

    def test_pipeline_saves_and_restores_nested_input(self):
        src = read_repo_file("source/Shell/Command.cs")
        pipe_body = method_body(src, "private static void ExecutePipeline(")
        self.assertIn("CommandIO.SaveInput();", pipe_body)
        self.assertIn("CommandIO.RestoreInput();", pipe_body)
        self.assertNotIn("CommandIO.SetInput(null)", pipe_body)

    def test_new_shell_commands_registered(self):
        src = read_repo_file("source/Shell/Command.cs")
        init_body = method_body(src, "public static void Initialize()")
        for cmd in ("CmdFind", "CmdTail", "CmdWc", "CmdWhich"):
            self.assertIn(f"new {cmd}()", init_body)


class TestShellCommandGuards(unittest.TestCase):
    def test_root_privilege_guards_for_system_commands(self):
        root_guarded = [
            "source/Shell/Commands/CmdHexdump.cs",
            "source/Shell/Commands/CmdKill.cs",
            "source/Shell/Commands/CmdKillall.cs",
            "source/Shell/Commands/CmdReboot.cs",
            "source/Shell/Commands/CmdShutDown.cs",
            "source/Shell/Commands/CmdChmod.cs",
            "source/Shell/Commands/CmdMount.cs",
            "source/Shell/Commands/CmdUmount.cs",
        ]
        for rel in root_guarded:
            src = read_repo_file(rel)
            self.assertIn("IsAuthenticated", src, rel)
            self.assertIn("CurrentUid != 0", src, rel)
            self.assertIn("LastCommandSuccess = false", src, rel)

    def test_cmd_firewall_guards_and_validation(self):
        src = read_repo_file("source/Shell/Commands/CmdFirewall.cs")
        self.assertIn("HasRootPrivilege()", src)
        for sub in ('"on"', '"strict"', '"off"', '"reset"', '"add"'):
            self.assertIn(sub, src)
        self.assertGreaterEqual(src.count("HasRootPrivilege()"), 5)
        self.assertIn("Invalid protocol", src)
        self.assertIn("Invalid firewall action", src)

    def test_cmd_update_guards_and_ed25519_text(self):
        src = read_repo_file("source/Shell/Commands/CmdUpdate.cs")
        self.assertIn("RequireRootPrivilege", src)
        for subcmd in ('"apply"', '"rollback"', '"stage"'):
            self.assertIn(subcmd, src)
        self.assertNotIn("DEV-KEY", src)
        self.assertNotIn("HMAC-SHA256", src)
        self.assertIn("SHA-256 / Ed25519", src)

    def test_cmd_integrity_auth_root_baseline_and_acl_redaction(self):
        src = read_repo_file("source/Shell/Commands/CmdIntegrity.cs")
        self.assertIn("!SecurityContext.IsAuthenticated", src)
        self.assertIn("HasRootPrivilege()", src)
        self.assertIn("PermissionManager.CanRead(r.Path, SecurityContext.CurrentUser)", src)
        self.assertIn("[RESTRICTED]", src)

    def test_cmd_chmod_and_cmd_audit_guards(self):
        chmod_src = read_repo_file("source/Shell/Commands/CmdChmod.cs")
        self.assertIn("VirtualFs.IsVirtualPath(path)", chmod_src)
        self.assertIn("VirtualFs.IsReadOnlyPath(path)", chmod_src)
        self.assertIn("MaxAclPathLength", chmod_src)

        audit_src = read_repo_file("source/Shell/Commands/CmdAudit.cs")
        self.assertIn("!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0", audit_src)
        self.assertIn("PermissionManager.CanRead(logPath, SecurityContext.CurrentUser)", audit_src)

    def test_cmd_mount_umount_virtual_path_rejection(self):
        for rel in ("source/Shell/Commands/CmdMount.cs", "source/Shell/Commands/CmdUmount.cs"):
            src = read_repo_file(rel)
            self.assertIn("VirtualFs.IsVirtualPath(mountPoint)", src, rel)
            self.assertIn('mountPoint == "/"', src, rel)

    def test_cmd_find_bounded_and_acl_filtered(self):
        src = read_repo_file("source/Shell/Commands/CmdFind.cs")
        self.assertIn("!SecurityContext.IsAuthenticated", src)
        self.assertIn("TryResolveBoundedPath", src)
        self.assertIn("MaxParentSegments", src)
        self.assertIn("MaxVisitedDirectories", src)
        self.assertIn("PermissionManager.CanRead", src)

    def test_shell_completion_containment_and_acl(self):
        src = read_repo_file("source/Shell/ShellCompletion.cs")
        self.assertIn("TryResolveContainedDirectory", src)
        self.assertIn("MaxCompletionParentSegments", src)
        self.assertIn("PermissionManager.CanRead(lookupDirectory, currentUser)", src)
        self.assertIn("PermissionManager.CanRead(childPath, currentUser)", src)

    def test_cmd_cp_mv_write_append_acl_and_virtualfs(self):
        for rel in (
            "source/Shell/Commands/CmdCp.cs",
            "source/Shell/Commands/CmdMv.cs",
            "source/Shell/Commands/CmdWrite.cs",
            "source/Shell/Commands/CmdAppend.cs",
        ):
            src = read_repo_file(rel)
            self.assertIn("!SecurityContext.IsAuthenticated", src, rel)
            self.assertIn("VirtualFs.IsReadOnlyPath", src, rel)
            self.assertIn("PermissionManager.CanCreate", src, rel)

        self.assertIn("CopyPermissionsUnder", read_repo_file("source/Shell/Commands/CmdCp.cs"))
        self.assertIn("MovePermissionsUnder", read_repo_file("source/Shell/Commands/CmdMv.cs"))

    def test_cmd_df_overflow_safe_and_partition_fallback(self):
        src = read_repo_file("source/Shell/Commands/CmdDf.cs")
        self.assertIn("ResolveTotalBytes(VfsMount mount)", src)
        self.assertIn("SafeMultiplyBytes", src)
        self.assertIn("DataVolumePolicy.IsSelectedDataPartition", src)


class TestDiskAndSecurityHardening(unittest.TestCase):
    def test_disk_acl_and_root_checks(self):
        src = read_repo_file("source/Storage/Disk.cs")
        for fn in (
            "public static void CreateFile(",
            "public static void AppendFile(",
            "public static void CreateDir(",
            "public static void CopyFile(",
            "public static void MoveFile(",
        ):
            self.assertIn("PermissionManager.CanCreate(", method_body(src, fn), fn)

        del_dir = method_body(src, "public static void DeleteDir(")
        self.assertIn("PermissionManager.CanDeleteRecursive(", del_dir)
        self.assertIn("PermissionManager.RemovePermissionsUnder(", del_dir)

        tree_body = method_body(src, "public static void Tree(")
        self.assertIn("PermissionManager.CanRead(path", tree_body)
        self.assertIn("PermissionManager.CanRead(file", tree_body)
        self.assertIn("PermissionManager.CanRead(dir", tree_body)

        stat_body = method_body(src, "public static void FileStat(")
        self.assertIn("PermissionManager.CanRead(path", stat_body)

        hex_body = method_body(src, "public static void HexDump(")
        self.assertIn("!SecurityContext.IsAuthenticated || SecurityContext.CurrentUid != 0", hex_body)

    def test_permission_manager_default_acls(self):
        src = read_repo_file("source/Security/PermissionManager.cs")
        for entry in (
            "/|root|755",
            "/home|root|755",
            "/root|root|700",
            "/tmp|root|777",
            "/etc/shadow|root|600",
            "/etc/acl.map|root|600",
        ):
            self.assertIn(entry, src)
        self.assertIn("StringComparer.OrdinalIgnoreCase", src)
        self.assertIn("MaxAclEntries = 4096", src)

    def test_user_manager_security_contracts(self):
        src = read_repo_file("source/Security/UserManager.cs")
        self.assertIn("ValidateCredentialsChars", src)
        self.assertIn("Crypto.FixedTimeHexEquals", src)
        self.assertIn("UserProfileManager.EnsureProfile(username, homeDir);", src)
        profile_src = read_repo_file("source/Security/UserProfileManager.cs")
        self.assertIn("EnsurePermission(home, owner, 700);", profile_src)


class TestGuiSecurityAndLifecycle(unittest.TestCase):
    def test_nano_app_hardening(self):
        src = read_repo_file("source/GUI/Application/NanoApp.cs")
        save_body = method_body(src, "private void Save()")
        self.assertIn('filePath.IndexOf(".."', save_body)
        self.assertIn("VirtualFs.IsReadOnlyPath(filePath)", save_body)
        self.assertIn("PermissionManager.CanCreate(filePath, SecurityContext.CurrentUser)", save_body)
        self.assertIn("CommandIO.LastCommandSuccess", save_body)
        close_body = method_body(src, "public override void Close()")
        self.assertIn("lines.Clear();", close_body)

    def test_security_center_app_auth_and_root_gates(self):
        src = read_repo_file("source/GUI/Application/SecurityCenterApp.cs")
        self.assertIn("PermissionManager.CanRead(logPath, SecurityContext.CurrentUser)", src)
        self.assertIn("RequireRootSession(", src)
        self.assertGreaterEqual(src.count("RequireRootSession("), 6)

    def test_pass_vault_app_v3_hmac_ctr_and_interactive_pin(self):
        src = read_repo_file("source/GUI/Application/PassVaultApp.cs")
        self.assertIn('"/root/.zonderq-vault.enc"', src)
        self.assertIn('"/home/" + owner + "/.zonderq-vault.enc"', src)
        self.assertIn("ZONDERQ_VAULT_v3", src)
        self.assertIn("Crypto.ComputeHmacSha256", src)
        self.assertIn("CipherTransformCtr", src)
        self.assertNotIn('"9999"', src)

        unlock_body = method_body(src, "private void UnlockVault()")
        self.assertIn("if (!DecryptVaultData(candidatePin))", unlock_body)
        self.assertLess(
            unlock_body.index("if (!DecryptVaultData(candidatePin))"),
            unlock_body.index("isUnlocked = true;"),
        )

        mouse_body = method_body(src, "public override void HandleMouse(")
        # RegisterActivity must be inside click branches, not triggered unconditionally on hover
        before_click = mouse_body.split("if (rightClicked", 1)[0]
        self.assertNotIn("RegisterActivity", before_click)
        self.assertIn("detailsX + 235, bodyY + 86", mouse_body)
        self.assertIn("detailsX + 150, bodyY + 86", mouse_body)
        self.assertIn("detailsX + detailsW - 95, bodyY + bodyH - 34", mouse_body)

    def test_user_accounts_notepad_imageviewer_and_per_user_apps(self):
        ua_src = read_repo_file("source/GUI/Application/UserAccountsApp.cs")
        self.assertIn("ClearInputState();", method_body(ua_src, "public override void Close()"))

        np_src = read_repo_file("source/GUI/Application/NotepadApp.cs")
        close_now = method_body(np_src, "private void CloseNow()")
        self.assertIn("dirty = false;", close_now)
        self.assertIn("ForceClose();", method_body(np_src, "public override void Close()"))
        self.assertIn("PermissionManager.CanRead", np_src)

        iv_src = read_repo_file("source/GUI/Application/ImageViewerApp.cs")
        self.assertIn("closeCallback?.Invoke();", method_body(iv_src, "public override void Close()"))
        self.assertIn("CanReadImageInSession", iv_src)

        qn_src = read_repo_file("source/GUI/Application/QuickNotesApp.cs")
        self.assertIn("ResolveNotesDirectory()", qn_src)
        self.assertIn('"/home/" + user', qn_src)
        self.assertIn("PermissionManager.CanCreate", qn_src)

        ss_src = read_repo_file("source/GUI/Application/SpriteStudioApp.cs")
        self.assertIn("GetUserDataDirectory()", ss_src)
        self.assertIn("TryPrepareWritePath", ss_src)
        self.assertIn("PermissionManager.CanRead", ss_src)

        sb_src = read_repo_file("source/GUI/Application/SysBenchmarkApp.cs")
        self.assertIn("benchmarkDir", sb_src)
        self.assertIn("PermissionManager.CanCreate", sb_src)
        self.assertIn("PermissionManager.CanRead", sb_src)

        hv_src = read_repo_file("source/GUI/Application/HexViewerApp.cs")
        self.assertIn("NormalizeSafePath", hv_src)
        self.assertIn("PermissionManager.CanRead(normalized", hv_src)

    def test_system_updater_app_root_busy_reboot_and_wording(self):
        src = read_repo_file("source/GUI/Application/SystemUpdaterApp.cs")
        self.assertIn("RequireRootAccess", src)
        for fn in (
            "private void PerformInstall()",
            "private void PerformRollback()",
            "private void PerformReboot()",
        ):
            body = method_body(src, fn)
            self.assertIn("isBusy", body, fn)
            self.assertIn("RequireRootAccess", body, fn)
        self.assertIn("SystemPower.Reboot", method_body(src, "private void PerformReboot()"))
        self.assertNotIn("HMAC", src)
        self.assertNotIn("DEV-KEY", src)

    def test_gui_manager_and_auth_screens_640x480_and_clearing(self):
        gui_src = read_repo_file("source/GUI/GuiManager.cs")
        self.assertIn("LockScreenManager.ClearSessionResources();", gui_src)
        self.assertIn("IconManager.ClearSessionCache();", gui_src)
        self.assertIn("if (mode != 0 && wallpaperCanvas != null)", gui_src)
        self.assertIn("wallpaperCanvas = null;", gui_src)

        icon_src = read_repo_file("source/GUI/Icons/IconManager.cs")
        self.assertIn("public static void ClearSessionCache()", icon_src)

        for rel in (
            "source/GUI/LoginScreenManager.cs",
            "source/GUI/InitialSetupManager.cs",
            "source/GUI/LockScreenManager.cs",
        ):
            src = read_repo_file(rel)
            self.assertIn("screenHeight < 560", src, rel)
            self.assertIn("l.Compact = compact;", src, rel)
            self.assertIn("Array.Clear(", src, rel)
            self.assertIn("ClearSensitiveData", src, rel)


class TestBootSlotBootGuardUsbAndReleaseBundle(unittest.TestCase):
    def test_boot_slot_manager_atomic_save_rollback_and_dual_arch_elf(self):
        src = read_repo_file("source/Core/Updates/BootSlotManager.cs")
        self.assertIn("# ZonderqOS A/B Bootloader State Configuration", src)
        self.assertIn("BOOTGUARD_PREBOOT=", src)
        self.assertIn("BOOTGUARD_FALLBACK=", src)
        self.assertIn("const int expectedMachine = 183;", src)
        self.assertIn("const int expectedMachine = 62;", src)
        self.assertIn("HasValidElfHeaderFile", src)

        rb_body = method_body(src, "public static bool ManualRollback(")
        self.assertIn("previousSlot == ActiveSlot", rb_body)
        self.assertIn("HasValidElfHeaderFile(kernelPath)", rb_body)
        self.assertIn("ActiveSlot = previousSlot;", rb_body)
        self.assertIn("FallbackSlot = failedSlot;", rb_body)

        save_body = method_body(src, "public static void SaveBootState()")
        self.assertIn("BootStateTempPath", save_body)
        self.assertIn("BootStateBackupPath", save_body)
        self.assertIn("File.Move(BootStateTempPath, BootStatePath)", save_body)

    def test_usb_scripts_and_bootguard_efi_parity(self):
        for rel in ("tools/deploy-to-usb.sh", "tools/prepare-zonderq-usb.sh"):
            src = read_repo_file(rel)
            self.assertIn("# ZonderqOS A/B Bootloader State Configuration", src, rel)
            self.assertIn("BOOTGUARD_PREBOOT=", src, rel)
            self.assertIn("BOOTGUARD_FALLBACK=", src, rel)
            self.assertIn("/ZonderqOS Slot A", src, rel)
            self.assertIn("/ZonderqOS Slot B", src, rel)
            self.assertIn("slot_a/ZonderqOS.elf", src, rel)
            self.assertIn("slot_b/ZonderqOS.elf", src, rel)
            self.assertIn("32 MiB", src, rel)
        self.assertIn("32 * 1024 * 1024", read_repo_file("tools/deploy-to-usb.sh"))
        self.assertIn("DATA_START * 512", read_repo_file("tools/prepare-zonderq-usb.sh"))

        efi_src = read_repo_file("tools/bootguard/bootguard_efi.c")
        self.assertIn("EFI_FILE_MODE_READ", efi_src)
        self.assertIn("is_read_only", efi_src)
        self.assertIn("clear_one_shot()", efi_src)
        self.assertIn("phnum <= 256u && phentsize <= 4096u", efi_src)

    def test_verify_release_bundle_symlinks_isascii_and_verify_before_walk(self):
        src = read_repo_file("tools/verify-release-bundle.py")
        self.assertIn(".isascii()", src)
        self.assertIn("os.path.islink", src)
        main_body = src[src.index("def main():") :]
        verify_pos = main_body.index('openssl("verify"')
        walk_pos = main_body.index("check_files(bundle")
        self.assertLess(verify_pos, walk_pos)

        # Functional test: verify-release-bundle.py must reject symlinked bundle/manifest
        script = REPO_ROOT / "tools/verify-release-bundle.py"
        with tempfile.TemporaryDirectory() as tmp:
            tmp_path = pathlib.Path(tmp)
            real_bundle = tmp_path / "real_bundle"
            real_bundle.mkdir()
            (real_bundle / "update.manifest").write_text("VERSION=1.0.0\n", encoding="utf-8")
            (real_bundle / "update.manifest.sig").write_bytes(b"\x00" * 64)
            pub = tmp_path / "pub.pem"
            pub.write_text("-----BEGIN PUBLIC KEY-----\nMCowBQYDK2VwAyEA\n-----END PUBLIC KEY-----\n", encoding="utf-8")

            link_bundle = tmp_path / "link_bundle"
            os.symlink(real_bundle, link_bundle)
            proc = subprocess.run(
                [sys.executable, str(script), "verify", "--bundle", str(link_bundle), "--public-key", str(pub)],
                capture_output=True,
                text=True,
            )
            self.assertNotEqual(proc.returncode, 0)
            self.assertIn("Symlink not permitted", proc.stderr)


if __name__ == "__main__":
    unittest.main()
