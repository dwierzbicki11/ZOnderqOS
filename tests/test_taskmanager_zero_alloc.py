#!/usr/bin/env python3
"""Source-contract checks for P2 RAM, Task Manager, and telemetry zero-alloc paths."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
TASK_MGR = (ROOT / "source/GUI/Application/TaskManagerModernApp.cs").read_text()
SMALL_TEXT = (ROOT / "source/GUI/SmallTextRenderer.cs").read_text()
SERIAL_TEL = (ROOT / "source/Platform/X64/SerialMemoryTelemetry.cs").read_text()
CPU_X64 = (ROOT / "source/Platform/X64/CpuHardwareInfo.cs").read_text()
CPU_ARM64 = (ROOT / "source/Platform/Arm64/CpuHardwareInfo.cs").read_text()


class TaskManagerZeroAllocContracts(unittest.TestCase):
    def test_task_manager_reuses_scratch_and_recycles_rows(self):
        self.assertIn("private const int InitialRowPool = 12;", TASK_MGR)
        self.assertIn("private sealed class ReusableScratch", TASK_MGR)
        self.assertIn("sharedScratchInUse = false;", TASK_MGR)
        self.assertIn("row.Reset();", TASK_MGR)
        self.assertIn("cachedCpuStringsFor", TASK_MGR)

    def test_task_manager_skips_thread_slot_scan_when_not_needed(self):
        self.assertIn("bool needThreadSlots =", TASK_MGR)
        self.assertIn("schedulerThreadCount = totalThreads;", TASK_MGR)
        self.assertIn("totalThreads = SchedulerInfo.ThreadCount;", TASK_MGR)

    def test_task_manager_uses_zero_alloc_render_helpers_and_gates_cache(self):
        self.assertNotIn("memoryCache.UpdateExtended(", TASK_MGR)
        self.assertIn("includeDetail, includeFullPoolLabels", TASK_MGR)
        for helper in (
            "DrawPageBreakdown",
            "DrawByteSizeSmart",
            "DrawMemoryDeltaPages",
            "DrawMiBOrNa",
            "DrawGcCollections",
        ):
            self.assertIn(f"SmallTextRenderer.{helper}(", TASK_MGR)
            self.assertIn(f"public static void {helper}(", SMALL_TEXT)

    def test_cpu_hardware_info_detect_is_cached(self):
        self.assertIn("private static CpuHardwareInfo cachedInfo;", CPU_X64)
        self.assertIn("if (cachedInfo != null)", CPU_X64)
        self.assertIn("private static CpuHardwareInfo cachedInfo;", CPU_ARM64)
        self.assertIn("if (cachedInfo != null && cachedCpuCount == managedCpuCount)", CPU_ARM64)

    def test_serial_telemetry_reports_threads_without_heap_or_disk_io(self):
        self.assertIn("SchedulerDiagnostics.ThreadCount", SERIAL_TEL)
        self.assertIn('" threads="', SERIAL_TEL)
        self.assertIn("WriteUInt(threadCount);", SERIAL_TEL)
        self.assertNotIn(".ToString(", SERIAL_TEL)
        self.assertNotIn("string.Format(", SERIAL_TEL)
        self.assertNotIn("GC.Collect(", SERIAL_TEL)
        self.assertNotIn("GarbageCollector.Collect(", SERIAL_TEL)
        self.assertNotIn("Disk.", SERIAL_TEL)


if __name__ == "__main__":
    unittest.main()
