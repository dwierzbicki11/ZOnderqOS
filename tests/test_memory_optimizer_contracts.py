#!/usr/bin/env python3
"""Regression contracts for the production 10-second Memory Optimizer.

Source-level tests only: these do NOT prove QEMU or physical-hardware stability.
"""
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]
POLICY = (ROOT / "source/Core/MemoryOptimizer.cs").read_text()
GUARD = (ROOT / "source/Core/MemoryGuardian.cs").read_text()
GUI = (ROOT / "source/GUI/GuiManager.cs").read_text()
ICONS = (ROOT / "source/GUI/Icons/IconManager.cs").read_text()
TASK = (ROOT / "source/GUI/Application/TaskManagerModernApp.cs").read_text()
SERIAL = (ROOT / "source/Platform/X64/SerialMemoryTelemetry.cs").read_text()


class MemoryOptimizerContracts(unittest.TestCase):
    def test_policy_is_bounded_and_samples_every_ten_seconds(self):
        self.assertIn("SampleIntervalSeconds = 10", POLICY)
        self.assertIn("TrimCooldownSeconds = 120", POLICY)
        self.assertIn("GrowthThresholdBytes = 8UL * 1024UL * 1024UL", POLICY)
        self.assertIn("MaxColdBuffersPerSweep = 16", POLICY)
        self.assertIn("nowTicks - lastSampleTicks < sampleInterval", POLICY)
        self.assertIn("nowTicks - lastRequestTicks < cooldown", POLICY)

    def test_uses_existing_guardian_not_new_thread(self):
        self.assertIn("ProcessManager.Start(\"sys_guardian\"", GUARD)
        self.assertIn("MemoryOptimizer.SampleIfDue(", GUARD)
        self.assertIn("token.WaitHandle.WaitOne(2000)", GUARD)
        self.assertIn("(guardianTick++ & 1) == 0", GUARD)
        self.assertNotIn("new Thread(", POLICY)
        self.assertNotIn("ProcessManager.Start(", POLICY)

    def test_never_forces_gc_or_frees_live_pages(self):
        for code in (POLICY, GUARD, GUI):
            self.assertIsNone(re.search(r"\b(?:GC|Heap|GarbageCollector)\s*\.\s*Collect\s*\(", code))
        for code in (POLICY,):
            self.assertNotIn("FreePages(", code)
            self.assertNotIn("AllocPages(", code)

    def test_background_worker_never_mutates_renderer(self):
        self.assertNotIn("IconManager", POLICY)
        self.assertNotIn("IconManager", GUARD)
        self.assertIn("MemoryOptimizer.TryTakeGuiTrimRequest()", GUI)
        self.assertIn("IconManager.TrimColdScaledCache(", GUI)
        self.assertIn("MemoryOptimizer.RecordGuiSweep(", GUI)

    def test_gc_caches_are_only_cold_scaled_buffers(self):
        self.assertIn("scaledTouchedSinceSweep", ICONS)
        self.assertIn("TrimColdScaledCache(int maxBuffers, out ulong candidateBytes)", ICONS)
        sweep = ICONS.split("internal static int TrimColdScaledCache", 1)[1].split(
            "private static bool ShouldCoalesce", 1
        )[0]
        self.assertIn("!scaledTouchedSinceSweep[read]", sweep)
        self.assertIn("removed < maxBuffers", sweep)
        self.assertIn("scaledPixels[i] = null;", sweep)
        self.assertNotIn("sourcePixels[", sweep)
        self.assertIn("System.Array.Clear(scaledRecentSlots", sweep)
        self.assertIn("System.Array.Clear(scaledPreviousSlots", sweep)

    def test_gui_session_lifecycle_preserves_ownership(self):
        self.assertIn("MemoryOptimizer.SetGuiReady(true)", GUI)
        self.assertIn("MemoryOptimizer.SetGuiReady(false)", GUI)
        self.assertIn("Interlocked.Exchange(ref pendingTrim, 0)", POLICY)
        self.assertIn("Interlocked.CompareExchange(ref pendingTrim, 0, 1)", POLICY)

    def test_zero_disk_io_and_no_dynamic_buffer_on_sampling_path(self):
        sample = POLICY.split("public static void SampleIfDue(", 1)[1].split(
            "public static bool TryTakeGuiTrimRequest(", 1
        )[0]
        for prohibited in ("File.", "Directory.", "new byte[", "new int[", "StringBuilder", "Console.Write"):
            self.assertNotIn(prohibited, sample)

    def test_metrics_do_not_claim_immediate_physical_reclamation(self):
        self.assertIn("unrootedCandidateBytes", POLICY)
        self.assertIn("public readonly struct MemoryOptimizerSnapshot", POLICY)
        self.assertIn("candidateBytes", POLICY)
        self.assertIn("MEMOPT PROBKI", TASK)
        self.assertIn("MEMOPT ZADANIA", TASK)
        self.assertIn("ODPIETE BUFORY", TASK)
        self.assertIn("GC KANDYDAT KiB", TASK)
        self.assertIn("mo_checks=", SERIAL)
        self.assertIn("mo_candidate_kb=", SERIAL)

    def test_no_periodic_cache_flush_without_growth_or_pressure(self):
        self.assertIn("bool growing =", POLICY)
        self.assertIn("bool criticalPressure =", POLICY)
        self.assertIn("(!growing && !criticalPressure)", POLICY)
        self.assertIn("baselineUsedPages = usedPages;", POLICY)


if __name__ == "__main__":
    unittest.main()
