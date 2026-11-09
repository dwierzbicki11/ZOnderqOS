#!/usr/bin/env python3
"""Regression contracts for Task Manager memory label caching and zero idle allocations."""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
TM_SOURCE = (ROOT / "source/GUI/Application/TaskManagerModernApp.cs").read_text(encoding="utf-8")
CACHE_SOURCE = (ROOT / "source/GUI/Application/TaskManagerMemoryCache.cs").read_text(encoding="utf-8")
GUI_SOURCE = (ROOT / "source/GUI/GuiManager.cs").read_text(encoding="utf-8")


class TaskManagerIdleMemoryContracts(unittest.TestCase):
    def test_task_manager_delegates_to_memory_cache(self):
        self.assertIn("memoryCache.Update(", TM_SOURCE)
        self.assertIn("internal string UsedMemoryText => memoryCache.UsedMemoryText;", TM_SOURCE)
        self.assertIn("internal string FreeMemoryText => memoryCache.FreeMemoryText;", TM_SOURCE)
        self.assertIn("internal string TotalMemoryText => memoryCache.TotalMemoryText;", TM_SOURCE)
        self.assertIn("internal string GcHeapText => memoryCache.GcHeapText;", TM_SOURCE)
        self.assertIn("internal string GcCommittedText => memoryCache.GcCommittedText;", TM_SOURCE)

    def test_cache_updates_strictly_on_displayed_precision_boundary(self):
        self.assertIn("if (usedMiB != lastDisplayedUsedMiB || usedHundredths != lastDisplayedUsedHundredths)", CACHE_SOURCE)
        self.assertIn("if (freeMiB != lastDisplayedFreeMiB || freeHundredths != lastDisplayedFreeHundredths)", CACHE_SOURCE)
        self.assertIn("if (scaledUsage != lastDisplayedScaledUsage)", CACHE_SOURCE)
        self.assertIn("if (usedChanged || usageChanged)", CACHE_SOURCE)

    def test_gc_heap_displays_na_when_telemetry_unavailable(self):
        self.assertIn('public string GcHeapText { get; private set; } = "N/A";', CACHE_SOURCE)
        self.assertIn('public string GcCommittedText { get; private set; } = "N/A";', CACHE_SOURCE)
        self.assertIn('GcHeapText = "N/A";', CACHE_SOURCE)
        self.assertIn('GcCommittedText = "N/A";', CACHE_SOURCE)

    def test_draw_graph_has_no_delegate_allocations(self):
        self.assertIn("private void DrawGraph(Canvas canvas, int x, int y, int width, int height, int count, int graphKind, Color line, bool labels)", TM_SOURCE)
        self.assertNotIn("HistoryGetter", TM_SOURCE)
        self.assertNotIn("delegate", TM_SOURCE)

    def test_render_memory_has_no_per_frame_string_concatenation(self):
        match = re.search(r'private void RenderMemory\(Canvas canvas, int x, int y, int width\)(.*?)\n        }', TM_SOURCE, re.S)
        self.assertIsNotNone(match)
        body = match.group(1)
        self.assertNotIn(' + "', body)
        self.assertNotIn('" + ', body)
        self.assertNotIn('.ToString(', body)
        self.assertIn('app.InstalledRamText', body)
        self.assertIn('app.UsableRamText', body)
        self.assertIn('app.HeapPoolText', body)
        self.assertIn('app.MemoryPagesText', body)
        self.assertIn('app.MemoryDetailText', body)
        self.assertIn('app.FreeMemoryText', body)
        self.assertIn('app.GcHeapText', body)
        self.assertIn('app.GcCommittedText', body)

    def test_close_tears_down_row_and_snapshot_pools(self):
        match = re.search(r'public override void Close\(\)(.*?)\n        }', TM_SOURCE, re.S)
        self.assertIsNotNone(match)
        body = match.group(1)
        self.assertIn("rowPool.Clear();", body)
        self.assertIn("allRows.Clear();", body)
        self.assertIn("visibleRows.Clear();", body)
        self.assertIn("processSnapshot.Clear();", body)

    def test_single_instance_activation_prevents_multiple_running_instances(self):
        self.assertIn("if (applicationManager.Applications[i] is TaskManagerModernApp existing && existing.IsRunning)", GUI_SOURCE)
        self.assertIn("applicationManager.Activate(existing);", GUI_SOURCE)

    def test_graph_history_has_constant_capacity(self):
        self.assertIn("private const int HistoryLength = 120;", TM_SOURCE)
        self.assertIn("internal int HistoryCapacity => HistoryLength;", TM_SOURCE)


if __name__ == "__main__":
    unittest.main()
