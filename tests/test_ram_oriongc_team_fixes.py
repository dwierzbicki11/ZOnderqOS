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


if __name__ == "__main__":
    unittest.main()
