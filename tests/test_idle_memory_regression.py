#!/usr/bin/env python3
"""Static regression contracts for the Gen3 desktop idle-memory fixes.

These tests verify source wiring only; they are NOT a substitute for a live
Cosmos/OrionGC heap-stability test in QEMU or on physical hardware.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
UPDATER = (ROOT / "source/Core/Updates/UpdateManager.cs").read_text(encoding="utf-8")
MANAGER = (ROOT / "source/GUI/Application/TaskManagerModernApp.cs").read_text(encoding="utf-8")


def method_body(source: str, signature: str) -> str:
    start = source.find(signature)
    if start < 0:
        raise AssertionError(f"Method missing: {signature}")
    open_brace = source.find("{", start + len(signature))
    if open_brace < 0:
        raise AssertionError(f"Method body missing: {signature}")
    depth = 0
    for offset in range(open_brace, len(source)):
        if source[offset] == "{":
            depth += 1
        elif source[offset] == "}":
            depth -= 1
            if depth == 0:
                return source[open_brace + 1:offset]
    raise AssertionError(f"Unbalanced method body: {signature}")


class IdleMemorySourceContracts(unittest.TestCase):
    def test_background_check_does_not_persist_config_on_each_poll(self):
        auto = method_body(UPDATER, "public static void PollBackground()")
        core = method_body(UPDATER, "private static bool CheckForUpdatesCore(")
        explicit = method_body(UPDATER, "public static bool CheckForUpdates(")

        self.assertIn("CheckForUpdatesCore(false, false,", auto)
        self.assertNotIn("SaveConfig(", auto)
        self.assertIn("CheckForUpdatesCore(forceRefresh, true,", explicit)
        self.assertRegex(core, r"if\s*\(persistCheckTime\)\s*SaveConfig\(\);")

    def test_idle_update_check_is_rate_limited_and_handles_clock_regression(self):
        auto = method_body(UPDATER, "public static void PollBackground()")
        self.assertIn("AutoPollIntervalSeconds = 60", UPDATER)
        self.assertIn("now >= lastAutoCheckTicks", auto)
        self.assertIn("AutoPollIntervalSeconds * freq", auto)
        self.assertIn("lastAutoCheckTicks = now;", auto)

    def test_taskmanager_closing_is_idempotent_and_clears_snapshots(self):
        close = method_body(MANAGER, "public override void Close()")
        self.assertIn("if (!IsRunning)", close)
        self.assertIn("base.Close();", close)
        self.assertIn("processSnapshot.Clear();", close)
        self.assertIn("allRows.Clear();", close)
        self.assertIn("visibleRows.Clear();", close)
        self.assertIn("row.GuiApplication = null;", close)
        self.assertIn("rowPool.Clear();", close)

    def test_memory_labels_refresh_only_when_their_counter_changes(self):
        snap = method_body(MANAGER, "private void UpdateMemorySnapshot()")
        self.assertRegex(snap, r"if\s*\(usedChanged\)\s*cachedUsedMemoryText")
        self.assertRegex(snap, r"if\s*\(freeChanged\)\s*cachedFreeMemoryText")
        self.assertRegex(snap, r"if\s*\(gcHeapChanged\)\s*cachedGcHeapText")
        self.assertRegex(snap, r"if\s*\(installedChanged\)\s*cachedInstalledRamText")
        self.assertRegex(snap, r"if\s*\(installedChanged\s*\|\|\s*totalChanged\)")
        self.assertIn("lastSnapUsedPages = usedPages;", snap)

    def test_bounded_history_and_no_forced_gc(self):
        self.assertIn("private const int HistoryLength = 120;", MANAGER)
        self.assertIn("history[write] = value;", MANAGER)
        self.assertNotIn("GC.Collect(", MANAGER)
        self.assertNotIn("CosmosGc.Collect(", MANAGER)


if __name__ == "__main__":
    unittest.main()
