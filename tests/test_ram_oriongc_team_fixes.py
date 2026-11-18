#!/usr/bin/env python3
"""Regression contracts for integrated RAM / OrionGC / GUI / Services fixes."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


def read(rel: str) -> str:
    return (ROOT / rel).read_text(encoding="utf-8")


class IntegratedRamOptimizationContracts(unittest.TestCase):
    def test_memory_optimizer_guards_backward_ticks_and_resets_session_deltas(self):
        src = read("source/Core/MemoryOptimizer.cs")
        reset_body = src.split("public static void SetGuiReady(", 1)[1].split(
            "public static void SampleIfDue(", 1
        )[0]
        self.assertIn("lastRequestTicks = 0;", reset_body)
        self.assertIn("lastSampleTicks = 0;", reset_body)
        self.assertIn("lastUsedPages = 0;", reset_body)
        self.assertIn("lastDeltaPages = 0;", reset_body)

        sample_body = src.split("public static void SampleIfDue(", 1)[1].split(
            "public static bool TryTakeGuiTrimRequest(", 1
        )[0]
        self.assertIn("if (nowTicks < lastRequestTicks)", sample_body)
        self.assertIn("lastRequestTicks = nowTicks;", sample_body)

    def test_icon_trim_skips_when_idle_unrendered_and_keeps_hints_on_noop(self):
        src = read("source/GUI/Icons/IconManager.cs")
        sweep = src.split("internal static int TrimColdScaledCache", 1)[1].split(
            "private static bool ShouldCoalesce", 1
        )[0]
        self.assertIn("anyTouchedSinceLastSweep", sweep)
        self.assertIn("if (!anyTouchedSinceLastSweep)", sweep)
        self.assertIn("if (removed > 0)", sweep)
        self.assertLess(sweep.index("if (removed > 0)"), sweep.index("System.Array.Clear(scaledRecentSlots"))

    def test_about_startmenu_filemanager_settings_kerneladvanced_avoid_frame_allocations(self):
        about = read("source/GUI/Application/AboutApp.cs")
        about_render = about.split("public override void Render(", 1)[1]
        self.assertIn("if (!string.Equals(cachedVersionValue, curVer", about_render)
        self.assertIn("UiTypography.Caption(canvas, cachedVersionCaption", about_render)
        self.assertIn("if (!string.Equals(cachedBuildValue, curBuild", about_render)
        self.assertIn("UiTypography.Caption(canvas, cachedBuildCaption", about_render)

        start = read("source/GUI/StartMenu.cs")
        render_tools = start.split("private void RenderTools(", 1)[1].split(
            "for (int i = 0; i < toolButtons.Count; i++)", 1
        )[0]
        self.assertNotIn('(toolScrollRow + 1) + "/" + toolTotalRows', render_tools)
        self.assertIn("SmallTextRenderer.DrawUInt(", render_tools)
        self.assertIn('SmallTextRenderer.Draw(canvas, "/"', render_tools)

        fm = read("source/GUI/Application/FileManagerApp.cs")
        dialog = fm.split("private void Dialog(Canvas canvas)", 1)[1]
        self.assertNotIn("app.DialogName + \"_\"", dialog)
        self.assertIn('SmallTextRenderer.Draw(canvas, "_"', dialog)

        settings = read("source/GUI/Application/SettingsApp.cs")
        launch_tm = settings.split("private void LaunchTaskManager()", 1)[1].split(
            "private void CycleProfile()", 1
        )[0]
        self.assertIn("existing.IsRunning", launch_tm)
        self.assertIn("applicationManager.Activate(existing)", launch_tm)

        ka = read("source/GUI/Application/KernelAdvancedApp.cs")
        render_hw = ka.split("private void RenderHardware(", 1)[1].split(
            "private void UpdateHardwareStrings(", 1
        )[0]
        self.assertIn("cachedTopologyText", render_hw)
        self.assertIn("cachedCacheText", render_hw)
        self.assertNotIn("FormatHardwareBytes(", render_hw)

    def test_cmdsysmond_and_systemlogger_avoid_periodic_fat32_and_string_churn(self):
        sysmond = read("source/Shell/Commands/CmdSysmond.cs")
        self.assertNotIn("File.AppendAllText", sysmond)
        self.assertNotIn("new FileInfo", sysmond)
        self.assertNotIn("File.Exists", sysmond)
        self.assertNotIn("WaitHandle", sysmond)

        logger = read("source/Core/SystemLogger.cs")
        ts = logger.split("private static string GetTimestamp()", 1)[1]
        self.assertNotIn('ToString("D4")', ts)
        self.assertNotIn('ToString("D2")', ts)
        self.assertIn("TimestampChars", ts)
        self.assertIn("message.IndexOf('\\r') < 0", logger)

    def test_serial_telemetry_guards_missing_bare_metal_com1(self):
        serial = read("source/Platform/X64/SerialMemoryTelemetry.cs")
        self.assertIn("com1Unavailable", serial)
        self.assertIn("lsr == 0xFF", serial)
        self.assertIn("com1TimeoutStreak", serial)

    def test_application_manager_drops_stale_mouse_capture_on_removal(self):
        app_mgr = read("source/GUI/Application/ApplicationManager.cs")
        self.assertIn("if (mouseCapture != null && (!mouseCapture.IsRunning ||", app_mgr)

    def test_measure_scenarios_supports_output_x64_and_ram_flag(self):
        bench = read("tools/measure_scenarios.py")
        self.assertIn("output-x64/ZonderqOS.iso", bench)
        self.assertIn('parser.add_argument("--ram"', bench)
        self.assertIn('"-m", ram', bench)

    def test_lock_unlock_avoids_fat32_and_pbkdf2_heap_churn(self):
        lock = read("source/GUI/LockScreenManager.cs")
        self.assertIn("ReusableScreen.Reset(", lock)
        self.assertIn("UserManager.ValidateCredentialsChars(", lock)
        self.assertNotIn("new LockScreen(", lock.split("public static bool Run(", 1)[1].split("private sealed class LockScreen", 1)[0])
        self.assertNotIn("new string(password, 0, passwordLength)", lock)

        user_mgr = read("source/Security/UserManager.cs")
        self.assertIn("private struct CachedShadowEntry", user_mgr)
        self.assertIn("Crypto.VerifyPasswordV2(", user_mgr)
        self.assertIn("InvalidateCredentialCache();", user_mgr)

        crypto = read("source/Common/Crypto.cs")
        self.assertIn("public static bool VerifyPasswordV2(", crypto)
        self.assertIn("PbkdfSchedule", crypto)

        sec_log = read("source/Security/SecurityLogger.cs")
        self.assertIn("internal static void LogSessionLockEvent(", sec_log)
        self.assertIn("SystemLogger.LogBuffered(", sec_log)
        self.assertIn("SystemLogger.GetTimestampCached()", sec_log)
        self.assertNotIn('ToString("D4")', sec_log)

        sys_log = read("source/Core/SystemLogger.cs")
        self.assertIn("public static void LogBuffered(", sys_log)
        self.assertIn("PendingDiskEntries", sys_log)
        self.assertIn("public static void Flush()", sys_log)

        gui = read("source/GUI/GuiManager.cs")
        run_lock = gui.split("private bool RunLockScreen()", 1)[1].split("private void ResetActivityTimer()", 1)[0]
        self.assertIn("SecurityLogger.LogSessionLockEvent(cachedLockLogMessage)", run_lock)
        self.assertIn("SecurityLogger.LogSessionLockEvent(cachedUnlockLogMessage)", run_lock)

        notif = read("source/Core/NotificationService.cs")
        self.assertIn("FormatMinuteCached(", notif)
        self.assertNotIn('ToString("HH:mm")', notif)

        power = read("source/Core/BootSessionHealth.cs")
        self.assertIn("SecurityLogger.Flush();", power)
        self.assertIn("SystemLogger.Flush();", power)

    def test_loggers_batch_pending_flushes_and_lockscreen_caches_unlock_failures(self):
        sys_log = read("source/Core/SystemLogger.cs")
        self.assertIn("PendingFlushBuilder", sys_log)
        self.assertIn("FlushPendingBatchLocked", sys_log)
        self.assertIn("flushToDisk: level >= SystemLogLevel.Error", sys_log)

        sec_log = read("source/Security/SecurityLogger.cs")
        self.assertIn("PendingAuditBuilder", sec_log)
        self.assertIn("FlushPendingBatchLocked", sec_log)
        self.assertIn("internal static void LogSessionUnlockFailure(string message, bool flushImmediately)", sec_log)

        lock = read("source/GUI/LockScreenManager.cs")
        self.assertIn("SecurityLogger.LogSessionUnlockFailure(cachedFailedUnlockLog, retryAfter > 0);", lock)
        self.assertIn("private void EnsureCachedUserLogs()", lock)
        self.assertIn("private string GetLockStatus(int retryAfter)", lock)

        settings = read("source/GUI/Application/SettingsApp.cs")
        audit_status = settings.split("private void ReadAuditStatus()", 1)[1].split("private ", 1)[0]
        self.assertIn("SecurityLogger.Flush();", audit_status)

    def test_native_scratch_buffer_frees_physical_pages_on_app_close(self):
        nsb = read("source/Core/NativeScratchBuffer.cs")
        self.assertIn("public const int MinUnmanagedBytes = 4096;", nsb)
        self.assertIn("NativeMemory.AllocZeroed((nuint)elementCount, (nuint)sizeof(T))", nsb)
        self.assertIn("NativeMemory.Free(toFree);", nsb)

        paint = read("source/GUI/Application/PaintApp.cs")
        self.assertIn("canvasBuffer.Allocate(CanvasWidth * CanvasHeight);", paint)
        self.assertIn("canvasBuffer.Dispose();", paint)
        self.assertIn("floodQueue.Allocate(maxPixels);", paint)
        self.assertIn("floodQueue.Dispose();", paint)

        bmp = read("source/GUI/BmpEncoder.cs")
        self.assertIn("chunk.Allocate(chunkBytes);", bmp)
        self.assertIn("chunk.Dispose();", bmp)

        bench = read("source/GUI/Application/SysBenchmarkApp.cs")
        self.assertIn("primeSieve.Allocate(100001);", bench)
        self.assertIn("testRamBuffer.Allocate(RamBufferSize);", bench)
        self.assertIn("primeSieve.Dispose();", bench)
        self.assertIn("testRamBuffer.Dispose();", bench)

        hexv = read("source/GUI/Application/HexViewerApp.cs")
        self.assertIn("fileBuffer.Allocate(", hexv)
        self.assertIn("fileBuffer.Dispose();", hexv)

        secc = read("source/GUI/Application/SecurityCenterApp.cs")
        self.assertIn("logTailBuffer.Allocate(LogTailMaxBytes);", secc)
        self.assertIn("logTailBuffer.Dispose();", secc)
        self.assertIn("cachedRecords.Clear();", secc)

        boot = read("source/Core/Updates/BootSlotManager.cs")
        self.assertIn("copyBuffer.Dispose();", boot)

        pkg = read("source/Core/Packages/PackageManager.cs")
        self.assertIn("copyBuffer.Allocate(16 * 1024);", pkg)
        self.assertIn("copyBuffer.Dispose();", pkg)

    def test_gui_managers_and_apps_reuse_or_clear_managed_state_on_close(self):
        sec_screen = read("source/GUI/SecureSessionScreenManager.cs")
        self.assertIn("private static readonly Screen ReusableScreen = new Screen();", sec_screen)
        self.assertIn("ReusableScreen.Reset((int)canvas.Width, (int)canvas.Height);", sec_screen)

        taskbar = read("source/GUI/Taskbar.cs")
        self.assertIn('private static readonly string[] VolumeLabels = { "VOL 0", "VOL 1", "VOL 2", "VOL 3", "VOL 4", "VOL 5", "VOL 6", "VOL 7", "VOL 8" };', taskbar)

        tm = read("source/GUI/Application/TaskManagerModernApp.cs")
        self.assertIn("private const int PreallocatedLogicalCpus = 16;", tm)
        self.assertIn("private const int PreallocatedThreadSlots = 128;", tm)

        g2048 = read("source/GUI/Application/Game2048App.cs")
        self.assertIn("private readonly int[] scratchLine = new int[GridSize];", g2048)

        notepad = read("source/GUI/Application/NotepadApp.cs")
        self.assertIn("lines.Clear();", notepad)
        self.assertIn("openEntries.Clear();", notepad)

        term = read("source/GUI/Application/Terminal.cs")
        self.assertIn("terminalBox?.ClearForClose();", term)
        tbox = read("source/GUI/TerminalBox.cs")
        self.assertIn("internal void ClearForClose()", tbox)

        imgv = read("source/GUI/Application/ImageViewerApp.cs")
        self.assertIn("private static int[] sharedRxMapScratch;", imgv)


if __name__ == "__main__":
    unittest.main()
