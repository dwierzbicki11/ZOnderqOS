#!/usr/bin/env python3
"""Lock screen phase tracing is read-only and remains out of hot render loops."""
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]
GUI = (ROOT / "source/GUI/GuiManager.cs").read_text()
LOCK = (ROOT / "source/GUI/LockScreenManager.cs").read_text()
SERIAL = (ROOT / "source/Platform/X64/SerialMemoryTelemetry.cs").read_text()


class LockScreenMemoryPhaseContracts(unittest.TestCase):
    def test_checkpoints_surround_security_disk_logging(self):
        lock_method = GUI.split("private bool RunLockScreen()", 1)[1].split(
            "private void ResetActivityTimer()", 1
        )[0]
        a = lock_method.index('TraceLockPhase("before_lock_log")')
        b = lock_method.index('SecurityLogger.LogEvent("INFO", "Graphical session locked')
        c = lock_method.index('TraceLockPhase("after_lock_log")')
        self.assertTrue(a < b < c)
        self.assertIn('TraceLockPhase("after_unlock_log")', lock_method)

    def test_checkpoints_surround_first_render(self):
        self.assertIn('TraceLockPhase("after_lock_init")', LOCK)
        self.assertIn('TraceLockPhase("after_first_lock_paint")', LOCK)
        self.assertIn('TraceLockPhase("before_lock_exit")', LOCK)
        init = LOCK.index('TraceLockPhase("after_lock_init")')
        paint = LOCK.index('TraceLockPhase("after_first_lock_paint")')
        self.assertLess(init, paint)

    def test_trace_avoids_allocation_and_disk_writes(self):
        trace = SERIAL.split("public static void TraceLockPhase(string phase)", 1)[1].split(
            "public static void SampleIfDue(", 1
        )[0]
        for bad in ("new ", "File.", "Directory.", "SystemLogger.", "SecurityLogger.",
                    "GC.Collect(", "Heap.Collect("):
            self.assertNotIn(bad, trace)
        self.assertIn('WriteAscii("[LOCK_MEM] phase=")', trace)
        self.assertIn('WriteAscii(" used_p=")', trace)
        self.assertIn('WriteAscii(" gc_comm_kb=")', trace)

    def test_serial_diagnostics_only_on_x64(self):
        self.assertIn("#if !ZONDERQ_ARCH_ARM64\n            global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.TraceLockPhase", GUI)
        self.assertIn("#if !ZONDERQ_ARCH_ARM64\n                global::ZonderqOS.Platform.X64.SerialMemoryTelemetry.TraceLockPhase", LOCK)

    def test_idle_lock_screen_avoids_full_screen_redraw(self):
        self.assertIn("else if (keyboardActivity || pointerMoved || buttonChanged)", LOCK)
        self.assertIn("cursorDamageRenderer.TryMove(mouseX, mouseY)", LOCK)
        self.assertIn("SerialMemoryTelemetry.SampleIfDue(", LOCK)


if __name__ == "__main__":
    unittest.main()
